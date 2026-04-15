# Lua 5.5 C# 重写路线图

## 项目简介

这是一个用 C# 从零重新实现 Lua 5.5 的学习型项目。

**核心目标：**

- 用清晰、可测试、可阅读的 C# 代码，完整实现 Lua 5.5 的运行时
- 最终能接受并运行官方 Lua 5.5 编译器能接受的 Lua 源码
- 整个过程按小步推进，每一步先写文档、再写代码和测试

**工具链：** .NET 10 / `net10.0`

## 官方参考

- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>
- Lua 5.5 变更说明：<https://www.lua.org/manual/5.5/readme.html>
- 官方测试集：<https://www.lua.org/tests/>
- 官方源码索引：<https://www.lua.org/source/5.5/>
- 本地参考源码：`references/lua-5.5.0/`

## 目标架构

```
src/Lua.Runtime       运行时值、栈、调用帧、闭包、表、状态对象
src/Lua.Bytecode      二进制块读取、指令元数据、反汇编
src/Lua.Syntax        词法分析、语法分析、AST
src/Lua.Compiler      AST 到字节码的编译
src/Lua.StandardLib   基础库、table、string、math、coroutine 等
src/Lua.Cli           REPL、脚本执行、诊断输出
test/Lua.*.Tests      单元测试、夹具测试、兼容性测试
```

不需要第一天就把所有项目建好，但新代码应该朝这个方向组织。

## 阶段计划

### 第 1 步：基础基线与规范对齐 ✅

把目标版本锁定为 Lua 5.5，搭好项目基础。

- 明确对齐 Lua 5.5.0
- 确定模块边界和测试策略
- 本地保留官方源码作为参考

### 第 2 步：运行时值模型 ✅

定义 Lua 运行时的核心类型。

- `LuaValue`：统一表示 nil / 布尔 / 整数 / 浮点 / 字符串 / 表 / 函数 / 线程 / userdata
- `LuaStack`：值栈
- `CallFrame`：调用帧
- `LuaState`：整体运行时状态

### 第 3 步：字节码加载与反汇编 ✅

正确读取 Lua 5.5 二进制块，并输出结构化结果。

- chunk 头部解析
- 指令格式和 90 个 opcode 元数据表
- 可读的反汇编输出

### 第 4 步：VM 骨架与指令执行 ✅

让真实 Lua 5.5 字节码跑起来。

- 取指、解码、执行循环
- 算术运算、比较、跳转、表构造、循环
- 90 个 opcode 全部实现基础路径

### 第 5 步：调用、闭包、上值、可变参数 ✅

让 Lua 函数调用真正工作。

- 闭包与共享上值捕获
- `_ENV` 全局环境
- 可变参数（VARARG / GETVARG / vararg table）
- `__close` 与 to-be-closed 变量

### 第 6 步：表与元表 ✅

补上 Lua 最核心的数据结构。

- `__index` / `__newindex` 表访问元方法
- `__add` / `__sub` / `__mul` 等二元算术元方法
- `__len` / `__concat` / `__eq` / `__lt` / `__le`
- `__unm` / `__bnot` / `__call`
- userdata 的全套元方法

### 第 7 步：标准库基础版 🔧（进行中）

让真实脚本开始具备可运行性。

**已实现：** `setmetatable` / `getmetatable`、`rawget` / `rawset` / `rawlen` / `rawequal`、`type`、`assert`、`select`、`tonumber` / `tostring`、`pcall` / `xpcall`、`error`

**待补充：** `next` / `pairs` / `ipairs`、`table` 库、`string` 库、`math` 库、`coroutine` 基础版

### 第 8 步：词法与语法分析

支持直接输入 Lua 源码。

- 分词器和 AST
- 带源码位置信息的语法错误

### 第 9 步：编译器

把 Lua 源码编译成字节码。

- 作用域解析、局部变量和上值
- 表达式和语句降级
- 常量表和原型生成

### 第 10 步：兼容性收口

从"能跑演示"推进到"能跑真实 Lua 程序"。

- 接入官方测试集
- 补语言边角行为
- 整理诊断和工具链

## 工作原则

- **先正确再性能** — 不为了快而牺牲清晰
- **小步推进** — 每步一个纵向切片，不做大重写
- **文档先行** — 每步先写文档，再写代码和测试
- **语义对齐** — 以 Lua 5.5 手册和官方源码为准

## 交付规则

每个实现步骤必须在 `docs/` 下新增一份文档，至少包含：

1. 背景和官方参考
2. 本步骤范围
3. 数据模型或模块设计
4. 实现清单
5. 完成标准
6. 明确延期的内容
