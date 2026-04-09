# 第 4 步补充：`repeat / until` 与全局声明检查

## 状态

已完成当前这一轮。

这一轮继续沿着控制流和错误路径往前补，主要落了两件事：

- 用真实 chunk 固定 `repeat / until`
- 把 `ERRNNIL` 从占位实现换成真正的 Lua 运行时错误

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lparser.c`
- `references/lua-5.5.0/src/lcode.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ldebug.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `repeat / until`
- backward `JMP`
- `ERRNNIL`
- Lua 5.5 的 `global` 声明保护检查

## 本步骤范围

本轮先落这几件事：

- 用真实 Lua 5.5 chunk 验证 `repeat / until`
- 在 VM 中实现 `ERRNNIL`
- 用真实 Lua 5.5 chunk 验证 `global` 正常声明路径
- 用真实 Lua 5.5 chunk 验证重复定义全局变量时的错误路径

## 设计原则

### 1. `repeat / until` 先用真实 fixture 把现状钉住

这一轮没有为 `repeat / until` 新增专门 opcode。

原因很直接：

- 它在 Lua 5.5 编译产物里主要展开成已有的比较指令和 backward `JMP`
- 之前循环那一轮已经把这条执行路径接通了

所以这一轮最重要的是：

- 用真实 fixture 证明这条路径确实已经可用

### 2. `ERRNNIL` 先对齐最小错误语义

`ERRNNIL` 不是普通索引错误，也不是一般的类型错误。

它对应的是 Lua 5.5 里 `global` 声明语法的保护检查：

- 如果目标全局变量当前是 `nil`，允许定义
- 如果目标全局变量当前不是 `nil`，抛出运行时错误

这一轮先对齐最关键的最小语义：

- 正确判断 `nil / non-nil`
- 正确从常量表里取出全局变量名
- 正确抛出 Lua 运行时异常对象

## 当前支持范围

这一轮新增支持：

- 真实 `repeat / until` 执行闭环
- `ERRNNIL`
- `global` 正常声明路径
- `global` 冲突报错路径

当前这一轮还没有展开的是：

- 更完整的 `global` 语法相关编译路径
- 更复杂的源位置和调试信息拼接

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/repeat_chunk.lua`
- `test/fixtures/lua55/chunks/repeat_chunk.luac`
- `test/fixtures/lua55/source/global_ok_chunk.lua`
- `test/fixtures/lua55/chunks/global_ok_chunk.luac`
- `test/fixtures/lua55/source/global_err_chunk.lua`
- `test/fixtures/lua55/chunks/global_err_chunk.luac`

它们分别覆盖：

- `repeat / until`
- `global answer = 42`
- `answer = 1` 后再 `global answer = 2`

## 实现清单

- [x] 编写本轮文档
- [x] 用真实 `repeat_chunk.luac` 验证 `repeat / until`
- [x] 实现 `ERRNNIL`
- [x] 新增真实 `global_ok_chunk.luac`
- [x] 新增真实 `global_err_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- `repeat / until` 可以通过真实 chunk 验证
- `global` 正常声明可以执行
- 重复定义全局变量会抛出 `global 'name' already defined`

## 下一步

接下来继续往下补：

- vararg table 那条路径
- 更完整的 `global` 语法与编译路径
- 更一般的元方法分发
