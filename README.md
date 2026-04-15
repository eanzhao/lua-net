# lua-net

用 C# 从头实现 Lua 5.5 的学习型项目。

- 对齐 Lua 5.5.0 语言行为
- 用清楚、可测试、可阅读的 C# 结构重新实现
- 按阶段推进，每一步先写文档，再写代码和测试

工具链基线：.NET 10 / `net10.0`

## 快速开始

```bash
# 运行全部测试（当前 140 个通过）
dotnet test lua-net.sln

# 查看解决方案结构
dotnet sln lua-net.sln list
```

## 阶段进度

项目按 [路线图](docs/001-roadmap.md) 分 10 步推进，当前进度如下：

| 阶段 | 主题 | 状态 |
|------|------|------|
| 第 1 步 | 基础基线与规范对齐 | 已完成 |
| 第 2 步 | 运行时值模型 | 已完成 |
| 第 3 步 | 字节码加载与反汇编 | 已完成 |
| 第 4 步 | VM 骨架与指令执行 | 已完成 |
| 第 5 步 | 调用、闭包、上值、可变参数 | 已完成 |
| 第 6 步 | 表与元表 | 已完成 |
| 第 7 步 | 标准库基础版 | 进行中 |
| 第 8 步 | 词法与语法分析 | 未开始 |
| 第 9 步 | 编译器 | 未开始 |
| 第 10 步 | 兼容性收口 | 未开始 |

### 第 7 步当前覆盖范围

**已实现：**
`setmetatable` / `getmetatable`、`rawget` / `rawset` / `rawlen` / `rawequal`、`type`、`assert`、`select`、`tonumber` / `tostring`、`pcall` / `xpcall`、`error`

**待补充：**
`next` / `pairs` / `ipairs`、`table` 库、`string` 库、`math` 库、`coroutine` 基础版

## 仓库结构

```
src/
├── Lua.Runtime/              运行时值系统与执行基础设施
│   ├── Values/               LuaValue, LuaValueKind, LuaValueHelper
│   ├── Objects/              LuaTable, LuaClosure, LuaUserData, IMetatableOwner
│   └── Execution/            LuaState, LuaStack, CallFrame, LuaUpvalue
├── Lua.Bytecode/             Lua 5.5 字节码格式与反汇编
│   ├── Chunks/               LuaChunk, LuaPrototype, LuaChunkReader
│   ├── Instructions/         LuaOpcode(90), LuaInstruction, 指令格式与布局
│   └── Disassembly/          LuaDisassembler, LuaLineInfoResolver
├── Lua.VM/                   虚拟机执行引擎（按职责拆分为 partial class）
│   ├── LuaVirtualMachine.cs            核心执行循环与公共 API
│   ├── LuaVirtualMachine.Arithmetic.cs 算术、位运算、拼接、长度
│   ├── LuaVirtualMachine.Comparison.cs 相等性、有序比较、条件跳转
│   ├── LuaVirtualMachine.TableAccess.cs 表读写与 __index/__newindex 链
│   ├── LuaVirtualMachine.Metamethods.cs 元方法解析与分发
│   ├── LuaVirtualMachine.ControlFlow.cs 循环、vararg、泛型 for、跳转
│   ├── LuaVirtualMachine.Helpers.cs     寄存器、上值、常量、资源清理
│   └── Closures/             LuaBytecodeClosureBody
└── Lua.Core/                 共享二进制 chunk 工具（早期遗留）

test/
├── Lua.Runtime.Tests/        运行时单元测试（37 个）
├── Lua.Bytecode.Tests/       字节码解析测试（13 个）
├── Lua.VM.Tests/             VM 集成测试（90 个，使用真实 Lua 5.5 chunk fixture）
└── Lua.Core.Test/            Lua.Core 测试

docs/                         阶段规划和设计文档（29 份）
references/lua-5.5.0/         官方 Lua 5.5.0 源码参考
```

## 文档索引

### 第 1 步：基础基线与规范对齐

| 编号 | 文档 | 主题 |
|------|------|------|
| 001 | [roadmap](docs/001-roadmap.md) | 总路线图与 10 步阶段计划 |
| 002 | [foundation](docs/002-step-01-foundation.md) | 基础基线 |
| 003 | [source-reference](docs/003-step-01-source-reference.md) | 官方源码参考策略 |

### 第 2 步：运行时值模型

| 编号 | 文档 | 主题 |
|------|------|------|
| 004 | [runtime-model](docs/004-step-02-runtime-model.md) | 运行时模型设计 |

### 第 3 步：字节码加载与反汇编

| 编号 | 文档 | 主题 |
|------|------|------|
| 005 | [bytecode-loader](docs/005-step-03-bytecode-loader.md) | 字节码加载与反汇编 |

### 第 4 步：VM 骨架

| 编号 | 文档 | 主题 |
|------|------|------|
| 006 | [vm-skeleton](docs/006-step-04-vm-skeleton.md) | VM 骨架与最小执行闭环 |
| 007 | [table-access](docs/007-step-04-table-access.md) | 最小表构造与原始表访问 |
| 015 | [load-opcodes](docs/015-step-04-load-opcodes.md) | 剩余加载路径 |
| 016 | [setlist](docs/016-step-04-setlist.md) | SETLIST 与数组批量写入 |
| 018 | [loops](docs/018-step-04-loops.md) | 循环执行路径 |
| 019 | [repeat-global-checks](docs/019-step-04-repeat-global-checks.md) | repeat/until 与全局声明检查 |

### 第 5 步：调用、闭包、上值、可变参数

| 编号 | 文档 | 主题 |
|------|------|------|
| 008 | [self-call](docs/008-step-05-self-call.md) | SELF 与对象方法调用 |
| 009 | [global-environment](docs/009-step-05-global-environment.md) | _ENV 与全局表访问 |
| 010 | [upvalue-cells](docs/010-step-05-upvalue-cells.md) | 共享上值 cell |
| 011 | [close](docs/011-step-05-close.md) | CLOSE 与块作用域上值关闭 |
| 012 | [tbc](docs/012-step-05-tbc.md) | TBC 最小快速路径 |
| 013 | [close-metamethod](docs/013-step-05-close-metamethod.md) | __close 生命周期 |
| 014 | [close-errors](docs/014-step-05-close-errors.md) | __close 错误传播 |
| 017 | [vararg-open-results](docs/017-step-05-vararg-open-results.md) | VARARG 与开放结果协议 |
| 020 | [vararg-table](docs/020-step-05-vararg-table.md) | vararg table |

### 第 6 步：表与元表

| 编号 | 文档 | 主题 |
|------|------|------|
| 021 | [binary-metamethods](docs/021-step-06-binary-metamethods.md) | 二元算术与位运算元方法 |
| 022 | [length-concat-compare](docs/022-step-06-length-concat-compare-metamethods.md) | 长度、拼接与比较元方法 |
| 023 | [unary-call](docs/023-step-06-unary-call-metamethods.md) | 一元元方法与 __call |
| 024 | [table-metamethods](docs/024-step-06-table-metamethods.md) | 表访问元方法 |
| 025 | [userdata-metamethods](docs/025-step-06-userdata-metamethods.md) | userdata 元方法 |

### 第 7 步：标准库基础版

| 编号 | 文档 | 主题 |
|------|------|------|
| 026 | [base-metatable-raw](docs/026-step-07-base-metatable-raw-functions.md) | 元表与 raw 函数 |
| 027 | [base-core](docs/027-step-07-base-core-functions.md) | type / assert / select / pcall |
| 028 | [xpcall](docs/028-step-07-xpcall.md) | xpcall |
| 029 | [number-string-conversion](docs/029-step-07-number-string-conversion.md) | tonumber / tostring |

## 开发方式

1. 先明确阶段目标
2. 先在 `docs/` 里写阶段文档
3. 再写最小可用实现
4. 用测试把当前阶段钉住
5. 再进入下一步

官方资料使用顺序：Lua 5.5 手册 → 官方源码 → 落实到 C# 代码和测试

## 参考资料

- [Lua 5.5 手册](https://www.lua.org/manual/5.5/)
- [Lua 5.5 发布说明](https://www.lua.org/manual/5.5/readme.html)
- [Lua 官方测试集](https://www.lua.org/tests/)
- [Lua 5.5 官方源码索引](https://www.lua.org/source/5.5/)
