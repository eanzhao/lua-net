# 第 5 步补充：CLOSE 与块作用域上值关闭

## 状态

已完成当前这一轮。

这一轮继续沿着上值生命周期往前推，目标是把块作用域结束时的提前关闭路径接起来，也就是 `OP_CLOSE`。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lfunc.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心指令是：

- `OP_CLOSE`

官方语义很直接：

- `close all upvalues >= R[A]`

也就是从寄存器 `A` 开始，把这一段及其后面的打开状态上值全部收成独立值。

## 本步骤范围

本轮先落这几件事：

- 在 `CallFrame` 中支持“按寄存器下界关闭上值”
- 在 VM 中支持 `CLOSE`
- 用真实 Lua 5.5 chunk 验证块作用域结束后的提前关闭

## 设计原则

### 1. 先解决寄存器复用串值问题

`CLOSE` 这条路径最核心的价值，不是补齐一个 opcode 名单，而是解决一个很实际的问题：

- 块作用域里的局部变量已经离开作用域
- 后面新的局部变量可能复用同一个寄存器
- 如果原来的上值还连着旧寄存器，闭包就会读到错误的新值

所以这一轮重点就是把这个问题钉住。

### 2. 先做最小生命周期分段

现在主线里的上值生命周期已经有两段：

- 帧结束时统一关闭
- 块结束时按寄存器下界提前关闭

这已经足够支撑一批真实 Lua 5.5 闭包场景。

## 当前支持范围

这一轮新增支持：

- `CLOSE`
- 按寄存器下界关闭打开状态上值
- 块作用域退出后的安全寄存器复用

当前还没有进入这些内容：

- `TBC`
- to-be-closed 变量的关闭方法调用
- 更完整的资源释放语义

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/close_chunk.lua`
- `test/fixtures/lua55/chunks/close_chunk.luac`

这份脚本覆盖的路径是：

- 在块作用域里创建一个被闭包捕获的局部变量
- 离开块作用域后触发 `CLOSE`
- 用新的局部变量复用原寄存器
- 验证闭包仍然读到旧值，而不是寄存器复用后的新值

## 实现清单

- [x] 编写本轮文档
- [x] 在 `CallFrame` 中支持按寄存器下界关闭上值
- [x] 支持 `CLOSE`
- [x] 新增真实 `close_chunk.luac`
- [x] 新增 `CLOSE` 对应 VM 测试

## 完成标准

本轮完成后，应满足：

- 块作用域结束时可以提前关闭相关上值
- 后续寄存器复用不会污染已经捕获的变量
- `CLOSE` 能跑通真实 Lua 5.5 fixture

## 下一步

接下来继续往下补：

- `TBC` 的 `nil/false` 快速路径已经拆到 `docs/012-step-05-tbc.md`
- `__close` 与 to-be-closed 生命周期第一版已经拆到 `docs/013-step-05-close-metamethod.md`
- 更完整的调用协议
- `LOADF`、`LOADKX`、`EXTRAARG` 的剩余路径
- 元方法调度
