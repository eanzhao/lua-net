# 第 5 步：共享上值 Cell 与最小捕获语义

## 状态

已完成当前这一轮。

这一轮正式从“只把闭包挂起来”往前走了一步，开始给闭包真正补上共享上值语义。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lfunc.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点不是某一条单独的指令，而是一整套最小闭包捕获链路：

- `OP_CLOSURE`
- `OP_GETUPVAL`
- `OP_SETUPVAL`

以及“多个闭包共享同一个外层局部变量”这件事本身。

## 本步骤范围

本轮先落这几件事：

- 增加 `LuaUpvalue`
- 让 `LuaClosure` 持有 upvalue cell，而不是只存一份值
- 在 `CallFrame` 里登记并复用打开状态的 upvalue
- 在帧退出时把打开状态的 upvalue 关闭成稳定值
- 支持 `GETUPVAL`
- 支持 `SETUPVAL`
- 用真实 Lua 5.5 chunk 验证共享捕获行为

## 设计原则

### 1. 先做共享，再谈完整生命周期

这一轮最重要的不是把所有上值细节一次做满，而是先保证下面这件事成立：

- 两个闭包如果捕获的是同一个外层变量，它们看到的应该是同一份状态

如果这点没做好，后面再补更多闭包能力都得返工。

### 2. 先区分“打开”和“关闭”

Lua 上值有一个关键阶段差异：

- 外层函数还活着时，它可以直接连到活动寄存器
- 外层函数结束后，它要变成独立保存的一份值

这一轮先把这个分界做出来，让后续再补 `CLOSE`、`TBC` 和更复杂的生命周期时有明确落点。

## 当前支持范围

这一轮新增支持：

- `LuaUpvalue`
- 打开状态的上值复用
- 帧退出时关闭上值
- `GETUPVAL`
- `SETUPVAL`
- 多个闭包共享同一个外层局部变量

当前还没有进入这些内容：

- `TBC`
- 更完整的 to-be-closed 语义
- 更复杂的开放结果调用协议
- 更大范围的闭包与元方法联动

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/upvalue_chunk.lua`
- `test/fixtures/lua55/chunks/upvalue_chunk.luac`

这份脚本覆盖的路径是：

- 外层函数创建局部变量
- 两个内部函数同时捕获这份局部变量
- 一个内部函数修改它
- 另一个内部函数读回修改后的结果
- 外层函数返回后，这两个闭包还能继续工作

## 实现清单

- [x] 编写本轮文档
- [x] 增加 `LuaUpvalue`
- [x] 让 `LuaClosure` 持有 upvalue cell
- [x] 在 `CallFrame` 中登记并关闭打开状态的上值
- [x] 支持 `GETUPVAL`
- [x] 支持 `SETUPVAL`
- [x] 新增真实 `upvalue_chunk.luac`
- [x] 新增运行时上值测试
- [x] 新增 VM 共享上值测试

## 完成标准

本轮完成后，应满足：

- 共享捕获的闭包可以看到同一份变量状态
- 上值在外层函数返回后仍然可用
- `GETUPVAL` / `SETUPVAL` 能跑通真实 Lua 5.5 fixture
- 运行时和 VM 两层都有测试钉住行为

## 下一步

接下来继续往下补：

- `CLOSE` 与块作用域关闭路径已经拆到 `docs/011-step-05-close.md`
- `TBC`
- 更完整的调用协议
- `LOADF`、`LOADKX`、`EXTRAARG` 的剩余路径
- 元方法调度
