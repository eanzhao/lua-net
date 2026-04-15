# 第 5 步补充：具名 vararg 参数与 vararg table

## 状态

已完成当前这一轮。

这一轮继续沿着 vararg 主线往前补，把之前还没接上的具名 vararg 参数和 vararg table 路径补上了。

这次主要打通的是：

- `local function f(...args) return args end`
- `local y = ...` 在 vararg table 语义下的取值路径
- `args[2] = 99` 这种对具名 vararg 参数的表写入

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lparser.c`
- `references/lua-5.5.0/src/lcode.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lobject.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `VARARGPREP`
- `VARARG` 的 `k` 路径
- 具名 vararg 参数
- `PF_VATAB`

## 本步骤范围

本轮先落这几件事：

- 在 `VARARGPREP` 中支持创建 vararg table
- 在 `VARARG` 中支持 `k` 路径
- 用真实 Lua 5.5 chunk 验证具名 vararg 参数直接返回
- 用真实 Lua 5.5 chunk 验证 `local y = ...` 的 `VARARG k` 路径
- 用真实 Lua 5.5 chunk 验证具名 vararg 参数的表写入和再读取

## 设计原则

### 1. 区分 hidden vararg 和 vararg table 两条实现路线

Lua 5.5 的 vararg 不只有一条内部表示：

- hidden vararg arguments
- vararg table

前一轮已经把 hidden vararg 的开放结果和 `GETVARG` 最小语义接通了。

这一轮继续把另一条真实会出现的路线补上，也就是：

- 具名 vararg 参数被当作普通局部值使用时
- 编译器会要求函数真正持有一个 vararg table

### 2. `VARARGPREP` 先负责把表建出来

当前这一版 C# VM 没有照搬官方 C 栈搬移，而是直接在调用帧里保存：

- `Varargs`
- `RegisterTop`

所以在 vararg table 这条线里，最直接的做法就是：

- `VARARGPREP` 根据 `CallFrame.Varargs` 创建 `LuaTable`
- 把整数键 `1..n` 和字符串键 `"n"` 写进去
- 再放到具名 vararg 参数对应的寄存器里

### 3. `VARARG k` 只补最小真实读取路径

当函数已经走 vararg table 路线时，编译器会给 `VARARG` 打上 `k` 标记，表示结果来自 vararg table，而不是 hidden args。

这一轮先补最小真实路径：

- 固定结果个数
- `all out`
- 从表里的 `"n"` 取数量

## 当前支持范围

这一轮新增支持：

- `VARARGPREP` 的 vararg table 初始化路径
- `VARARG k`
- 具名 vararg 参数作为普通值返回
- 具名 vararg 参数的表读写路径

当前这一轮还没有展开的是：

- 更复杂的 vararg table 错误恢复
- 更一般的表元方法分发

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/vararg_table_return_chunk.lua`
- `test/fixtures/lua55/chunks/vararg_table_return_chunk.luac`
- `test/fixtures/lua55/source/vararg_table_mix_chunk.lua`
- `test/fixtures/lua55/chunks/vararg_table_mix_chunk.luac`
- `test/fixtures/lua55/source/vararg_table_mutation_chunk.lua`
- `test/fixtures/lua55/chunks/vararg_table_mutation_chunk.luac`

它们分别覆盖：

- `return args`
- `local y = ...; return y, args`
- `args[2] = 99; return args[2], args.n`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `VARARGPREP` 中创建 vararg table
- [x] 支持 `VARARG k`
- [x] 新增真实 `vararg_table_return_chunk.luac`
- [x] 新增真实 `vararg_table_mix_chunk.luac`
- [x] 新增真实 `vararg_table_mutation_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- 具名 vararg 参数可以直接作为表返回
- `local y = ...` 可以在 vararg table 路线上取到第一个参数
- 具名 vararg 参数可以像普通表一样读写

## 下一步

接下来继续往下补：

- 更一般的元方法分发
- 更完整的全局声明与相关语义
- 更多控制流和标准库配套能力
