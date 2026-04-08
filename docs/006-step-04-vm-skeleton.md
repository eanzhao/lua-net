# 第 4 步：VM 骨架与最小执行闭环

## 状态

进行中。

这一阶段的目标，是把前面已经落好的 `Runtime` 和 `Bytecode` 真正接起来，先形成一条最小但真实可跑的执行链路。

这一步不追求一次把 Lua VM 做完整，而是先完成：

- 从 `LuaChunk` 创建可执行闭包
- 建立第一版取指执行循环
- 支持一组最小 opcode
- 让真实 Lua 5.5 chunk 能跑出结果

## 背景与参考

这一阶段主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lvm.h`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lfunc.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一阶段的关注点，不是把 `lvm.c` 逐行翻成 C#，而是先把它拆成更容易读懂和逐步扩展的结构。

## 本步骤范围

本阶段先落这几件事：

- 建立 `Lua.VM` 项目
- 定义“闭包挂执行体”的最小承载方式
- 实现第一版 `LuaVirtualMachine`
- 支持固定参数、固定返回值的最小调用协议
- 支持一组能跑通真实 fixture 的基础指令
- 建立 `Lua.VM.Tests`
- 用真实 `nested_chunk.luac` 验证执行结果
- 用真实 `branch_chunk.luac` 验证控制流执行结果
- 用真实 `eqk_chunk.luac`、`lt_chunk.luac`、`testset_chunk.luac` 验证比较与短路行为
- 用真实 `arith_chunk.luac`、`addk_chunk.luac`、`not_chunk.luac`、`floor_div_chunk.luac` 验证算术与一元运算行为
- 用真实 `k_ops_chunk.luac`、`bit_chunk.luac` 验证 K 变体、位运算与移位行为
- 用真实 `pow_chunk.luac`、`str_chunk.luac` 验证幂运算与字符串原语行为
- 用真实 `self_chunk.luac` 验证对象方法调用行为
- 用真实 `global_chunk.luac` 验证 `_ENV` 全局读写行为
- 用真实 `upvalue_chunk.luac` 验证共享上值捕获行为

## 设计原则

### 1. 先把执行路径接通

现在项目已经有：

- 运行时值模型
- 调用帧和状态容器
- chunk 与 proto 模型
- opcode 和指令解码

所以第 4 步最重要的事情，不是继续堆模型，而是把这几块接成一条真实链路。

### 2. 先支持固定协议

Lua 完整调用协议里有不少复杂点：

- 变长参数
- 开放参数个数
- 开放返回值个数
- 上值捕获
- 元方法调度

这一步先只支持最小固定协议：

- 固定实参数量
- 固定返回值数量
- 基础数值运算
- 基础闭包调用

这样可以先把主循环稳定下来，再逐层扩展。

### 3. 用真实 chunk 验证，而不是只测手工数据

如果只测手工构造的 proto，很容易把实现写成“只适配测试”。

所以这一步除了手工指令测试，还要直接执行真实 Lua 5.5 chunk，确保：

- `ChunkReader` 输出的模型足够支撑执行
- `VM` 对 opcode 的理解和官方产物一致
- 前三步的拆分方式能形成闭环

## 当前支持范围

第一版 VM 先支持这些能力：

- `MOVE`
- `LOADFALSE`
- `LOADTRUE`
- `LOADNIL`
- `LOADI`
- `LOADK`
- `GETUPVAL`
- `GETTABUP`
- `GETTABLE`
- `GETI`
- `GETFIELD`
- `SETUPVAL`
- `SETTABUP`
- `SETTABLE`
- `SETI`
- `SETFIELD`
- `NEWTABLE`
- `SELF`
- `ADDI`
- `ADDK`
- `SUBK`
- `MULK`
- `MODK`
- `DIVK`
- `IDIVK`
- `POWK`
- `ADD`
- `SUB`
- `MUL`
- `MOD`
- `POW`
- `DIV`
- `IDIV`
- `BANDK`
- `BORK`
- `BXORK`
- `BAND`
- `BOR`
- `BXOR`
- `SHLI`
- `SHRI`
- `SHL`
- `SHR`
- `UNM`
- `BNOT`
- `NOT`
- `LEN`
- `CONCAT`
- `JMP`
- `EQ`
- `LT`
- `LE`
- `EQK`
- `EQI`
- `LTI`
- `LEI`
- `GTI`
- `GEI`
- `TEST`
- `TESTSET`
- `CALL`
- `TAILCALL`
- `RETURN`
- `RETURN0`
- `RETURN1`
- `CLOSURE`
- `VARARGPREP`

当前 `ADD` 只先支持数值快速路径；配套的元方法分派先留到后续阶段继续做。

当前算术也只先支持最小快速路径：

- `ADDI` / `ADDK` / `SUBK` / `MULK` / `ADD` / `SUB` / `MUL` 先支持整数与浮点数
- `POWK` / `POW` 先支持数值路径，并按 Lua 的浮点结果语义返回
- `DIV` 按 Lua 规则返回浮点结果
- `DIVK` / `IDIVK` / `MODK` 和寄存器版本一起对齐 Lua 规则
- `IDIV` 和 `MOD` 先对齐 Lua 的向下取整和取模规则
- `UNM` / `NOT` 先支持基础数值和真假值语义
- 算术元方法分派放到后续阶段

当前字符串原语也先支持最小快速路径：

- `LEN` 先支持字符串长度
- `CONCAT` 先支持字符串和数值拼接
- 表、元方法和更完整的对象语义放到后续阶段

当前位运算也先支持快速路径：

- `BANDK` / `BORK` / `BXORK` 与寄存器版本先支持整数运算
- `SHLI` / `SHRI` / `SHL` / `SHR` 先对齐 Lua 的移位规则
- `BNOT` 先支持整数路径
- 位运算元方法分派放到后续阶段

当前分支和比较也只先支持最小快速路径：

- `TEST` 走 Lua 的真假值规则
- `TESTSET` 走 Lua 的短路规则
- 立即数比较先支持数值路径
- `EQ` / `EQK` 先走原始比较快速路径
- `LT` / `LE` 先支持数值和字符串顺序比较
- 元方法与更复杂的比较行为放到后续阶段

## 模型说明

### `ILuaClosureBody`

职责：

- 让 `LuaClosure` 可以挂不同类型的执行体
- 继续保持 `Lua.Runtime` 不依赖 `Lua.Bytecode` 和 `Lua.VM`

这样后续无论是：

- Lua 字节码闭包
- C# 原生函数闭包
- 标准库包装闭包

都可以挂到同一个运行时闭包容器上。

### `LuaBytecodeClosureBody`

职责：

- 挂住一个 `LuaPrototype`
- 作为第一版 VM 的可执行函数体

### `LuaVirtualMachine`

职责：

- 从 `LuaChunk` 或 `LuaClosure` 启动执行
- 管理 `LuaState`
- 建立第一版取指执行循环
- 解释当前支持的 opcode
- 返回执行结果

## 当前实现清单

- [x] 编写本阶段文档
- [x] 建立 `Lua.VM` 项目
- [x] 让 `LuaClosure` 可以挂执行体
- [x] 实现第一版 `LuaVirtualMachine`
- [x] 支持最小调用与返回协议
- [x] 支持最小布尔加载与条件跳转
- [x] 支持第一批比较与短路指令
- [x] 支持第一批算术与一元运算指令
- [x] 支持第一批 K 变体、位运算与移位指令
- [x] 支持第一批幂运算与字符串原语指令
- [x] 建立 `Lua.VM.Tests`
- [x] 用真实 `nested_chunk.luac` 验证执行结果
- [x] 用真实 `branch_chunk.luac` 验证控制流结果
- [x] 用真实 `eqk_chunk.luac`、`lt_chunk.luac`、`testset_chunk.luac` 验证比较与短路结果
- [x] 用真实 `arith_chunk.luac`、`addk_chunk.luac`、`not_chunk.luac`、`floor_div_chunk.luac` 验证算术与一元结果
- [x] 用真实 `k_ops_chunk.luac`、`bit_chunk.luac` 验证 K 变体、位运算与移位结果
- [x] 用真实 `pow_chunk.luac`、`str_chunk.luac` 验证幂运算与字符串原语结果
- [x] 用真实 `self_chunk.luac` 验证对象方法调用结果
- [x] 用真实 `global_chunk.luac` 验证 `_ENV` 全局读写结果
- [x] 用真实 `upvalue_chunk.luac` 验证共享上值捕获结果

## 完成标准

本阶段这一轮完成时，应满足：

- `Lua.VM` 可以独立编译
- `LuaChunk` 能进入 VM 执行
- 至少有一份真实 Lua 5.5 chunk 能跑出正确结果
- 调用帧和栈在执行前后能正确收拢
- 测试可以覆盖手工 proto 和真实 fixture 两条路径

## 下一步

这一步跑通之后，后续继续往下补：

- 表访问的最小快速路径已经拆到 `docs/007-step-04-table-access.md`
- `SELF` 与最小对象方法调用路径已经拆到 `docs/008-step-04-self-call.md`
- `_ENV` 与最小全局表访问路径已经拆到 `docs/009-step-04-global-environment.md`
- 共享上值 cell 与 `GETUPVAL` / `SETUPVAL` 已经拆到 `docs/010-step-05-upvalue-cells.md`
- 更完整的调用协议
- 更完整的跳转与条件分支
- `EQI` / `LEI` / `GEI` 之外更多比较组合
- `and` / `or` 之外更复杂的短路场景
- `CLOSE`、`TBC` 与更完整的上值生命周期路径
- `LOADF`、`LOADKX`、`EXTRAARG` 等其余加载路径
- 元方法调度
