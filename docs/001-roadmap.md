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
- 指令格式和 85 个 opcode 元数据表
- 可读的反汇编输出

### 第 4 步：VM 骨架与指令执行 ✅

让真实 Lua 5.5 字节码跑起来。

- 取指、解码、执行循环
- 算术运算、比较、跳转、表构造、循环
- 85 个 opcode 全部实现基础路径

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

### 第 7 步：基础库核心函数 🔧（进行中）

让真实脚本开始具备可运行性。

**已实现：**

- `setmetatable` / `getmetatable`（含 protected metatable）
- `rawget` / `rawset` / `rawlen` / `rawequal`
- `next` / `pairs` / `ipairs`
- `type` / `assert` / `select`
- `tonumber` / `tostring`（含 `__tostring` / `__name`）
- `pcall` / `xpcall` / `error`
- `print` / `warn`

**待补充：**

- `load` / `dofile` / `loadfile`
- `collectgarbage`（最小版本）
- `require`（最小版本，依赖 Step 14 的完整 package 系统）
- 字符串元表 `__index`（让 `("hello"):upper()` 工作）
- 字符串算术元方法（Lua 5.5 新增：`"10" + 1` → `11`）

### 第 8 步：table 库与 math 库

补上最常用的两个标准库。

**table 库：**

- `table.concat` / `table.insert` / `table.remove` / `table.move`
- `table.sort`
- `table.pack` / `table.unpack`

**math 库：**

- 基础函数：`abs` / `ceil` / `floor` / `max` / `min` / `sqrt` / `log` / `exp`
- 三角函数：`sin` / `cos` / `tan` / `asin` / `acos` / `atan`
- 转换：`deg` / `rad` / `fmod` / `modf` / `tointeger` / `type`
- 常量：`pi` / `huge` / `maxinteger` / `mininteger`
- 位相关：`ult`

**utf8 库：**

- `utf8.offset` / `utf8.codepoint` / `utf8.char` / `utf8.len` / `utf8.codes`

### 第 9 步：string 库与模式匹配

string 库函数数量多，模式匹配引擎是独立的复杂子系统。

**基础函数：**

- `string.byte` / `string.char` / `string.len`
- `string.lower` / `string.upper` / `string.reverse` / `string.rep`
- `string.sub` / `string.format`

**模式匹配引擎：**

- `string.find` / `string.match` / `string.gmatch` / `string.gsub`
- 字符类（`%a` `%d` `%w` `%s` `%p` 等）
- 锚点（`^` `$`）
- 捕获（`()` 与位置捕获）
- 平衡匹配（`%b()`）

**二进制打包：**

- `string.pack` / `string.packsize` / `string.unpack`

### 第 10 步：coroutine 库

需要改造 VM 调用栈以支持挂起和恢复。

- `coroutine.create` / `coroutine.resume` / `coroutine.yield`
- `coroutine.wrap` / `coroutine.status` / `coroutine.isyieldable`
- `coroutine.close`（Lua 5.4+ 新增，与 `__close` 配合）
- `coroutine.running`

### 第 11 步：词法分析

支持直接输入 Lua 源码。

- 分词器：保留字、标识符、数字字面量、字符串字面量（短字符串和长字符串）
- 注释处理（短注释和长注释）
- token 流与源码位置追踪
- Lua 5.5 新增保留字：`global`

### 第 12 步：语法分析与 AST

- 表达式解析（优先级攀爬）
- 语句解析（赋值、if/elseif/else、while、repeat、for、do、return、break、goto/labels）
- 函数定义与方法定义
- 表构造器
- `global` 声明（Lua 5.5 新增）
- 带源码位置的语法错误报告

### 第 13 步：编译器（AST → 字节码）

- 作用域解析与局部变量分配
- 上值解析与闭包生成
- 寄存器分配
- 常量折叠
- 表达式降级与语句降级
- 跳转修补与控制流编译
- 常量表和原型生成

### 第 14 步：io / os / package / debug 库

补上与宿主系统交互的库。

**io 库：**

- `io.open` / `io.close` / `io.read` / `io.write` / `io.lines`
- `io.input` / `io.output` / `io.flush` / `io.type`
- 文件句柄方法

**os 库：**

- `os.clock` / `os.date` / `os.time` / `os.difftime`
- `os.execute` / `os.getenv` / `os.remove` / `os.rename`

**package 系统：**

- `require()` 完整语义
- `package.path` / `package.cpath` / `package.loaded`
- `package.searchpath` / `package.loadlib`
- 搜索器链与模块缓存

**debug 库：**

- `debug.getinfo` / `debug.getlocal` / `debug.setlocal`
- `debug.getupvalue` / `debug.setupvalue`
- `debug.traceback` / `debug.sethook` / `debug.gethook`
- `debug.upvalueid` / `debug.upvaluejoin`
- `debug.getuservalue` / `debug.setuservalue`

### 第 15 步：字节码序列化与 REPL

- `string.dump()`：将函数序列化为二进制 chunk（与 Step 3 对称）
- 交互式 REPL（读取-求值-打印循环）
- 脚本执行入口（`lua script.lua`）
- 命令行参数处理

### 第 16 步：兼容性收口

从"能跑演示"推进到"能跑真实 Lua 程序"。

- 接入官方测试集
- 补语言边角行为：
  - 弱表（`__mode = "k"` / `"v"` / `"kv"`）
  - `__gc` 终结器
  - 多 userdata 关联值
  - 错误层级（error level）与完整 traceback
- 量化与官方 Lua 5.5 的差距
- 性能基线测量

## Lua 5.5 特有特性清单

以下特性是 5.5 相对于 5.4 新增或变更的，需要在对应步骤中特别关注：

| 特性 | 涉及步骤 | 说明 |
|------|----------|------|
| `global` 关键字 | 11, 12 | 新增保留字，用于声明全局变量 |
| 字符串算术元方法 | 7 | 字符串自动注册 `__add` / `__sub` 等元方法 |
| 分代式 GC 增强 | 16 | `collectgarbage` 的 generational/incremental 模式参数 |
| to-be-closed 完善 | 5 | `__close` 语义与 `coroutine.close` 配合 |
| 泛型 for 迭代协议变更 | 4 | 闭合值的处理方式调整 |

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
