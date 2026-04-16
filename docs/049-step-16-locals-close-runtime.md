# 049 Step 16: `locals.lua` 兼容性推进与 close/runtime 语义修正

## 本轮目标

在接入官方 Lua 5.5.0 测试集以后，`locals.lua` 先后暴露出多组源码编译器与运行时缺口：

- `goto` / `label` 缺失
- 具名函数调试名缺失，导致 `debug.getinfo(level).name` 不对
- `<close>` 在正常返回、错误展开、泛型 `for` 第四返回值等路径上的语义不对
- `error` / `debug.traceback` 的输出信息过于简化
- 递归没有 Lua 层的 `stack overflow` 保护

这一轮的目标不是一次做完 `locals.lua`，而是把这些真实缺口逐个打穿，并把“当前下一缺口”压缩到更小的范围。

## 主要改动

### 1. 源码编译器补上 `goto` / `label`

在 `LuaCompiler` 里加入了完整的 label 可见性、pending goto 解析、跳入新作用域校验，以及和 `CLOSE` / `break` / `for` 作用域收束配套的补丁逻辑。

这一步把 `locals.lua` 从“完全不支持 goto/label”推进到了真正运行 `<close>` 相关断言的阶段。

### 2. 源码函数现在保留稳定 debug name

给 `LuaPrototype` 增加了非序列化的 `DebugName` 元数据，并在下列编译入口写入名字：

- `local function foo()`
- `global function foo()`
- `function t.f()` / `function t:f()`
- `local foo = function() end`
- `foo = function() end`
- 表字段形式的匿名函数初始化

VM 创建 closure 时会把这个名字带入 `LuaClosure.DebugName`，从而让 `debug.getinfo(...).name` 在源码编译路径上不再退化成 `function@line`。

### 3. `debug` 库与包系统细节补齐

为了让官方脚本直接 `require "debug"`，本轮把标准库表注册到了 `package.preload`。同时补上了 `_G` 到全局环境自身的绑定。

另外，`debug.getupvalue` / `debug.setupvalue` 现在会优先使用 bytecode upvalue 名字，而不是始终回退到 `(upvalue n)`。

### 4. `<close>` 语义修正

围绕 to-be-closed 变量补了几组关键语义：

- 正常离开作用域时，`__close` 只收到 `self`
- 错误展开时，`__close` 才收到第二个 error object
- 泛型 `for` 会接住并关闭第四个返回值（close value）
- 字符串型 close 错误会追加 `in metamethod 'close'`
- 缺失 `__close` 与 `__close` 不是函数会产出不同的 Lua 兼容错误文本
- `<close>` 注册失败时，源码路径可以报出具体变量名，例如 `variable 'x' got a non-closable value`

为此，本轮还额外给 `LuaPrototype` / `LuaClosure` 增加了少量仅运行时使用的非序列化元数据：

- closure 的 `SourceName` / `LineDefined`
- `Tbc` 指令到局部变量名的映射

### 5. 错误与 traceback 输出更接近 Lua

`error(value, level)` 现在开始支持基础的 level 语义：

- 非字符串 error object 维持原样
- 字符串 error object 在 `level > 0` 时会带上 source/line 前缀
- `level == 0` 时不加前缀

同时，`debug.traceback` 默认不再把 `debug.traceback` 自己打进栈里；在 `xpcall(foo, debug.traceback)` 这类路径上，输出已经能对齐官方脚本依赖的基本形状。

### 6. 引入 Lua 层的递归深度保护

VM 现在对调用帧深度做软限制，并在超过阈值时抛出 `stack overflow` 的 Lua 运行时错误，而不是继续递归到 CLR 自己的栈边界。

这让 `locals.lua` 里的 `xpcall(overflow, errorh, 0)` 路径终于能被 Lua 层错误处理器接住，并继续验证高栈位 `<close>` 收尾逻辑。

## 回归测试

本轮新增并收紧了多组编译器集成测试，覆盖：

- 具名函数的 debug name / upvalue name
- 正常与异常路径的 `<close>` 参数个数
- 泛型 `for` 的第四返回值关闭
- 错误展开时 caller 从已废弃 frame 切换到 `pcall`
- 字符串型 close 错误上的 `in metamethod 'close'`
- non-closable value 的 Lua 兼容错误文本
- 深递归转成 `stack overflow`

另外，官方 compatibility 诊断也更新到了当前事实：`locals.lua` 已经不再停在 goto/label，而是推进到了 return-hook 相关缺口。

## 当前状态

`locals.lua` 现在已经通过了：

- local environments
- local constants
- 大部分 `<close>` 正常/异常展开路径
- non-closable values
- 高栈位 `stack overflow` 清理

当前下一缺口已经收敛到：

- `debug.sethook` / `debug.gethook` 的 return-hook 语义
- 特别是 `__close vs. return hooks in Lua functions` 这一段

也就是说，下一轮不需要再回头补 `<close>` 的基础语义，应该直接集中到 hook 事件建模和 return hook 触发顺序。
