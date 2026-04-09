# 第 4 步补充：循环执行路径

## 状态

已完成当前这一轮。

这一轮继续沿着 VM 主循环往前补，把 Lua 里最常见的一批循环执行路径接上了。

这次补上的重点有两类：

- 数值 `for`
- 泛型 `for`

顺手还把 backward `JMP` 的执行语义修正了，这样 `while` 这类依赖回跳的控制流也能真正跑通。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lparser.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心指令是：

- `FORPREP`
- `FORLOOP`
- `TFORPREP`
- `TFORCALL`
- `TFORLOOP`
- `JMP`

## 本步骤范围

本轮先落这几件事：

- 在 VM 中支持整数数值 `for`
- 在 VM 中支持浮点数值 `for`
- 在 VM 中支持最小泛型 `for`
- 修正 `JMP` 的回跳路径
- 用真实 Lua 5.5 chunk 验证整数 `for`、浮点 `for`、泛型 `for` 和 `while`

## 设计原则

### 1. 先把最常见的循环主路径接通

Lua 的循环语义不止一类：

- 数值 `for`
- 泛型 `for`
- `while`
- `repeat`

这一轮先把最常见、最能带动后续实现的一组路径接通：

- `FORPREP` / `FORLOOP`
- `TFORPREP` / `TFORCALL` / `TFORLOOP`
- backward `JMP`

这样后面再补 `repeat`、迭代器细节和更完整的错误语义时，主干已经是通的。

### 2. 数值 `for` 先对齐整数和浮点两条快速路径

官方 Lua 在数值 `for` 上分成两种执行模型：

- 整数循环
- 浮点循环

这一轮按这个结构拆开实现：

- 整数循环在寄存器里保存剩余计数、步长和控制变量
- 浮点循环在寄存器里保存 limit、step 和当前控制变量

这样和官方模型更接近，也方便后面继续细化边界行为。

### 3. 泛型 `for` 先支持最小迭代闭环

这一轮的泛型 `for` 先支持最小但真实可跑的路径：

- `TFORPREP` 里的控制变量与关闭变量交换
- `TFORCALL` 的迭代器调用
- `TFORLOOP` 的 `nil` 终止判断

配合我们已经有的：

- `CALL`
- `RETURN`
- `CLOSE`
- to-be-closed 变量登记

已经足够跑通真实 Lua 5.5 编译产物里的最小泛型迭代闭环。

## 当前支持范围

这一轮新增支持：

- 整数 `FORPREP`
- 整数 `FORLOOP`
- 浮点 `FORPREP`
- 浮点 `FORLOOP`
- `TFORPREP`
- `TFORCALL`
- `TFORLOOP`
- backward `JMP`

当前这一轮还没有展开的是：

- 更完整的泛型 `for` 关闭值场景
- 标准库迭代器配套能力
- `repeat` / `until`
- `ERRNNIL` 的完整错误信息路径

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/for_integer_chunk.lua`
- `test/fixtures/lua55/chunks/for_integer_chunk.luac`
- `test/fixtures/lua55/source/for_float_chunk.lua`
- `test/fixtures/lua55/chunks/for_float_chunk.luac`
- `test/fixtures/lua55/source/for_generic_chunk.lua`
- `test/fixtures/lua55/chunks/for_generic_chunk.luac`
- `test/fixtures/lua55/source/while_chunk.lua`
- `test/fixtures/lua55/chunks/while_chunk.luac`

它们分别覆盖：

- 整数数值 `for`
- 浮点数值 `for`
- 最小泛型 `for`
- 依赖 backward `JMP` 的 `while`

## 实现清单

- [x] 编写本轮文档
- [x] 支持整数 `FORPREP` / `FORLOOP`
- [x] 支持浮点 `FORPREP` / `FORLOOP`
- [x] 支持 `TFORPREP` / `TFORCALL` / `TFORLOOP`
- [x] 修正 backward `JMP`
- [x] 新增真实 `for_integer_chunk.luac`
- [x] 新增真实 `for_float_chunk.luac`
- [x] 新增真实 `for_generic_chunk.luac`
- [x] 新增真实 `while_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- 整数数值 `for` 可以执行
- 浮点数值 `for` 可以执行
- 最小泛型 `for` 可以执行
- backward `JMP` 可以执行

## 下一步

接下来继续往下补：

- `repeat` / `until`
- 更完整的迭代器与标准库配套
- `ERRNNIL` 的完整错误路径
- 更一般的元方法分发
