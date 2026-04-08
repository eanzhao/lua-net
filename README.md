# lua-net

`lua-net` 是一个用 C# 从头实现 Lua 5.5 的学习型项目。

项目目标很直接：

- 对齐 Lua 5.5 语言行为
- 用清楚、可测试、可阅读的 C# 结构重新实现
- 按阶段推进，每一步先写文档，再写代码和测试

当前工具链基线：

- SDK：.NET 10
- Target Framework：`net10.0`

## 当前进度

目前已经完成的基础工作：

- 明确项目目标为 Lua 5.5.0
- 拉取官方 Lua 5.5.0 源码到 `references/lua-5.5.0/`
- 建立新的运行时主线项目 `Lua.Runtime`
- 建立字节码主线项目 `Lua.Bytecode`
- 建立虚拟机主线项目 `Lua.VM`
- 完成运行时值、栈、调用帧、状态对象的第一版骨架
- 完成 Lua 5.5 指令布局、opcode 顺序和字节码静态模型
- 完成第一版 Lua 5.5 chunk 读取器、`proto` 模型和反汇编器
- 完成第一版 VM 骨架和最小执行循环
- 补上第一批布尔加载、条件判断和跳转指令
- 补上第一批比较与短路指令
- 补上第一批算术与一元运算指令
- 建立 `Lua.Runtime.Tests`
- 建立 `Lua.Bytecode.Tests`
- 建立 `Lua.VM.Tests`
- 加入真实 Lua 5.5 chunk fixture
- 跑通真实 `nested_chunk.luac` 的执行结果
- 跑通真实 `branch_chunk.luac` 的控制流执行结果
- 跑通真实 `eqk_chunk.luac`、`lt_chunk.luac`、`testset_chunk.luac` 的比较与短路结果
- 跑通真实 `arith_chunk.luac`、`addk_chunk.luac`、`not_chunk.luac`、`floor_div_chunk.luac` 的算术与一元结果

当前主线测试结果：

- `dotnet test lua-net.sln`
- 40 个测试通过

## 仓库结构

当前仓库主要目录如下：

- `docs/`
  阶段规划和设计文档
- `references/lua-5.5.0/`
  官方 Lua 5.5.0 源码参考
- `src/Lua.Runtime/`
  当前主线运行时实现
- `src/Lua.Bytecode/`
  当前主线字节码实现
- `src/Lua.VM/`
  当前主线虚拟机实现
- `test/Lua.Runtime.Tests/`
  当前主线运行时测试
- `test/Lua.Bytecode.Tests/`
  当前主线字节码测试
- `test/Lua.VM.Tests/`
  当前主线虚拟机测试

后续会逐步扩展到这些模块：

- `Lua.Bytecode`
- `Lua.Syntax`
- `Lua.Compiler`
- `Lua.StandardLib`
- `Lua.Cli`

## 文档索引

当前已经落地的文档：

- [docs/001-roadmap.md](docs/001-roadmap.md)
- [docs/002-step-01-foundation.md](docs/002-step-01-foundation.md)
- [docs/003-step-01-source-reference.md](docs/003-step-01-source-reference.md)
- [docs/004-step-02-runtime-model.md](docs/004-step-02-runtime-model.md)
- [docs/005-step-03-bytecode-loader.md](docs/005-step-03-bytecode-loader.md)
- [docs/006-step-04-vm-skeleton.md](docs/006-step-04-vm-skeleton.md)

这些文档对应的是：

- 总路线图
- 第 1 步基础基线
- 官方源码参考策略
- 第 2 步运行时模型
- 第 3 步字节码加载与反汇编
- 第 4 步 VM 骨架与最小执行闭环

## 开发方式

这个项目按下面的节奏推进：

1. 先明确阶段目标
2. 先在 `docs/` 里写阶段文档
3. 再写最小可用实现
4. 用测试把当前阶段钉住
5. 再进入下一步

官方资料的使用顺序：

1. 先看 Lua 5.5 手册
2. 再看官方 Lua 5.5 源码
3. 最后把行为落实到 C# 代码和测试

## 快速开始

### 1. 还原并运行测试

```bash
dotnet test lua-net.sln
```

### 2. 查看当前主线项目

```bash
dotnet sln lua-net.sln list
```

### 3. 查看官方源码参考

```bash
ls references/lua-5.5.0/src
```

## 当前实现范围

当前 `Lua.Runtime` 已经包含这些基础类型：

- `LuaValueKind`
- `LuaValue`
- `LuaTable`
- `LuaClosure`
- `LuaThread`
- `LuaUserData`
- `LuaStack`
- `CallFrame`
- `LuaState`
- `ILuaClosureBody`

这些类型的目标不是一次做满，而是先为后续 VM、字节码、闭包、表和标准库提供统一的运行时承载结构。

当前 `Lua.Bytecode` 已经包含这些基础能力：

- Lua 5.5 chunk 头常量
- 指令格式定义
- 指令位布局定义
- opcode 枚举
- opcode 名称表
- opcode 模式表
- 原始 32 位指令解码
- `LuaChunkReader`
- `LuaChunk` / `LuaPrototype` / `LuaConstant` 模型
- 真实 Lua 5.5 chunk 的基础结构读取
- 第一版反汇编输出

当前 `Lua.VM` 已经包含这些基础能力：

- `LuaBytecodeClosureBody`
- `LuaVirtualMachine`
- 固定参数、固定返回值的最小调用协议
- `MOVE` / `LOADFALSE` / `LOADTRUE` / `LOADNIL`
- `LOADI` / `LOADK`
- `ADDI` / `ADDK` / `ADD` / `SUB` / `MUL` / `MOD` / `DIV` / `IDIV`
- `UNM` / `NOT`
- `JMP`
- `EQ` / `LT` / `LE` / `EQK`
- `EQI` / `LTI` / `LEI` / `GTI` / `GEI`
- `TEST` / `TESTSET`
- `CALL` / `TAILCALL` / `RETURN` / `RETURN0` / `RETURN1`
- `CLOSURE` / `VARARGPREP`
- 真实 Lua 5.5 chunk 的最小执行闭环
- 真实控制流 chunk 的基础执行闭环
- 真实比较与短路 chunk 的基础执行闭环
- 真实算术与一元 chunk 的基础执行闭环

## 下一步

下一步会继续第 4 步后半段：

- 扩展更多基础 opcode
- 补上更多条件指令和跳转场景
- 补上更完整的调用协议
- 补上更多 K 变体和位运算
- 开始进入上值捕获和元方法调度

## 参考资料

- [Lua 5.5 手册](https://www.lua.org/manual/5.5/)
- [Lua 5.5 发布说明](https://www.lua.org/manual/5.5/readme.html)
- [Lua 官方测试集](https://www.lua.org/tests/)
- [Lua 5.5 官方源码索引](https://www.lua.org/source/5.5/)
