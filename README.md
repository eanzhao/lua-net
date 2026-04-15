# lua-net

**用 C# 从零实现 Lua 5.5 的学习项目。**

这个项目的目标不是做最快的 Lua 实现，而是用最清楚的方式把 Lua 5.5 的运行机制拆开给你看——每一步先写文档说清楚要做什么、为什么做，再写代码和测试把它钉住。

## 这个项目是什么

简单说：**用 C# 重写 Lua 5.5 的完整运行时**。

它能做什么？

- 读取 Lua 5.5 编译出的字节码文件（`.luac`）
- 执行字节码，跑出和官方 Lua 一样结果
- 支持表、闭包、元方法、标准库函数等核心语言特性

最终目标是：能接受并运行官方 Lua 5.5 编译器能接受的 Lua 源码。

## 快速开始

```bash
# 运行全部测试
dotnet test lua-net.sln

# 查看解决方案结构
dotnet sln lua-net.sln list
```

工具链基线：**.NET 10** / `net10.0`

## 项目进度

整个项目按 [路线图](docs/001-roadmap.md) 分 **16 步**推进，每一步先写文档，再写代码和测试：

| 阶段 | 主题 | 状态 |
|------|------|------|
| 第 1 步 | 基础基线与规范对齐 | 已完成 |
| 第 2 步 | 运行时值模型 | 已完成 |
| 第 3 步 | 字节码加载与反汇编 | 已完成 |
| 第 4 步 | VM 骨架与指令执行 | 已完成 |
| 第 5 步 | 调用、闭包、上值、可变参数 | 已完成 |
| 第 6 步 | 表与元表 | 已完成 |
| 第 7 步 | 基础库核心函数 | 已完成 |
| 第 8 步 | `table` / `math` / `utf8` 基础库 | 已完成 |
| 第 9 步 | string 库与模式匹配 | 已完成 |
| 第 10 步 | coroutine 库 | 已完成 |
| 第 11 步 | 词法分析 | 未开始 |
| 第 12 步 | 语法分析与 AST | 未开始 |
| 第 13 步 | 编译器（AST → 字节码） | 未开始 |
| 第 14 步 | io / os / package / debug 库 | 未开始 |
| 第 15 步 | 字节码序列化与 REPL | 未开始 |
| 第 16 步 | 兼容性收口 | 未开始 |

### 第 7 步当前覆盖范围

**已实现：** `setmetatable` / `getmetatable`、`rawget` / `rawset` / `rawlen` / `rawequal`、`next` / `pairs` / `ipairs`、`collectgarbage`（最小版本）、`load` / `loadfile` / `dofile`（先覆盖二进制 chunk 路径）、最小 `package` / `require`、`type`、`assert`、`select`、`tonumber` / `tostring`、`pcall` / `xpcall`、`error`、`print` / `warn`、最小 `string` 表（`upper` / `lower` / `len`）、字符串元表 `__index`、字符串算术元方法

**后续扩展：** `load` / `loadfile` 的文本源码路径、完整 `package` / `loadlib`、完整 GC 模式参数会在后续阶段继续展开。

### 第 8 步当前覆盖范围

**已实现：** `table.concat` / `table.insert` / `table.remove` / `table.move` / `table.sort` / `table.pack` / `table.unpack`，`math.abs` / `ceil` / `floor` / `max` / `min` / `sqrt` / `log` / `exp` / `sin` / `cos` / `tan` / `asin` / `acos` / `atan` / `deg` / `rad` / `fmod` / `modf` / `tointeger` / `type` / `ult`，`math.pi` / `huge` / `maxinteger` / `mininteger`，`utf8.offset` / `utf8.codepoint` / `utf8.char` / `utf8.len` / `utf8.codes` / `utf8.charpattern`

**后续扩展：** `table.create`、`math.random` / `math.randomseed`、更完整的字符串/模式匹配消费路径会在后续阶段继续展开。

## 仓库结构

```
src/
├── Lua.Runtime/              运行时值系统与执行基础设施
│   ├── Values/               LuaValue, LuaValueKind, LuaValueHelper
│   ├── Objects/              LuaTable, LuaClosure, LuaUserData, IMetatableOwner
│   └── Execution/            LuaState, LuaStack, CallFrame, LuaUpvalue
├── Lua.Bytecode/             Lua 5.5 字节码格式与反汇编
│   ├── Chunks/               LuaChunk, LuaPrototype, LuaChunkReader
│   ├── Instructions/         LuaOpcode(85), LuaInstruction, 指令格式与布局
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
├── Lua.Runtime.Tests/        运行时单元测试（73 个）
├── Lua.Bytecode.Tests/       字节码解析测试（13 个）
├── Lua.VM.Tests/             VM 集成测试（102 个，使用真实 Lua 5.5 chunk fixture）
└── Lua.Core.Test/            Lua.Core 测试

docs/                         阶段规划和设计文档（37 份）
references/lua-5.5.0/         官方 Lua 5.5.0 源码参考
```

## 文档索引

### 第 1 步：基础基线与规范对齐

| 编号 | 文档 | 主题 |
|------|------|------|
| 001 | [roadmap](docs/001-roadmap.md) | 总路线图与 16 步阶段计划 |
| 002 | [foundation](docs/002-step-01-foundation.md) | 基础基线（目标版本、模块边界、测试策略） |
| 003 | [source-reference](docs/003-step-01-source-reference.md) | 官方源码参考策略 |

### 第 2 步：运行时值模型

| 编号 | 文档 | 主题 |
|------|------|------|
| 004 | [runtime-model](docs/004-step-02-runtime-model.md) | 运行时模型设计（LuaValue, LuaStack, LuaState 等） |

### 第 3 步：字节码加载与反汇编

| 编号 | 文档 | 主题 |
|------|------|------|
| 005 | [bytecode-loader](docs/005-step-03-bytecode-loader.md) | 字节码格式、指令解码、反汇编 |

### 第 4 步：VM 骨架

| 编号 | 文档 | 主题 |
|------|------|------|
| 006 | [vm-skeleton](docs/006-step-04-vm-skeleton.md) | VM 核心执行循环与全部已支持指令 |
| 007 | [table-access](docs/007-step-04-table-access.md) | 最小表构造与原始表访问 |
| 015 | [load-opcodes](docs/015-step-04-load-opcodes.md) | LOADF / LOADKX / LFALSESKIP |
| 016 | [setlist](docs/016-step-04-setlist.md) | SETLIST 与数组批量写入 |
| 018 | [loops](docs/018-step-04-loops.md) | 数值 for、泛型 for、while |
| 019 | [repeat-global-checks](docs/019-step-04-repeat-global-checks.md) | repeat/until 与 global 声明检查 |

### 第 5 步：调用、闭包、上值、可变参数

| 编号 | 文档 | 主题 |
|------|------|------|
| 008 | [self-call](docs/008-step-05-self-call.md) | SELF 指令与 `:` 方法调用 |
| 009 | [global-environment](docs/009-step-05-global-environment.md) | `_ENV` 与全局变量读写 |
| 010 | [upvalue-cells](docs/010-step-05-upvalue-cells.md) | 共享上值 cell 与闭包捕获 |
| 011 | [close](docs/011-step-05-close.md) | CLOSE 与块作用域上值关闭 |
| 012 | [tbc](docs/012-step-05-tbc.md) | TBC 的 nil/false 快速路径 |
| 013 | [close-metamethod](docs/013-step-05-close-metamethod.md) | `__close` 生命周期 |
| 014 | [close-errors](docs/014-step-05-close-errors.md) | `__close` 错误传播 |
| 017 | [vararg-open-results](docs/017-step-05-vararg-open-results.md) | VARARG 与开放结果协议 |
| 020 | [vararg-table](docs/020-step-05-vararg-table.md) | 具名 vararg 参数与 vararg table |

### 第 6 步：表与元表

| 编号 | 文档 | 主题 |
|------|------|------|
| 021 | [binary-metamethods](docs/021-step-06-binary-metamethods.md) | `__add` / `__sub` 等二元算术与位运算元方法 |
| 022 | [length-concat-compare](docs/022-step-06-length-concat-compare-metamethods.md) | `__len` / `__concat` / `__eq` / `__lt` / `__le` |
| 023 | [unary-call](docs/023-step-06-unary-call-metamethods.md) | `__unm` / `__bnot` / `__call` |
| 024 | [table-metamethods](docs/024-step-06-table-metamethods.md) | `__index` / `__newindex` 表访问元方法 |
| 025 | [userdata-metamethods](docs/025-step-06-userdata-metamethods.md) | userdata 的全套元方法 |

### 第 7 步：标准库基础版

| 编号 | 文档 | 主题 |
|------|------|------|
| 026 | [base-metatable-raw](docs/026-step-07-base-metatable-raw-functions.md) | getmetatable / rawget / rawset / rawlen / rawequal |
| 027 | [base-core](docs/027-step-07-base-core-functions.md) | type / assert / select / pcall |
| 028 | [xpcall](docs/028-step-07-xpcall.md) | xpcall 与 message handler |
| 029 | [number-string-conversion](docs/029-step-07-number-string-conversion.md) | tonumber / tostring |
| 030 | [table-iteration-functions](docs/030-step-07-table-iteration-functions.md) | next / pairs / ipairs |
| 031 | [print-warn-functions](docs/031-step-07-print-warn-functions.md) | print / warn |
| 032 | [string-metamethods](docs/032-step-07-string-metamethods.md) | 最小 string 表、字符串元表与字符串算术 |
| 033 | [load-dofile-functions](docs/033-step-07-load-dofile-functions.md) | load / loadfile / dofile 的二进制 chunk 路径 |
| 034 | [collectgarbage-require-functions](docs/034-step-07-collectgarbage-require-functions.md) | collectgarbage 与最小 require / package |

### 第 8 步：`table` / `math` / `utf8` 基础库

| 编号 | 文档 | 主题 |
|------|------|------|
| 035 | [table-math-utf8-libraries](docs/035-step-08-table-math-utf8-libraries.md) | `table` / `math` / `utf8` 基础库 |

### 第 9 步：完整 `string` 库与模式匹配

| 编号 | 文档 | 主题 |
|------|------|------|
| 036 | [string-library-patterns](docs/036-step-09-string-library-patterns.md) | 完整 `string` 库、Lua 模式匹配与二进制 pack/unpack |

### 第 10 步：`coroutine` 库

| 编号 | 文档 | 主题 |
|------|------|------|
| 037 | [coroutine-library](docs/037-step-10-coroutine-library.md) | `coroutine` 库、显式调用栈与挂起恢复 |

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
