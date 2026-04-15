# 第 6 步：一元元方法与最小 `__call`

## 状态

已完成当前这一轮。

这一轮继续沿着元方法主线往前补，把 `UNM`、`BNOT` 和最小 `__call` 路径接上了。

这次主要打通的是：

- table metatable 上的 `__unm`
- table metatable 上的 `__bnot`
- 普通 `CALL` 走 `__call`
- `TAILCALL` 走 `__call`

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ldo.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `OP_UNM`
- `OP_BNOT`
- `OP_CALL`
- `OP_TAILCALL`
- `TM_UNM`
- `TM_BNOT`
- `TM_CALL`

## 本步骤范围

本轮先落这几件事：

- 在 `UNM` 中支持 table metatable 的 `__unm`
- 在 `BNOT` 中支持 table metatable 的 `__bnot`
- 让 `CALL` 在目标不是函数时继续查找 `__call`
- 让 `TAILCALL` 在目标不是函数时继续查找 `__call`
- 用真实 Lua 5.5 chunk 验证普通调用和尾调用两条 `__call` 路径

## 设计原则

### 1. 一元元方法沿用官方的“把同一个值传两次”思路

Lua 5.5 在一元元方法内部其实复用了二元元方法调用入口：

- `__unm`
- `__bnot`

都会把同一个操作数作为两个参数传进去。

这一轮也沿用了这个结构，而不是单独再造一套一元元方法调用器。

### 2. `__call` 先做最小可用链路

官方 VM 在调用非函数值时，会不断尝试把 `__call` 插到调用位上，直到遇到真正的函数，或者链太长报错。

这一轮先补最小但真实可用的版本：

- 非函数值先查 metatable 的 `__call`
- 找到后把原对象插到参数最前面
- 再继续解析，直到遇到真正可调用的函数

这样普通 `CALL` 和 `TAILCALL` 都能直接复用同一套逻辑。

### 3. 先把 table 可调用对象做稳

这一轮没有一次把所有对象种类都做满，而是先补最常见、最适合用真实 chunk 验证的路线：

- table
- metatable
- `__call`

userdata 和更一般的可调用对象，后面再接。

## 当前支持范围

这一轮新增支持：

- `UNM` 的 table `__unm`
- `BNOT` 的 table `__bnot`
- `CALL` 的最小 `__call`
- `TAILCALL` 的最小 `__call`

当前这一轮还没有展开的是：

- userdata 的一元元方法
- userdata 的 `__call`
- 更完整的 `__call` 错误消息与边界细节

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/meta_unm_chunk.lua`
- `test/fixtures/lua55/chunks/meta_unm_chunk.luac`
- `test/fixtures/lua55/source/meta_bnot_chunk.lua`
- `test/fixtures/lua55/chunks/meta_bnot_chunk.luac`
- `test/fixtures/lua55/source/meta_call_chunk.lua`
- `test/fixtures/lua55/chunks/meta_call_chunk.luac`
- `test/fixtures/lua55/source/meta_tailcall_chunk.lua`
- `test/fixtures/lua55/chunks/meta_tailcall_chunk.luac`

它们分别覆盖：

- `-x`
- `~x`
- 普通 `CALL` 的 `__call`
- `TAILCALL` 的 `__call`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `UNM` 中支持 `__unm`
- [x] 在 `BNOT` 中支持 `__bnot`
- [x] 在 `CALL` 中支持最小 `__call`
- [x] 在 `TAILCALL` 中支持最小 `__call`
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- table metatable 上的一元元方法可以通过真实 chunk 被调用
- 普通 `CALL` 和 `TAILCALL` 都能在必要时落到 `__call`
- 调用参数顺序保持 Lua 5.5 的最小语义

## 下一步

接下来继续往下补：

- `__index` / `__newindex`
- userdata 路径
- 更一般的元方法调度
