# 第 4 步补充：`VARARG` 与开放结果协议

## 状态

已完成当前这一轮。

这一轮继续沿着 VM 的调用协议往前补，把之前还断着的 vararg 和开放结果路径接上了。

后续在 `docs/020-step-04-vararg-table.md` 里，已经继续补上了具名 vararg 参数和 vararg table 这条路径。

这次真正打通的是这几类场景：

- `...` 读取固定个数结果
- `return ...`
- 一个调用把“所有结果”继续传给下一个调用
- `SETLIST B == 0` 从当前动态栈顶取元素个数

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lobject.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心指令是：

- `VARARG`
- `GETVARG`
- `CALL`
- `TAILCALL`
- `RETURN`
- `SETLIST`
- `VARARGPREP`

## 本步骤范围

本轮先落这几件事：

- 在调用帧里补上 vararg 存储
- 在调用帧里补上第一版动态寄存器顶
- 在 VM 中支持 `VARARG` 的固定结果和 `all out` 路径
- 在 VM 中支持 `GETVARG` 的最小语义
- 在 VM 中支持 `CALL` / `TAILCALL` 的开放参数路径
- 在 VM 中支持 `CALL` / `RETURN` 的开放结果路径
- 在 VM 中补上 `SETLIST B == 0`
- 用真实 Lua 5.5 chunk 验证 vararg、开放调用和开放 `SETLIST`

## 设计原则

### 1. 不照搬 Lua C 栈搬移，先用更直接的 C# 模型承载

官方 Lua 在 `VARARGPREP` 里会重排栈布局，把隐藏参数放到函数对象前面。

这一版 C# 实现没有照搬那套栈搬移，而是直接在 `CallFrame` 里显式保存：

- `Varargs`
- `RegisterTop`

这样做的好处是：

- 代码更容易读
- `VARARG` / `CALL` / `RETURN` / `SETLIST` 可以共享同一套动态结果语义
- 不需要过早把运行时绑死在 C 栈布局上

### 2. 让开放结果只由“生产者”更新动态顶

这一轮把动态寄存器顶约束得比较明确：

- `VARARG all out` 会更新动态顶
- `CALL all out` 会更新动态顶
- 其他普通写寄存器指令不更新动态顶

这样消费方的语义就比较清楚：

- `CALL B == 0`
- `TAILCALL B == 0`
- `RETURN B == 0`
- `SETLIST B == 0`

都会从最近一次开放结果生产出来的动态顶取结果个数。

### 3. 先把 hidden vararg 这条主路径做稳

Lua 5.5 的 vararg 有两条内部表示路线：

- hidden vararg arguments
- vararg table

这一轮先把前者做稳，也就是当前最常见、最直接的 `...` 路径。`GETVARG` 也先支持最小语义：

- 整数索引
- `"n"` 取参数个数

## 当前支持范围

这一轮新增支持：

- `VARARG` 固定结果路径
- `VARARG all out`
- `GETVARG` 最小读取路径
- `CALL all in`
- `CALL all out`
- `TAILCALL all in`
- `RETURN all out`
- `SETLIST B == 0`

当前这一轮还没有展开的是：

- `PF_VATAB` 相关的 vararg table 路径
- 泛化后的多返回值元方法和迭代器链路

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/vararg_fixed_chunk.lua`
- `test/fixtures/lua55/chunks/vararg_fixed_chunk.luac`
- `test/fixtures/lua55/source/vararg_all_chunk.lua`
- `test/fixtures/lua55/chunks/vararg_all_chunk.luac`
- `test/fixtures/lua55/source/open_call_chunk.lua`
- `test/fixtures/lua55/chunks/open_call_chunk.luac`
- `test/fixtures/lua55/source/setlist_open_chunk.lua`
- `test/fixtures/lua55/chunks/setlist_open_chunk.luac`

它们分别覆盖：

- `local a, b, c = ...`
- `return ...`
- `count(pack(...))` 这种开放结果继续喂给下一个调用
- `{pack(4, 5, 6)}` 这种开放结果直接喂给 `SETLIST`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `CallFrame` 中加入 `Varargs`
- [x] 在 `CallFrame` 中加入 `RegisterTop`
- [x] 支持 `VARARG`
- [x] 支持 `GETVARG`
- [x] 支持 `CALL` 开放参数与开放结果路径
- [x] 支持 `TAILCALL` 开放参数路径
- [x] 支持 `RETURN` 开放结果路径
- [x] 支持 `SETLIST B == 0`
- [x] 新增真实 `vararg_fixed_chunk.luac`
- [x] 新增真实 `vararg_all_chunk.luac`
- [x] 新增真实 `open_call_chunk.luac`
- [x] 新增真实 `setlist_open_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- `...` 可以按固定个数读取
- `return ...` 可以返回全部额外参数
- 上一个调用的全部结果可以继续作为下一个调用的实参
- `SETLIST B == 0` 可以按动态顶写入数组部分

## 下一步

接下来继续往下补：

- 更完整的 vararg table 路径
- 迭代器相关 opcode
- 更一般的元方法分发

其中 vararg table 这条线已经在后续一轮推进到 `docs/020-step-04-vararg-table.md`。
