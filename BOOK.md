# 用 C# 实现 Lua 5.5

> 从零开始，用清晰的 C# 代码构建一个完整的 Lua 5.5 运行时、虚拟机与编译器。

---

## 前言

### 为什么要用 C# 实现 Lua

Lua 是一门小巧而精巧的动态语言。它的运行时核心只有大约三万行 C 代码，却包含了完整的闭包、协程、元方法、增量/分代 GC 等特性。它被广泛用作嵌入式脚本语言（游戏引擎、Nginx、Redis、Neovim 等），也是学习编程语言实现的绝佳素材。

C# 是一门类型安全、工具链成熟的现代语言。用 C# 来重写 Lua，有几个独特的学习价值：

- **对比理解两种类型系统**：Lua 是动态类型，C# 是静态类型。如何在静态语言里高效、安全地表达动态值，是一个经典的工程设计问题。
- **理解虚拟机的工作原理**：.NET CLR 本身就是一台虚拟机。用 C# 实现另一台虚拟机（Lua VM），能让你以"创作者"视角重新理解虚拟机的取指-解码-执行循环、调用栈管理、异常处理等机制。
- **实践编译器前端的完整流程**：从词法分析到语法分析，再到代码生成，你可以亲手走一遍编译器的完整链路。
- **学习垃圾回收的实现**：Lua 5.5 同时支持增量式和分代式两种 GC 策略。用 C# 实现 Lua 侧的 GC 逻辑，能让你深入理解弱表、终结器、三色标记等概念。

### 这本书适合谁

这本书面向的读者：

- 熟悉 C#（泛型、模式匹配、`readonly struct`、`record` 等现代特性），但对 Lua 语言本身不太了解
- 了解基本的编程概念（栈、堆、函数调用、递归），但没有系统学习过编译原理
- 想要理解脚本语言的内部实现，或者想要亲手构建一门语言运行时

你不需要事先了解 Lua，也不需要读过"龙书"。这本书会在需要的时候解释 Lua 的语言特性和编译原理的基本概念。

### 项目概览

本项目是一个完整的 Lua 5.5 实现，包含以下模块：

```
src/Lua.Runtime       运行时值、栈、调用帧、闭包、表、状态对象
src/Lua.Bytecode      二进制块读取、指令元数据、反汇编
src/Lua.Syntax        词法分析、语法分析、AST
src/Lua.Compiler      AST 到字节码的编译
src/Lua.VM            虚拟机（取指-执行循环）
src/Lua.Cli           REPL、脚本执行、命令行工具
test/                 单元测试、夹具测试、兼容性测试
```

实现分为 16 个步骤，按小步推进，每一步都能独立验证。整个过程中遵循"先正确再性能"的原则——代码的首要目标是清晰可读，而非极致优化。

### 如何使用这本书

这本书按"自底向上"的顺序组织：先构建运行时的基础类型，然后实现字节码加载，接着搭建虚拟机，再逐步补上元方法、标准库、编译器前端，最后处理 GC 和兼容性。

每一章的末尾会标注：

- **对应的源代码文件**——你可以在仓库里直接阅读
- **对应的实现文档**——位于 `docs/` 目录，包含更详细的设计决策和实现清单
- **对应的测试**——验证本章内容的测试代码

建议的阅读方式：

1. 如果你只是想了解 Lua VM 的工作原理，重点阅读"第三部分：字节码"和"第四部分：虚拟机"
2. 如果你对编译器前端感兴趣，直接跳到"第六部分：编译器"
3. 如果你想从头到尾理解整个系统，按顺序阅读即可

---

## 第一部分：认识 Lua

### 第 1 章：Lua 语言速览

Lua 是一门诞生于 1993 年的轻量级脚本语言，由巴西里约热内卢天主教大学的设计团队持续维护。它有几个鲜明特点，让它在语言实现学习中独树一帜。

#### 1.1 极简的类型系统

Lua 只有九种值类型：

```csharp
// src/Lua.Runtime/Values/LuaValueKind.cs
public enum LuaValueKind
{
    Nil = 0,      // nil，表示"不存在"或"无效"
    Boolean = 1,  // true 或 false
    Integer = 2,  // 64 位整数
    Float = 3,    // 64 位双精度浮点数（注意：Lua 没有 32 位浮点）
    String = 4,   // 不可变字节字符串
    Table = 5,    // 唯一的复合数据结构——既是数组也是哈希表
    Function = 6, // 函数（Lua 函数或 C# 原生函数）
    Thread = 7,   // 协程（coroutine），不是操作系统线程
    UserData = 8  // 宿主语言（C#）注入的 opaque 数据
}
```

对比 C#，你会发现 Lua 没有 class、struct、interface、enum 这些类型——Table 是它唯一的复合数据结构，通过元方法（metamethod）机制可以实现面向对象、运算符重载等行为。

#### 1.2 数字只有两种

Lua 5.5 里数字要么是 64 位整数，要么是 64 位浮点。整数和浮点是两个独立子类型，但可以自动转换：

```lua
local x = 10        -- integer
local y = 10.0      -- float
local z = x + y     -- float（混合运算结果是浮点）
local w = 10 / 3    -- float（除法总是返回浮点）
local q = 10 // 3   -- integer（整除）
```

#### 1.3 表（Table）是一切数据结构的基础

Lua 的表既是数组又是哈希表，还可以通过元方法模拟类和对象：

```lua
-- 数组
local arr = {10, 20, 30}

-- 哈希表
local person = {name = "Alice", age = 30}

-- 面向对象（通过元方法）
local MyClass = {}
MyClass.__index = MyClass

function MyClass.new(name)
    return setmetatable({name = name}, MyClass)
end

function MyClass:greet()
    return "Hello, " .. self.name
end
```

#### 1.4 函数是一等公民

Lua 的函数可以赋值给变量、作为参数传递、作为返回值返回。它还支持闭包（closure）——函数可以捕获外层作用域的变量：

```lua
function makeCounter()
    local count = 0
    return function()
        count = count + 1
        return count
    end
end

local counter = makeCounter()
print(counter())  -- 1
print(counter())  -- 2
```

#### 1.5 协程（Coroutine）

Lua 的协程是非对称式协程（asymmetric coroutine），通过 `coroutine.yield` 挂起，通过 `coroutine.resume` 恢复。它在语言层面提供了协作式多任务的能力：

```lua
local co = coroutine.create(function()
    print("hello")
    coroutine.yield()
    print("world")
end)

coroutine.resume(co)  -- 输出 hello
coroutine.resume(co)  -- 输出 world
```

#### 1.6 元方法（Metamethod）

元方法是 Lua 的"运算符重载"和"行为定制"机制。通过在元表（metatable）中定义特定的键（如 `__add`、`__index`、`__call`），你可以改变值在各种操作下的行为：

```lua
local vec = {x = 1, y = 2}
local mt = {
    __add = function(a, b)
        return setmetatable({x = a.x + b.x, y = a.y + b.y}, mt)
    end
}
setmetatable(vec, mt)

local v2 = vec + vec
print(v2.x, v2.y)  -- 2  4
```

#### 1.7 Lua vs C#：思维转换

| 概念 | C# | Lua |
|------|-----|-----|
| 类型系统 | 静态、名义类型 | 动态、鸭子类型 |
| 空值 | `null`（引用类型） | `nil`（唯一的"无"值） |
| 数据结构 | `class`、`struct`、`List<>`、`Dictionary<>` | `table`（包揽一切） |
| 作用域 | 块作用域 | 词法作用域 + 显式 `local` |
| 面向对象 | 类 + 继承 + 接口 | 元表 + 元方法（原型链） |
| 错误处理 | `try/catch` | `pcall`/`xpcall` |
| 并发 | `Thread`/`Task` | `coroutine`（协作式） |
| 数组索引 | 0-based | **1-based** |

理解这些差异，是后续实现的核心出发点。

> **本章源代码**：`src/Lua.Runtime/Values/LuaValueKind.cs`
>
> **参考文档**：`docs/001-roadmap.md`

---

### 第 2 章：项目架构与工作方法

#### 2.1 目标版本

本项目只对齐一个版本：**Lua 5.5.0**。所有语义以官方手册和官方源码为准，不做向下兼容，也不做功能裁剪。

官方参考源码保留在 `references/lua-5.5.0/` 目录中，方便随时交叉比对。原则是"语义对齐，而非逐行翻译"——C# 实现应该用 C# 的方式组织，但行为必须和 Lua 5.5 一致。

#### 2.2 模块划分

项目拆分为六个核心模块，每个模块有清晰的职责边界：

```
┌─────────────────────────────────────────────────────┐
│                    Lua.Cli                          │
│              REPL / 脚本执行入口                      │
├─────────────────────────────────────────────────────┤
│ Lua.Compiler │  Lua.VM  │ Lua.Syntax │ Lua.Bytecode │
│  AST→字节码   │  虚拟机   │  词法/语法   │  二进制块读取  │
├─────────────────────────────────────────────────────┤
│                   Lua.Runtime                       │
│           值、栈、调用帧、闭包、表、状态               │
└─────────────────────────────────────────────────────┘
```

关键约束：`Lua.Runtime` 是最底层的模块，它不依赖 `Lua.Bytecode`、`Lua.VM` 或 `Lua.Compiler`。这意味着：

- 值类型（`LuaValue`）的定义不包含任何字节码概念
- 调用帧（`CallFrame`）不包含任何 VM 实现细节
- 标准库函数通过委托注册，而不是直接调用 VM API

这种分层让每个模块都可以独立测试。

#### 2.3 测试策略

测试分三层：

1. **单元测试**（`test/Lua.Runtime.Tests` 等）——验证单个类型和方法的行为
2. **夹具测试**（`test/fixtures/lua55/`）——用真实 Lua 5.5 编译的字节码验证 VM 行为
3. **兼容性测试**（`test/Lua.Compatibility.Tests`）——跑官方 Lua 5.5 测试套件，量化兼容性

夹具测试是本项目的一个特色：我们不手工构造测试数据，而是用官方 `luac` 编译真实 Lua 源码得到 `.luac` 文件，然后用我们的 VM 执行这些 `.luac` 文件。这保证测试数据和真实 Lua 行为一致。

#### 2.4 工作原则

四个核心原则贯穿整个项目：

1. **先正确再性能**——不为了快而牺牲清晰
2. **小步推进**——每步一个纵向切片，不做大重写
3. **文档先行**——每步先写文档，再写代码和测试
4. **语义对齐**——以 Lua 5.5 手册和官方源码为准

> **参考文档**：`docs/001-roadmap.md`、`docs/002-step-01-foundation.md`、`docs/003-step-01-source-reference.md`

---

## 第二部分：运行时基础

### 第 3 章：值的统一表示

在 C# 这样的静态类型语言中实现动态类型系统，第一个要解决的问题就是"如何统一表示不同种类的值"。

#### 3.1 设计选择：struct + 枚举

我们选择用 `readonly struct` + 枚举来表示 Lua 值，而不是用继承体系：

```csharp
// src/Lua.Runtime/Values/LuaValue.cs
public readonly struct LuaValue : IEquatable<LuaValue>
{
    private readonly object? _reference;  // 字符串、表、函数、线程、userdata
    private readonly long _integer;       // 整数值
    private readonly double _float;       // 浮点值
    private readonly bool _boolean;       // 布尔值

    private LuaValue(
        LuaValueKind kind,
        object? reference = null,
        long integer = 0,
        double @float = 0,
        bool boolean = false)
    {
        Kind = kind;
        _reference = reference;
        _integer = integer;
        _float = @float;
        _boolean = boolean;
    }

    public LuaValueKind Kind { get; }
}
```

为什么不用继承？因为 Lua 的值语义和 C# 的引用语义有本质区别：

- 在 Lua 中，`nil`、`true`、`42`、`"hello"` 这些值是"按值传递"的
- 而 table、function 这些是"按引用标识"的
- 如果用继承，`LuaValue` 只能是引用类型（class），会导致大量的堆分配
- 用 struct 可以让简单值（nil、bool、integer、float）直接在栈上传递

这个设计的代价是每个 `LuaValue` 占用较多内存（kind + reference + integer + float + boolean），但换来的是值传递零开销和类型检查的简洁性。

#### 3.2 工厂方法

每种值类型通过静态工厂方法创建：

```csharp
LuaValue.Nil                           // nil
LuaValue.FromBoolean(true)             // 布尔
LuaValue.FromInteger(42)               // 整数
LuaValue.FromFloat(3.14)               // 浮点
LuaValue.FromString("hello")           // 字符串
LuaValue.FromTable(new LuaTable())     // 表
LuaValue.FromFunction(closure)         // 函数
LuaValue.FromThread(thread)            // 线程
LuaValue.FromUserData(userData)        // userdata
```

为什么不直接用构造函数？因为工厂方法有名字，能表达意图，而且可以复用静态 `Nil` 单例。

#### 3.3 相等性规则

Lua 的相等性和 C# 默认行为有几个关键差异：

- **整数和浮点比较**：`1 == 1.0` 在 Lua 中为 `true`（值相等，忽略子类型）
- **字符串**：按内容比较（`Ordinal` 比较），不是引用比较
- **表、函数、线程、userdata**：按引用身份比较——只有完全相同的对象才相等
- **nil**：只有 nil 和 nil 相等

```csharp
// LuaValue 的相等性实现逻辑（简化版）
public bool Equals(LuaValue other)
{
    if (Kind != other.Kind)
    {
        // 整数和浮点可以跨类型比较
        if (Kind == LuaValueKind.Integer && other.Kind == LuaValueKind.Float)
            return (double)_integer == other._float;
        if (Kind == LuaValueKind.Float && other.Kind == LuaValueKind.Integer)
            return _float == (double)other._integer;
        return false;
    }
    return Kind switch
    {
        LuaValueKind.Nil => true,
        LuaValueKind.Boolean => _boolean == other._boolean,
        LuaValueKind.Integer => _integer == other._integer,
        LuaValueKind.Float => _float == other._float,
        LuaValueKind.String => string.Equals((string)_reference!, (string)other._reference!, StringComparison.Ordinal),
        _ => ReferenceEquals(_reference, other._reference) // 表、函数、线程、userdata
    };
}
```

#### 3.4 Lua 真假值

在 C# 里只有 `bool` 类型能做条件判断。但在 Lua 里，`nil` 和 `false` 为假，**其他所有值都为真**——包括 `0` 和空字符串 `""`。这是 Lua 新手最常踩的坑之一：

```lua
if 0 then print("true!") end       -- 会执行，0 是真
if "" then print("true!") end      -- 会执行，空字符串也是真
if nil then print("never") end     -- 不会执行，nil 是假
```

在我们的运行时中，这通过一个辅助方法来表达：

```csharp
// LuaValue 的 IsTruthy 属性
public bool IsTruthy => Kind != LuaValueKind.Nil && !(Kind == LuaValueKind.Boolean && !_boolean);
```

> **本章源代码**：`src/Lua.Runtime/Values/LuaValue.cs`、`src/Lua.Runtime/Values/LuaValueKind.cs`、`src/Lua.Runtime/Values/LuaValueHelper.cs`
>
> **参考文档**：`docs/004-step-02-runtime-model.md`
>
> **测试**：`test/Lua.Runtime.Tests/LuaValueTests.cs`

---

### 第 4 章：栈、调用帧与状态

Lua VM 的执行模型围绕三个核心容器展开：值栈（stack）、调用帧（call frame）和全局状态（state）。

#### 4.1 值栈（LuaStack）

Lua 使用**寄存器式虚拟机**（register-based VM），而不是栈式虚拟机（stack-based VM）。这意味着指令不是在栈顶操作，而是通过寄存器索引直接寻址。

```
栈的结构（从底到顶）：
┌──────────────────────┐
│  函数 A 的寄存器       │  ← CallFrame.BaseIndex
│  R(0) .. R(n)        │
├──────────────────────┤
│  函数 B 的寄存器       │  ← CallFrame.BaseIndex
│  R(0) .. R(m)        │
├──────────────────────┤
│  ...                 │
└──────────────────────┘
```

`LuaStack` 提供基本的栈操作：

```csharp
public sealed class LuaStack
{
    public void Push(LuaValue value);
    public LuaValue Pop();
    public LuaValue Peek(int offset = 0);
    public LuaValue this[int index] { get; set; }
    public void SetTop(int top);
}
```

`SetTop` 是一个关键操作——它用来调整栈顶位置。在函数调用返回后，需要把栈收缩到正确的位置，`SetTop` 就是做这件事的。

#### 4.2 调用帧（CallFrame）

每次 Lua 函数调用都会创建一个调用帧，记录这次调用的上下文：

```csharp
// src/Lua.Runtime/Execution/CallFrame.cs（关键字段）
public sealed class CallFrame
{
    public LuaClosure Closure { get; }         // 当前执行的闭包
    public int BaseIndex { get; }              // 栈上寄存器区的起始位置
    public int ExpectedResults { get; }        // 调用者期望的返回值数量
    public int ProgramCounter { get; }         // 当前指令位置（PC）
    public int RegisterTop { get; }            // 寄存器区上界
    public IReadOnlyList<LuaValue> Varargs { get; }  // 可变参数
    public LuaCallReturnTarget ReturnTarget { get; } // 返回目标
}
```

调用帧还管理两类重要状态：

- **open upvalue**：当前帧的寄存器中，哪些被上值引用着（闭包捕获的外部变量）
- **to-be-closed 寄存器**：哪些寄存器在作用域退出时需要执行 `__close` 元方法

#### 4.3 闭包（LuaClosure）

Lua 的函数不是孤立的——每个函数都带着它捕获的外部变量（上值），这个组合叫做闭包（closure）：

```csharp
// src/Lua.Runtime/Objects/LuaClosure.cs（关键字段）
public sealed class LuaClosure
{
    public string? DebugName { get; }
    public int UpvalueCount { get; }
    public ILuaClosureBody? Body { get; }      // 执行体（字节码或 C# 原生函数）
    public LuaUpvalue[] Upvalues { get; }      // 捕获的上值
}
```

`Body` 是一个接口，允许挂接不同类型的执行体：

```csharp
public interface ILuaClosureBody { }
```

这样字节码闭包、C# 原生函数、标准库包装闭包都可以挂到同一个运行时闭包容器上，而 `Lua.Runtime` 不需要知道执行体的具体类型。

#### 4.4 全局状态（LuaState）

`LuaState` 是整个运行时的聚合根，管理所有共享状态：

- 全局环境表（`_ENV`）
- 值栈和调用帧
- 标准库注册（`print`、`pcall`、`require` 等）
- 类型级元表（字符串元表、数字元表等）
- 协程调度
- GC 状态

`LuaState` 的设计遵循一个重要原则：**核心项目不直接写 `Console`**。输出通过可注入的 sink 实现，这样在测试和嵌入场景下都能灵活控制。

#### 4.5 上值（Upvalue）与闭包捕获

上值是 Lua 闭包的核心机制。理解它的关键在于 **open/closed 两种状态**：

```
open upvalue:  直接指向栈上的寄存器槽位
closed upvalue: 值已经复制到自己的存储中

┌──────────────┐
│   LuaStack    │    ┌──────────────┐
│   R(0) = 42  │◄───│ Upvalue(open)│ ← 闭包 A 引用
│   R(1) = 99  │◄───│ Upvalue(open)│ ← 闭包 A、B 共享引用
└──────────────┘    └──────────────┘

当函数退出、R(1) 离开作用域时：
                    ┌──────────────┐
                    │ Upvalue(closed)│ 值 99 被复制进来
                    └──────────────┘
```

多个闭包可以共享同一个上值——它们看到的是同一个变量。当外层函数退出时，open upvalue 被关闭（值被复制到 upvalue 自身的存储中），但引用关系不变。

> **本章源代码**：`src/Lua.Runtime/Execution/CallFrame.cs`、`src/Lua.Runtime/Execution/LuaStack.cs`、`src/Lua.Runtime/Execution/LuaState.cs`、`src/Lua.Runtime/Objects/LuaClosure.cs`、`src/Lua.Runtime/Objects/LuaUpvalue.cs`
>
> **参考文档**：`docs/004-step-02-runtime-model.md`、`docs/010-step-05-upvalue-cells.md`
>
> **测试**：`test/Lua.Runtime.Tests/LuaStackTests.cs`、`test/Lua.Runtime.Tests/LuaUpvalueTests.cs`

---

## 第三部分：字节码

### 第 5 章：二进制块格式

Lua 的编译器把源码编译成字节码（bytecode），保存为二进制块（binary chunk）。我们的 VM 执行的就是这种格式。

> 类比：.NET 的 IL 字节码存在 PE 文件中；Lua 的字节码存在 `.luac` 文件中。格式不同，但角色类似。

#### 5.1 chunk 的整体结构

一个 Lua 二进制块由 header 和 prototype 树组成：

```
┌──────────────────────────────────┐
│         Chunk Header             │
│  签名、版本号、格式标记、           │
│  整数/浮点/指令的大小和校验值       │
├──────────────────────────────────┤
│       Main Prototype             │
│  ├─ 指令列表                      │
│  ├─ 常量表                        │
│  ├─ 上值描述表                     │
│  ├─ 局部变量表                    │
│  ├─ 行号信息                      │
│  └─ 子 prototype 列表             │
│      └─ （递归结构）               │
└──────────────────────────────────┘
```

Header 包含一系列格式校验值，确保 chunk 是由兼容的 Lua 版本生成的：

```csharp
// src/Lua.Bytecode/Chunks/LuaChunkHeader.cs
public sealed class LuaChunkHeader
{
    public required byte Version { get; init; }                    // 版本号（0x55 = Lua 5.5）
    public required byte Format { get; init; }                    // 格式版本
    public required byte IntSize { get; init; }                   // int 字节数
    public required int IntFormatMarker { get; init; }            // 校验：0x5678
    public required byte InstructionSize { get; init; }           // 指令字节数
    public required uint InstructionFormatMarker { get; init; }   // 校验指令端序
    public required byte LuaIntegerSize { get; init; }            // lua_Integer 字节数
    public required long LuaIntegerFormatMarker { get; init; }    // 校验：0x5678
    public required byte LuaNumberSize { get; init; }             // lua_Number 字节数
    public required double LuaNumberFormatMarker { get; init; }   // 校验：370.5
}
```

这些校验值确保了 chunk 不会在错误的平台上加载——比如在大端机器上编译的 chunk 不能在小端机器上直接执行。

#### 5.2 Prototype（原型）

Prototype 是编译器输出的核心单元，对应一个函数体：

```csharp
// src/Lua.Bytecode/Chunks/LuaPrototype.cs（关键字段）
public sealed class LuaPrototype
{
    public string SourceName { get; }
    public int LineDefined { get; }
    public int LastLineDefined { get; }
    public byte NumParams { get; }
    public byte IsVarArg { get; }
    public byte MaxStackSize { get; }
    public IReadOnlyList<uint> Instructions { get; }        // 指令列表（原始 uint）
    public IReadOnlyList<LuaConstant> Constants { get; }    // 常量表
    public IReadOnlyList<LuaUpvalueDescriptor> Upvalues { get; } // 上值描述
    public IReadOnlyList<LuaPrototype> Children { get; }    // 子函数
    public IReadOnlyList<LuaLocalVariable> LocalVariables { get; }
    public IReadOnlyList<LuaAbsoluteLineInfo> AbsoluteLineInfo { get; }
    public int FirstLine { get; }
    public int LastLine { get; }
}
```

注意 `Instructions` 是 `uint` 列表——每条指令就是一个 32 位无符号整数，需要进一步解码才能理解含义。

#### 5.3 常量表

常量表存储指令中引用的字面量值。一条 `LOADK` 指令通过索引从常量表中取值：

```lua
local x = 42        -- LOADK R(0), K(0)   常量表 K(0) = 42
local y = "hello"   -- LOADK R(1), K(1)   常量表 K(1) = "hello"
```

常量的种类：

```csharp
// src/Lua.Bytecode/Chunks/LuaConstantKind.cs
public enum LuaConstantKind
{
    Nil, Boolean, Integer, Float, String
}
```

#### 5.4 从文件到 Prototype

整个加载流程：

```
.luac 文件 → BinaryReader → LuaChunkHeader + LuaPrototype 树 → LuaChunk 对象
```

`LuaChunkReader` 负责逐字节解析二进制格式，生成内存中的 `LuaChunk` 对象。

> **本章源代码**：`src/Lua.Bytecode/Chunks/` 目录
>
> **参考文档**：`docs/005-step-03-bytecode-loader.md`
>
> **测试**：`test/Lua.Bytecode.Tests/LuaChunkReaderTests.cs`

---

### 第 6 章：指令编码

Lua 的指令是 32 位定长编码，每条指令包含操作码（opcode）和若干操作数。理解指令编码，是理解 VM 执行的基础。

#### 6.1 六种指令格式

Lua 5.5 定义了六种指令格式，决定了 32 位如何被切分：

```csharp
// src/Lua.Bytecode/Instructions/LuaInstructionFormat.cs
public enum LuaInstructionFormat
{
    IABC = 0,    // [op:7][A:8][C:8][B:8]       — 通用三操作数
    IvABC = 1,   // [op:7][A:8][vC:9][vB:9]     — 扩展 B/C 位宽
    IABx = 2,    // [op:7][A:8][Bx:17]           — Bx 用于常量索引等
    IAsBx = 3,   // [op:7][A:8][sBx:17]          — 有符号 Bx，用于跳转偏移
    IAx = 4,     // [op:7][Ax:25]                — 超大索引
    IsJ = 5      // [op:7][sJ:25]                — 有符号跳转
}
```

用位运算解码：

```csharp
// src/Lua.Bytecode/Instructions/LuaInstruction.cs
public readonly record struct LuaInstruction(uint Raw)
{
    public LuaOpcode Opcode => (LuaOpcode)GetArg(Raw, PosOp, SizeOp);
    public int A => GetArg(Raw, PosA, SizeA);
    public int B => GetArg(Raw, PosB, SizeB);
    public int C => GetArg(Raw, PosC, SizeC);
    public int Bx => GetArg(Raw, PosBx, SizeBx);
    public int SBx => Bx - OffsetSBx;   // 有符号偏移
    // ...
}
```

#### 6.2 89 个操作码

Lua 5.5 定义了 89 个操作码（opcode），按功能分组：

| 分组 | 操作码示例 | 说明 |
|------|-----------|------|
| 加载 | `LOADI`、`LOADK`、`LOADTRUE`、`LOADNIL` | 把值加载到寄存器 |
| 表访问 | `GETTABLE`、`SETTABLE`、`GETFIELD`、`SETFIELD` | 读写表的字段 |
| 算术 | `ADD`、`SUB`、`MUL`、`DIV`、`IDIV`、`POW` | 基本运算 |
| 位运算 | `BAND`、`BOR`、`BXOR`、`SHL`、`SHR` | 按位操作 |
| 比较 | `EQ`、`LT`、`LE`、`EQK`、`LTI` | 条件跳转 |
| 控制 | `JMP`、`CALL`、`RETURN`、`FORLOOP` | 流程控制 |
| 闭包 | `CLOSURE`、`GETUPVAL`、`SETUPVAL` | 闭包和上值 |
| 其他 | `VARARG`、`CLOSE`、`TBC`、`SETLIST` | 杂项 |

> **本章源代码**：`src/Lua.Bytecode/Instructions/` 目录
>
> **参考文档**：`docs/005-step-03-bytecode-loader.md`
>
> **测试**：`test/Lua.Bytecode.Tests/LuaInstructionTests.cs`、`test/Lua.Bytecode.Tests/LuaOpcodeTablesTests.cs`

---

### 第 7 章：反汇编器

在 VM 实现之前，先写一个反汇编器（disassembler），能让我们检查 chunk 的内容，验证加载是否正确。

反汇编器做的事情很简单：把 `uint` 指令流翻译成人类可读的文本：

```
; 反汇编输出示例
1   LOADK     R(0), K(0)         ; 加载常量 42 到寄存器 0
2   LOADK     R(1), K(1)         ; 加载常量 "hello" 到寄存器 1
3   ADD       R(2), R(0), R(0)   ; R(2) = R(0) + R(0)
4   RETURN    R(2), 2, 1         ; 返回 R(2)
```

这个工具在后续的 VM 调试中会反复使用——当你不确定 VM 是否正确执行了某段代码时，先看反汇编输出，再对照源码，通常能快速定位问题。

> **本章源代码**：`src/Lua.Bytecode/Disassembly/LuaDisassembler.cs`
>
> **参考文档**：`docs/005-step-03-bytecode-loader.md`

---

## 第四部分：虚拟机

### 第 8 章：执行循环

现在万事俱备：运行时模型有了，字节码能加载了，指令能解码了。接下来把它们串成一条真实的执行链路。

#### 8.1 VM 的职责

虚拟机做三件事：

1. 从 chunk 创建根闭包（root closure）
2. 建立取指-解码-执行循环
3. 管理调用帧和栈的生命周期

```csharp
// src/Lua.VM/LuaVirtualMachine.cs（核心结构）
public sealed partial class LuaVirtualMachine
{
    public LuaState State { get; }

    public LuaValue[] Execute(LuaChunk chunk)
    {
        var closure = CreateRootClosure(chunk.MainFunction, ...);
        return Call(closure);
    }
}
```

#### 8.2 取指-解码-执行

VM 的核心循环是一个巨大的 switch 语句，每条指令对应一个 case：

```csharp
// 简化示意
while (frame.ProgramCounter < instructions.Count)
{
    var instruction = LuaInstruction.FromRaw(instructions[frame.ProgramCounter]);
    frame.Advance();

    switch (instruction.Opcode)
    {
        case LuaOpcode.LoadI:
            var value = LuaValue.FromInteger(instruction.Bx);
            SetRegister(instruction.A, value);
            break;

        case LuaOpcode.Add:
            var a = GetRegister(instruction.A);
            var b = GetRegister(instruction.B);
            var c = GetRegister(instruction.C);
            SetRegister(instruction.A, Add(a, b, c));
            break;

        case LuaOpcode.Call:
            // ... 压入新调用帧，继续循环
            break;

        case LuaOpcode.Return:
            // ... 处理返回值，弹出调用帧
            break;

        // ... 其他 85 个 opcode
    }
}
```

`LuaVirtualMachine` 按职责拆分为 partial class：

```
LuaVirtualMachine.cs              — 核心执行循环和公共 API
LuaVirtualMachine.Arithmetic.cs   — 算术、位运算、拼接、长度
LuaVirtualMachine.Comparison.cs   — 相等性、有序比较、条件跳转
LuaVirtualMachine.TableAccess.cs  — 表读写、SETLIST、NEWTABLE、SELF
LuaVirtualMachine.Metamethods.cs  — 元方法解析与分发
LuaVirtualMachine.ControlFlow.cs  — 循环、vararg、泛型 for、跳转
LuaVirtualMachine.Helpers.cs      — 寄存器、上值、常量、资源清理
LuaVirtualMachine.Coroutines.cs   — 协程支持
```

#### 8.3 从 chunk 到闭包

执行 chunk 的第一步是创建根闭包。一个 chunk 的 `MainFunction` 是一个 `LuaPrototype`，需要包装成 `LuaClosure` 才能执行：

```csharp
var closure = new LuaClosure(
    debugName: "main",
    upvalueCount: prototype.Upvalues.Count,
    body: new LuaBytecodeClosureBody(prototype),
    sourceName: prototype.SourceName);
```

其中第一个上值绑定到全局环境表 `_ENV`，这样代码里的全局变量访问就能正常工作。

#### 8.4 第一次运行

当 VM 真正跑通第一条指令的那一刻，前几章所有的工作就形成了闭环：

```
源码 → luac → .luac 文件 → LuaChunkReader → LuaChunk → LuaVirtualMachine.Execute → 输出
```

> **本章源代码**：`src/Lua.VM/` 目录
>
> **参考文档**：`docs/006-step-04-vm-skeleton.md`
>
> **测试**：`test/Lua.VM.Tests/LuaVirtualMachineTests.cs`

---

### 第 9 章：算术与位运算

#### 9.1 快速路径与元方法回退

Lua 的算术运算遵循"快速路径优先，元方法回退"的模式：

1. 如果两个操作数都是数值类型，直接计算（快速路径）
2. 如果快速路径失败（比如其中一个操作数是表），查找元方法

```
ADD R(a), R(b), R(c)
  ├─ b 和 c 都是数字？→ 直接计算
  ├─ b 有元表且元表有 __add？→ 调用 __add(b, c)
  ├─ c 有元表且元表有 __add？→ 调用 __add(c, b)  [注意参数翻转]
  └─ 都没有？→ 抛出类型错误
```

#### 9.2 整数与浮点的规则

Lua 的算术有几条容易忽略的规则：

- **除法 `/` 总是返回浮点**：`10 / 2` 的结果是 `5.0`（浮点），不是 `5`（整数）
- **整除 `//` 对整数返回整数**：`10 // 3` 的结果是 `3`
- **幂运算 `^` 总是返回浮点**：`2 ^ 3` 的结果是 `8.0`
- **整数溢出回绕**：Lua 的整数运算溢出时采用二进制补码回绕，和 C# 的 `unchecked` 行为一致

#### 9.3 K 变体和 I 变体

很多算术指令有"常量"和"立即数"变体，用来减少指令条数和常量表压力：

| 指令 | 说明 |
|------|------|
| `ADD R(a), R(b), R(c)` | 两个寄存器操作数 |
| `ADDK R(a), R(b), K(c)` | 第二个操作数从常量表取 |
| `ADDI R(a), R(b), sC` | 第二个操作数是指令内嵌的立即数 |

这些变体不影响语义——`ADD R(0), R(1), R(2)` 和 `ADDK R(0), R(1), K(2)` 最终做的都是加法，只是操作数的来源不同。

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.Arithmetic.cs`
>
> **参考文档**：`docs/006-step-04-vm-skeleton.md`
>
> **测试**：`test/fixtures/lua55/chunks/arith_chunk.luac`、`bit_chunk.luac`、`floor_div_chunk.luac` 等

---

### 第 10 章：比较与控制流

#### 10.1 相等性比较

Lua 的 `==` 比较：

- **nil**：只有 nil == nil
- **布尔**：按值比较
- **数字**：先尝试同类型比较，再跨类型（`1 == 1.0` 为 true）
- **字符串**：按内容比较
- **表、函数、线程、userdata**：按引用比较——除非有 `__eq` 元方法

`EQ` 指令后面通常跟着一个 `JMP` 指令：

```lua
if x == y then ... end
```
编译成：
```
EQ R(x), R(y)     -- 如果 x == y 则跳过下一条
JMP +1            -- 不相等时跳到 else 分支
...               -- then 分支
```

#### 10.2 有序比较

`LT`（小于）、`LE`（小于等于）支持数字比较和字符串比较：

```lua
1 < 2       -- 数字比较
"abc" < "def"  -- 字符串按字典序比较
1 < "hello"    -- 错误！不能跨类型有序比较
```

#### 10.3 条件跳转与短路

`TEST` 和 `TESTSET` 是 Lua 的"短路"指令：

```lua
local x = a and b    -- TESTSET: 如果 a 为真则 x = b，否则 x = a
local y = a or b     -- TESTSET: 如果 a 为真则 x = a，否则 x = b
if a then ... end    -- TEST: 如果 a 为真则继续，否则跳过
```

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.Comparison.cs`
>
> **参考文档**：`docs/006-step-04-vm-skeleton.md`

---

### 第 11 章：表

表（table）是 Lua 唯一的复合数据结构。在 C# 中，我们用一个类来实现它，同时支持数组索引和哈希表查找。

#### 11.1 LuaTable 的结构

```csharp
// src/Lua.Runtime/Objects/LuaTable.cs（简化版）
public sealed class LuaTable : IMetatableOwner
{
    private readonly Dictionary<LuaValue, TableEntry> _entries;
    private readonly List<TableEntry> _entriesInOrder;
    public LuaTable? Metatable { get; }

    public LuaValue? RawGet(LuaValue key);
    public void RawSet(LuaValue key, LuaValue value);
    public int NextIndex { get; }       // 用于 next/pairs 迭代
}
```

#### 11.2 键的规范化

Lua 表的键有一个容易忽略的规则：**整数浮点数和整数是同一个键**：

```lua
local t = {}
t[1] = "integer"
t[1.0] = "float"
print(t[1])   -- "float"（1.0 覆盖了 1，因为它们是同一个键）
```

而 NaN 永远不能做键（`NaN ~= NaN`）：

```lua
local t = {}
t[0/0] = "never"   -- 运行时错误：table index is NaN
```

#### 11.3 SETLIST 与数组构造

表构造器 `SETLIST` 指令用于批量设置数组部分的连续元素：

```lua
local t = {10, 20, 30}
```
编译成：
```
NEWTABLE R(0)         -- 创建空表
LOADI R(1), 10        -- 准备元素
LOADI R(2), 20
LOADI R(3), 30
SETLIST R(0), 3       -- 把 R(1)..R(3) 写入表的数组部分
```

当元素数量超过指令内嵌位宽时，会使用 `EXTRAARG` 指令提供额外参数。

#### 11.4 弱表

Lua 支持弱引用表——表可以声明其键或值为弱引用，允许 GC 回收只被弱表引用的对象：

```lua
local t = setmetatable({}, {__mode = "v"})  -- 弱值表
t[1] = someObject
-- 如果 someObject 没有其他引用，GC 后 t[1] 会变成 nil
```

弱引用模式有三种：
- `__mode = "k"` — 弱键
- `__mode = "v"` — 弱值
- `__mode = "kv"` — 弱键 + 弱值

> **本章源代码**：`src/Lua.Runtime/Objects/LuaTable.cs`
>
> **参考文档**：`docs/007-step-04-table-access.md`、`docs/016-step-04-setlist.md`、`docs/054-step-16-weak-tables.md`
>
> **测试**：`test/Lua.Runtime.Tests/LuaTableTests.cs`

---

### 第 12 章：函数调用与闭包

#### 12.1 调用协议

Lua 的函数调用遵循"固定参数 + 可变参数"的协议：

1. 调用者把函数和参数依次压入栈
2. `CALL` 指令触发调用，创建新的调用帧
3. 被调用者在新帧的寄存器区中接收参数
4. 返回时，结果写回调用者指定的位置

```
调用前栈布局：
┌──────────────┐
│  R(func)     │ ← 调用者放函数
│  R(arg1)     │
│  R(arg2)     │
│  R(arg3)     │
└──────────────┘

调用后（新帧的视角）：
┌──────────────┐
│  R(0) = arg1 │ ← 新帧的 BaseIndex 指向这里
│  R(1) = arg2 │
│  R(2) = arg3 │
│  ...         │ ← 寄存器区
└──────────────┘
```

#### 12.2 开放调用与开放返回

Lua 的函数可以返回任意数量的值。当调用者不知道会返回多少个值时，使用"开放"模式：

```lua
local a, b, c = f()   -- 期望 3 个返回值
local d = f()         -- 期望 1 个返回值（多余的丢弃）
print(f())            -- 开放：f 的所有返回值直接传给 print
```

`CALL` 和 `RETURN` 指令的参数 `C` 表示返回值数量。当 `C == 0` 时，表示"开放"——接收所有返回值。

#### 12.3 上值捕获与 `_ENV`

Lua 的全局变量不是真正的"全局"，而是通过 `_ENV` 表实现的：

```lua
x = 42
-- 等价于：_ENV.x = 42
```

在字节码层面，全局读写使用 `GETTABUP` 和 `SETTABUP` 指令——它们访问的是当前闭包的上值 #0（即 `_ENV`）：

```
GETTABUP R(0), U(0), K("x")     -- R(0) = _ENV["x"]
SETTABUP U(0), K("x"), R(1)     -- _ENV["x"] = R(1)
```

#### 12.4 SELF 与方法调用

Lua 的 `obj:method(args)` 语法糖编译为 `SELF` 指令：

```lua
obj:method(arg)
```
编译成：
```
SELF R(0), R(obj), K("method")   -- R(0) = method, R(1) = obj
MOVE R(2), R(arg)                 -- 参数
CALL R(0), 2, 1                   -- 调用 method(obj, arg)
```

`SELF` 同时把函数和 `self`（即 `obj`）放到连续的寄存器中，这样后续的 `CALL` 可以自然地把 `self` 作为第一个参数传递。

#### 12.5 尾调用（TAILCALL）

尾调用是 Lua 的一种优化：当函数的最后一个动作是调用另一个函数时，可以复用当前调用帧，避免调用栈增长：

```lua
function f(n)
    if n <= 0 then return 0 end
    return f(n - 1)   -- 尾调用：不会增长调用栈
end
```

在 VM 层面，`TAILCALL` 把当前帧替换为新函数的帧，而不是压入新帧。

#### 12.6 to-be-closed 变量

Lua 5.4+ 引入了 `to-be-closed` 变量——当变量离开作用域时（无论是正常退出还是出错），自动调用其 `__close` 元方法：

```lua
do
    local f <close> = io.open("file.txt")
    -- 使用 f ...
end   -- 这里自动调用 f:close()
```

`TBC` 指令标记一个寄存器为"待关闭"，`CLOSE` 指令触发关闭。错误传播规则：即使 `__close` 本身抛出错误，剩余的 to-be-closed 变量仍然会被关闭。

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.TableAccess.cs`、`src/Lua.VM/LuaVirtualMachine.ControlFlow.cs`
>
> **参考文档**：`docs/008-step-05-self-call.md`、`docs/009-step-05-global-environment.md`、`docs/010-step-05-upvalue-cells.md`、`docs/011-step-05-close.md`、`docs/012-step-05-tbc.md`、`docs/013-step-05-close-metamethod.md`、`docs/014-step-05-close-errors.md`

---

### 第 13 章：循环

#### 13.1 数值 for 循环

```lua
for i = 1, 10, 2 do
    print(i)   -- 1, 3, 5, 7, 9
end
```

编译成：
```
LOADI R(0), 1      -- 初始值
LOADI R(1), 10     -- 上限
LOADI R(2), 2      -- 步长
FORPREP R(0)       -- 预计算：R(0) -= R(2)，然后进入循环
...                 -- 循环体（R(3) 是当前 i 的副本）
FORLOOP R(0)       -- R(0) += R(2)，如果未超限则跳回循环体
```

注意 Lua 的 for 循环在整数路径上使用整数运算，在浮点路径上使用浮点运算，不会混合。

#### 13.2 泛型 for 循环

```lua
for k, v in pairs(t) do
    print(k, v)
end
```

编译成：
```
-- 迭代器状态存放在连续的寄存器中
TFORPREP R(0)      -- 准备迭代
TFORCALL R(0)      -- 调用迭代函数，获取下一个 k, v
TFORLOOP R(0)      -- 如果 k 不为 nil 则跳回循环体
```

泛型 for 的状态由三个值组成：迭代函数、状态表、初始键。

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.ControlFlow.cs`
>
> **参考文档**：`docs/018-step-04-loops.md`

---

### 第 14 章：可变参数

Lua 的可变参数（vararg）在 Lua 5.5 中有两种形态：经典的可变参数和具名可变参数（vararg table）。

#### 14.1 经典可变参数

```lua
function f(...)
    local a, b = ...            -- 捕获前两个
    print(...)                  -- 全部传递
    local t = {...}             -- 打包成表
end
```

`VARARGPREP` 指令标记函数为可变参数函数。`VARARG` 指令把可变参数加载到寄存器区。

#### 14.2 开放结果

可变参数可以"开放"传递——把所有参数原封不动地传给下一个调用：

```lua
function f(...)
    return g(...)   -- g 收到 f 收到的所有参数
end
```

#### 14.3 vararg table（Lua 5.5）

Lua 5.5 新增了具名可变参数：

```lua
function f(...args)
    print(args[1])     -- 通过名字访问
    args[2] = "modified"  -- 可修改
end
```

具名可变参数在内部创建一个表，参数同时存在于寄存器和表中。

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.ControlFlow.cs`
>
> **参考文档**：`docs/017-step-05-vararg-open-results.md`、`docs/020-step-05-vararg-table.md`

---

### 第 15 章：元方法

元方法是 Lua 最强大的特性之一——它允许你自定义值在各种操作下的行为。对于 C# 开发者来说，可以把它理解为"全局可插拔的运算符重载 + 行为拦截"。

#### 15.1 什么是元方法

每个表和 userdata 可以有一个元表（metatable），元表是一个普通的 Lua 表，其中的特殊键（以双下划线开头）定义了元方法：

```lua
local mt = {
    __add = function(a, b) ... end,     -- 加法
    __sub = function(a, b) ... end,     -- 减法
    __mul = function(a, b) ... end,     -- 乘法
    __index = function(t, k) ... end,   -- 表读取
    __newindex = function(t, k, v) ... end,  -- 表写入
    __call = function(f, ...) ... end,  -- 调用
    __tostring = function(v) ... end,   -- 转字符串
    __len = function(v) ... end,        -- 取长度
    __eq = function(a, b) ... end,      -- 相等比较
    __lt = function(a, b) ... end,      -- 小于比较
    __le = function(a, b) ... end,      -- 小于等于
    __gc = function(v) ... end,         -- 垃圾回收终结器
    __close = function(v, err) ... end, -- to-be-closed 清理
}
```

#### 15.2 元方法的查找顺序

对于二元操作（如 `__add`），Lua 按特定顺序查找元方法：

1. 先查左操作数的元表
2. 如果左操作数没有对应的元方法，查右操作数的元表
3. 对于常量在左边的情况（如 `42 + table`），Lua 会翻转操作数再查找

在 C# 实现中，`MMBIN`/`MMBINI`/`MMBINK` 指令负责在快速路径失败后触发元方法查找。

#### 15.3 表访问元方法

`__index` 和 `__newindex` 是最常用的元方法：

```lua
-- __index：当表没有某个键时触发
local proto = {greet = function(self) return "hi " .. self.name end}
local mt = {__index = proto}
local obj = setmetatable({name = "Lua"}, mt)
obj:greet()  -- "hi Lua"（obj 没有 greet，查元表的 __index）

-- __newindex：当给不存在的键赋值时触发
local readonly = setmetatable({}, {
    __newindex = function(t, k, v)
        error("this table is read-only")
    end
})
readonly.x = 1  -- 错误！
```

关键细节：如果键已经存在，`__newindex` **不会被触发**——它只在"新增"键时触发。这区分了"原始命中"和"原始未命中"。

#### 15.4 __call

任何表或 userdata 都可以被"调用"，只要它的元表定义了 `__call`：

```lua
local callable = setmetatable({}, {
    __call = function(self, x)
        return x * 2
    end
})
print(callable(21))  -- 42
```

在 VM 中，`CALL` 和 `TAILCALL` 遇到非函数值时，会查找 `__call` 元方法，把原始值作为第一个参数传入。

#### 15.5 统一的元方法查找

所有支持元方法的对象（表和 userdata）都实现 `IMetatableOwner` 接口：

```csharp
public interface IMetatableOwner
{
    LuaTable? Metatable { get; }
}
```

通过 `MetatableOwnerExtensions.TryGetMetamethod` 统一查找元方法，避免在 `LuaState` 和 `LuaVirtualMachine` 里重复实现。

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.Metamethods.cs`、`src/Lua.Runtime/Objects/IMetatableOwner.cs`
>
> **参考文档**：`docs/021-step-06-binary-metamethods.md` 至 `docs/025-step-06-userdata-metamethods.md`

---

## 第五部分：标准库

### 第 16 章：基础库

Lua 的基础库提供了一组核心函数，它们注册在全局环境 `_ENV` 中。这些函数本身是用 C# 实现的原生函数，通过 `LuaNativeFunction` 委托注册。

#### 16.1 类型与转换

```lua
type(42)            -- "number"
type(nil)           -- "nil"
type({})            -- "table"
tonumber("42")      -- 42
tonumber("0xFF")    -- 255
tonumber("1010", 2) -- 10（二进制）
tostring(42)        -- "42"（会查 __tostring 元方法）
```

#### 16.2 错误处理：pcall 与 xpcall

Lua 的错误处理不是 `try/catch`，而是 `pcall`（protected call）：

```lua
local ok, result = pcall(function()
    error("something went wrong")
end)
-- ok = false, result = "something went wrong"

-- xpcall 允许设置错误处理函数
local ok, result = xpcall(riskyFunc, function(err)
    return "handled: " .. tostring(err)
end)
```

在 C# 实现中，`pcall` 和 `xpcall` 使用 `try/catch` 捕获 `LuaRuntimeException`，但关键是它们**复用同一套 callable 解析路径**——不区分 Lua 函数、C# 原生函数、带 `__call` 的表，统一走 `LuaState.InvokeCallable`。

#### 16.3 表操作

```lua
rawget(t, k)           -- 不触发 __index 的表访问
rawset(t, k, v)        -- 不触发 __newindex 的表写入
rawlen(t)              -- 不触发 __len 的取长度
rawequal(a, b)         -- 不触发 __eq 的比较
setmetatable(t, mt)    -- 设置元表
getmetatable(t)        -- 获取元表（受 __metatable 保护）
next(t, k)             -- 表迭代器的底层原语
pairs(t)               -- 支持 __pairs 元方法的迭代
ipairs(t)              -- 数组迭代（到第一个 nil 为止）
```

#### 16.4 输出

```lua
print(1, "hello", {})  -- 1    hello    table: 0x...
warn("something bad")  -- 输出到 stderr
```

`print` 对每个参数调用 `tostring`，用制表符分隔。`warn` 支持 `@on`/`@off` 控制消息。

#### 16.5 加载与执行

```lua
load(chunk)           -- 加载二进制 chunk 或文本 chunk
loadfile(filename)    -- 从文件加载
dofile(filename)      -- 加载并立即执行
```

文本 chunk 的加载路径需要编译器前端（Step 11-13）的支持。在实现顺序上，先支持二进制 chunk，后支持文本 chunk。

#### 16.6 模块加载

```lua
local mod = require("mylib")
```

`require` 实现了模块缓存（`package.loaded`）、搜索器链和模块路径查找。模块返回 `nil` 时缓存 `true`，防止重复加载。

> **本章源代码**：`src/Lua.Runtime/Execution/LuaState.cs`（内联标准库注册）
>
> **参考文档**：`docs/026-step-07-base-metatable-raw-functions.md` 至 `docs/034-step-07-collectgarbage-require-functions.md`

---

### 第 17 章：table / math / utf8 库

#### 17.1 table 库

```lua
table.concat({"a", "b", "c"}, ", ")  -- "a, b, c"
table.insert(t, 42)                   -- 尾部插入
table.insert(t, 1, 42)               -- 指定位置插入
table.remove(t)                       -- 尾部删除
table.sort(t)                         -- 原地排序
table.pack(1, 2, 3)                  -- 打包成表 {1, 2, 3, n = 3}
table.unpack({10, 20, 30})           -- 返回 10, 20, 30
```

#### 17.2 math 库

math 库覆盖了常用的数学函数：三角函数（`sin`、`cos`、`tan`）、取整（`floor`、`ceil`）、极值（`max`、`min`）、常量（`pi`、`huge`、`maxinteger`）以及位运算辅助（`ult`——无符号小于比较）。

#### 17.3 utf8 库

utf8 库按字节位置操作，不是 .NET 的字符索引——这是一个容易踩的坑：

```lua
utf8.len("你好")           -- 2（字符数）
utf8.offset("你好", 1)     -- 1（第一个字符的字节偏移）
utf8.offset("你好", 2)     -- 4（"你" 占 3 字节，第二个字符从第 4 字节开始）
```

> **本章源代码**：`src/Lua.Runtime/Execution/LuaState.cs`
>
> **参考文档**：`docs/035-step-08-table-math-utf8-libraries.md`

---

### 第 18 章：string 库与模式匹配

string 库是 Lua 最复杂的标准库之一，主要是因为它包含了一个完整的模式匹配引擎。

#### 18.1 基础函数

```lua
string.byte("ABC", 1)       -- 65
string.char(65, 66)          -- "AB"
string.rep("ab", 3)          -- "ababab"
string.reverse("hello")      -- "olleh"
string.sub("hello", 2, 4)    -- "ell"
string.format("%d + %d = %d", 1, 2, 3)  -- "1 + 2 = 3"
```

#### 18.2 模式匹配

Lua 的模式不是正则表达式。它更简单但也更高效，有自己的语法：

```
.       任意字符
%a      字母          %d     数字
%w      字母或数字     %s     空白
%p      标点          %l/%u  小写/大写
^       字符串开头     $      字符串结尾
*       0 次或多次（贪婪）  +   1 次或多次
-       0 次或多次（懒惰）  ?   0 次或 1 次
```

关键差异：Lua 模式不支持交替（`|`）、不支持反向引用、不支持非捕获组。但支持平衡匹配（`%b()`）。

```lua
string.find("hello world", "(%w+)")      -- 1, 5, "hello"
string.match("2024-01-15", "(%d+)-(%d+)-(%d+)")  -- "2024", "01", "15"
string.gsub("hello", "(%w)", "%1%1")     -- "hheelllloo"
```

模式匹配引擎用 C# 实现，直接操作字节，而不是使用 .NET 的 `Regex`。

#### 18.3 二进制打包

`string.pack` / `string.unpack` 实现了二进制数据的序列化与反序列化，格式字符串类似于 C 的 `printf` 格式：

```lua
local packed = string.pack("<i4i4", 1, 2)   -- 小端，两个 4 字节整数
local a, b = string.unpack("<i4i4", packed)  -- 1, 2
```

> **本章源代码**：`src/Lua.Runtime/Execution/LuaState.String.cs`、`src/Lua.Runtime/Execution/LuaState.String.Format.cs`、`src/Lua.Runtime/Execution/LuaState.String.Pack.cs`
>
> **参考文档**：`docs/036-step-09-string-library-patterns.md`

---

### 第 19 章：coroutine 库

协程是 Lua 最独特的特性之一，也是实现中最具挑战性的部分——它要求对 VM 的调用模型做根本性的改造。

#### 19.1 协程的概念

Lua 的协程是非对称式协程：有一个调用者（`resume`）和一个被调用者（`yield`），控制权在两者之间显式切换。

```
                    yield(values)
    调用者 ◄─────────────────────── 协程
                    resume(args)
    调用者 ────────────────────────► 协程
```

协程有自己的状态：
- **suspended**：已创建但未开始，或已被 yield 挂起
- **running**：正在执行
- **normal**：resume 了其他协程
- **dead**：函数执行完毕或出错

#### 19.2 VM 调用模型的根本改造

要支持 `yield`，VM 的调用模型必须从"C# 递归调用"改为"显式 Lua 调用栈驱动"。

为什么？考虑这个场景：

```lua
function f()
    coroutine.yield()   -- 挂起
    return 42
end

function g()
    return f()           -- 调用 f
end

local co = coroutine.create(g)
coroutine.resume(co)    -- 执行到 yield 挂起
coroutine.resume(co)    -- 从 yield 之后继续执行，返回 42
```

如果 VM 用 C# 递归实现（`g` 调用 `f` 时，C# 方法 `ExecuteClosure` 递归调用自己），那么 `yield` 时整个 C# 调用栈都会展开，`f` 和 `g` 的状态全部丢失。第二次 `resume` 无法回到 `yield` 的位置。

解决方案：**不使用 C# 递归**。所有 Lua 函数调用都通过压入 `CallFrame` 实现，由统一的解释器循环驱动。这样 `yield` 时只需保存当前帧栈，`resume` 时恢复即可。

```
改造前（递归调用）：
    ExecuteClosure(main) → ExecuteClosure(g) → ExecuteClosure(f) → yield
    所有 C# 栈帧丢失！

改造后（显式调用栈）：
    ┌──────────────┐
    │ Frame: main  │
    │ Frame: g     │
    │ Frame: f     │  ← yield 时这些帧全部保留
    └──────────────┘
    统一的 ExecuteLoop 继续驱动最顶层帧
```

#### 19.3 yield 的实现

`coroutine.yield` 通过抛出专用异常来实现控制流转移：

```csharp
// 简化版实现
void Yield(LuaValue[] values)
{
    throw new LuaYieldException(values);
}

LuaValue[] Resume(LuaThread thread, LuaValue[] args)
{
    try
    {
        // 把参数写入挂起点，继续执行
        return ContinueExecution(thread);
    }
    catch (LuaYieldException e)
    {
        // 协程被挂起，返回 yield 的值
        return e.Values;
    }
}
```

#### 19.4 八个协程函数

| 函数 | 说明 |
|------|------|
| `create(f)` | 创建新协程 |
| `resume(co, ...)` | 恢复协程，传入参数 |
| `yield(...)` | 挂起当前协程，传出值 |
| `wrap(f)` | 创建协程并返回一个可直接调用的函数 |
| `status(co)` | 查询协程状态 |
| `isyieldable()` | 当前协程是否可以 yield |
| `close(co)` | 关闭协程，触发 __close |
| `running()` | 返回当前运行的协程 |

> **本章源代码**：`src/Lua.VM/LuaVirtualMachine.Coroutines.cs`、`src/Lua.Runtime/Execution/LuaState.Coroutine.cs`、`src/Lua.Runtime/Objects/LuaThread.cs`
>
> **参考文档**：`docs/037-step-10-coroutine-library.md`
>
> **测试**：`test/Lua.Runtime.Tests/LuaThreadTests.cs`

---

## 第六部分：编译器前端

到目前为止，我们的 VM 只能执行预编译的字节码。接下来我们要实现编译器前端，让 VM 能直接执行 Lua 源码。

编译器前端分三个阶段：词法分析（lexer）→ 语法分析（parser）→ 代码生成（compiler）。

```
源码 → LuaLexer → Token 流 → LuaParser → AST → LuaCompiler → LuaPrototype
```

### 第 20 章：词法分析

#### 20.1 Token 的定义

词法分析器的任务是：把源码字符串切成一个个词法单元（token）。每个 token 有类型、值和源码位置：

```csharp
// src/Lua.Syntax/Lexing/LuaTokenKind.cs
public enum LuaTokenKind
{
    And, Break, Do, Else, ElseIf, End, False, For, Function,
    Global, Goto, If, In, Local, Nil, Not, Or, Repeat, Return,
    Then, True, Until, While,
    Identifier, Number, String, EndOfFile,
    Plus, Minus, Star, Slash, IntegerDivision, Percent, Caret,
    Hash, Ampersand, Tilde, Pipe, LessThan, LessEqual, LeftShift,
    GreaterThan, GreaterEqual, RightShift, Assign, Equal, NotEqual,
    LeftParen, RightParen, LeftBrace, RightBrace, LeftBracket, RightBracket,
    Colon, DoubleColon, Semicolon, Comma, Dot, Concat, Vararg
}
```

注意 `Global`——这是 Lua 5.5 新增的保留字，用于声明全局变量。

#### 20.2 词法分析的规则

**保留字与标识符**

Lua 有 23 个保留字（加上 `global` 共 24 个）。标识符以字母或下划线开头，后续可以跟数字。

**数字字面量**

支持多种格式：
```lua
42              -- 十进制整数
0xFF            -- 十六进制整数
3.14            -- 浮点数
1.0e10          -- 科学计数法
0xF0.0          -- 十六进制浮点数（Lua 5.5）
```

**字符串字面量**

短字符串用 `"` 或 `'`，支持转义序列：
```
\a \b \f \n \r \t \v   -- 控制字符
\\ \" \'                 -- 字面量
\xXX                     -- 十六进制
\ddd                     -- 十进制
\u{XXX}                  -- Unicode
\z                       -- 吞掉后续空白
```

长字符串用 `[[]]` 或 `[=[]=]`，不处理转义序列。开括号后紧跟换行时，该换行被跳过。

**注释**

```lua
-- 单行注释
--[[ 多行注释 ]]
--[=[ 长注释 ]=]
```

注释不进入 token 流，但位置信息仍然精确推进——这对后续的错误报告很重要。

#### 20.3 位置追踪

每个 token 都带有源码位置（行号、列号、字符偏移），用于错误报告和调试信息：

```csharp
public readonly struct LuaSourceRange
{
    public LuaSourcePosition Start { get; }
    public LuaSourcePosition End { get; }
}

public readonly struct LuaSourcePosition
{
    public int Line { get; }     // 1-based
    public int Column { get; }   // 1-based
    public int Offset { get; }   // 0-based
}
```

> **本章源代码**：`src/Lua.Syntax/Lexing/LuaLexer.cs`、`src/Lua.Syntax/Lexing/LuaToken.cs`
>
> **参考文档**：`docs/038-step-11-lexical-analysis.md`
>
> **测试**：`test/Lua.Syntax.Tests/LuaLexerTests.cs`

---

### 第 21 章：语法分析与 AST

#### 21.1 优先级攀爬

Lua 的表达式解析使用优先级攀爬（precedence climbing）算法。Lua 的运算符优先级从低到高：

```
or
and
<  >  <=  >=  ~=  ==
..
+  -
*  /  //  %
not  #  -  ~
^
```

注意 `..`（字符串拼接）是右结合的，`^`（幂运算）也是右结合的，其他二元运算符都是左结合的。

#### 21.2 AST 节点设计

AST 使用一组 record 类型表示：

- **表达式**：数字字面量、字符串字面量、nil/true/false、标识符、二元运算、一元运算、函数调用、表构造器、索引访问、字段访问、方法调用、匿名函数、可变参数 `...`
- **语句**：局部变量声明、赋值、函数调用、do/end 块、if/elseif/end、while、repeat/until、for（数值和泛型）、return、break、goto/label、函数声明、方法声明

设计上保留了语法糖的区分——函数声明 `function foo() end` 和方法声明 `function obj:method() end` 是不同的 AST 节点，方便代码生成时选择不同的指令序列。

#### 21.3 解析器的职责

解析器除了构建 AST，还负责一些"便宜"的语义检查：

- `break` 只能出现在循环体内
- `goto` 不能跳进局部变量作用域
- 函数的最后一个块必须是 `return`

更复杂的语义分析（类型检查、变量是否定义等）留给后续阶段。

> **本章源代码**：`src/Lua.Syntax/Parsing/LuaParser.cs`、`src/Lua.Syntax/Ast/LuaSyntaxNodes.cs`
>
> **参考文档**：`docs/039-step-12-syntax-analysis-ast.md`
>
> **测试**：`test/Lua.Syntax.Tests/LuaParserTests.cs`

---

### 第 22 章：代码生成

代码生成是把 AST 翻译成 `LuaPrototype` 的过程——也就是编译器的后端。

#### 22.1 从 AST 到字节码

编译器的输出是一个 `LuaPrototype` 树，结构如下：

```
LuaPrototype (main chunk)
├─ Instructions: [LOADK, ADD, CALL, ...]
├─ Constants: [42, "hello", ...]
├─ Upvalues: [{name: "_ENV", instack: true, index: 0}]
├─ LocalVariables: [{name: "x", start: 0, end: 10}]
├─ Children:
│  ├─ LuaPrototype (function f)
│  │  ├─ Instructions: [...]
│  │  └─ ...
│  └─ LuaPrototype (function g)
│     └─ ...
```

#### 22.2 `_ENV` 的解析

全局变量在编译期按词法名字解析：

1. 先查找当前函数的局部变量
2. 再查找当前函数的上值
3. 都找不到时，按 `_ENV["name"]` 处理

这样 `_ENV` 本身也可以被局部变量遮蔽：

```lua
local _ENV = {print = function(...) end}
print("hello")  -- 调用的是本地 _ENV.print，不是全局的
```

#### 22.3 寄存器分配策略

第一版编译器选择"不复用"策略——局部变量一旦分配了寄存器，在本函数内不再回收。临时寄存器按表达式求值增长。

这样做的代价是生成的 `MaxStackSize` 可能比官方编译器更大，但好处是：
- 上值捕获不会被寄存器复用污染
- 不需要复杂的 `CLOSE` 和作用域回收逻辑
- 更容易保证正确性

#### 22.4 多返回值

Lua 的多返回值传播是编译器最复杂的部分之一。第一版只覆盖最关键的位置：

```lua
return f()              -- 尾部开放返回
local a, b, c = f()     -- 最后一个调用按剩余变量数补齐
g(a, f())               -- 最后一个参数开放传递
```

其他位置默认按单值收缩。

#### 22.5 编译器的逐步完善

第一版编译器不支持 `for`、`goto`、vararg、`<const>`/`<close>` 属性等特性。这些特性在 Step 16 中通过驱动官方测试套件逐个补齐：

- `function(...)` 和 `function(...args)` → Step 16 第 2 轮
- 数值 `for` 和泛型 `for` → Step 16 第 3 轮
- `<const>` 和 `<close>` 属性 → Step 16 第 4 轮
- `goto`/`label` → Step 16 第 5 轮

> **本章源代码**：`src/Lua.Compiler/LuaCompiler.cs`
>
> **参考文档**：`docs/040-step-13-compiler-first-cut.md`、`docs/046-step-16-vararg-functions.md`、`docs/047-step-16-for-loops-bitwise.md`、`docs/048-step-16-local-variable-attributes.md`
>
> **测试**：`test/Lua.Compiler.Tests/LuaCompilerTests.cs`

---

## 第七部分：包管理与工具

### 第 23 章：包系统

Lua 的 `require` 机制比看起来要复杂。它不仅仅是一个文件加载器，而是一个完整的模块系统。

#### 23.1 require 的工作流程

```
require("mylib")
  │
  ├─ 检查 package.loaded["mylib"] → 如果有缓存，直接返回
  │
  ├─ 遍历搜索器链（searchers）：
  │  ├─ searcher 1: 查找 package.preload["mylib"]
  │  ├─ searcher 2: 按 package.path 查找 Lua 文件
  │  ├─ searcher 3: 按 package.cpath 查找 C 模块
  │  └─ searcher 4: C root 查找
  │
  ├─ 找到后执行模块代码
  │
  ├─ 缓存结果到 package.loaded["mylib"]
  │   （模块返回 nil 时缓存 true）
  │
  └─ 返回模块值
```

#### 23.2 搜索器链

搜索器是可扩展的——每个搜索器是一个函数，接收模块名，返回加载器函数或错误消息。默认搜索器：

1. **preload 搜索器**：查 `package.preload` 表
2. **Lua 搜索器**：按 `package.path` 查找 `.lua` 文件，支持文本和二进制
3. **C 搜索器**：按 `package.cpath` 查找动态库
4. **C root 搜索器**：查找根模块名对应的 C 库

在我们的 C# 实现中，C 模块通过宿主注册的原生库模拟，不涉及真正的动态加载。

> **本章源代码**：`src/Lua.Runtime/Execution/LuaState.Package.cs`
>
> **参考文档**：`docs/041-step-14-package-system.md`

---

### 第 24 章：字节码序列化与反序列化

`string.dump` 把编译好的函数序列化成二进制 chunk——和 Step 3 的读取过程完全对称。

#### 24.1 LuaChunkWriter

`LuaChunkWriter` 是 `LuaChunkReader` 的对称实现：

```
LuaChunkReader:  二进制字节 → LuaChunk 对象
LuaChunkWriter:  LuaChunk 对象 → 二进制字节
```

支持 strip 模式：去掉调试信息（行号、局部变量名），减小输出体积。

#### 24.2 往返验证

dump → load 的往返（roundtrip）是验证序列化正确性的关键测试：

```csharp
// 伪代码
var chunk = LoadChunk(file);
var dumped = DumpChunk(chunk);
var reloaded = LoadChunk(dumped);
AssertEqual(chunk, reloaded);  // 两次加载的结果必须完全一致
```

> **本章源代码**：`src/Lua.Bytecode/Chunks/LuaChunkWriter.cs`
>
> **参考文档**：`docs/042-step-15-bytecode-dump.md`

---

### 第 25 章：命令行工具（CLI & REPL）

#### 25.1 REPL

REPL（Read-Eval-Print Loop）是交互式 Lua 环境，支持：

```bash
$ lua
> print("hello")
hello
> = 1 + 2       -- = 前缀是求值简写
3
> function f()   -- 多行输入自动续行
>>   return 42
>> end
```

多行续行通过启发式检测实现——如果当前输入看起来不完整（比如有未闭合的 `do`/`function`/括号），继续读取下一行。

#### 25.2 命令行参数

```bash
lua script.lua arg1 arg2   # 执行脚本
lua -e "print(42)"         # 执行一行代码
lua -i                     # 交互模式
lua -v                     # 显示版本
lua --help                 # 帮助
```

命令行参数通过 `arg` 全局表暴露给脚本。

> **本章源代码**：`src/Lua.Cli/` 目录
>
> **参考文档**：`docs/043-step-15-cli-repl.md`

---

## 第八部分：垃圾回收与兼容性

### 第 26 章：垃圾回收

Lua 5.5 支持两种 GC 模式：增量式（incremental）和分代式（generational）。

#### 26.1 为什么需要在 C# 里实现 Lua 侧的 GC

C# 已经有 GC 了，为什么还要在 Lua 侧再实现一套？因为 Lua 的弱表（weak table）语义依赖于**Lua 可达性**（Lua reachability），而不是宿主语言的可达性。

考虑这个场景：

```lua
local t = setmetatable({}, {__mode = "v"})  -- 弱值表
local key = "mykey"
t[key] = {}   -- 值是一个空表
-- 现在：唯一引用这个空表的是弱表 t
-- Lua 语义：GC 后 t[key] 应该变成 nil
-- 但如果只依赖 C# GC：C# 仍有一个引用指向空表（在 LuaTable 的内部字典中）
```

所以我们需要在 Lua 侧追踪哪些表是弱表，在合适的时机清理那些"只被弱表引用"的条目。

#### 26.2 弱表清理

弱表清理按特定顺序进行：

1. **弱值表**：清理值为 nil 或不可达的条目
2. **终结器表**：调度有 `__gc` 的对象的终结器
3. **弱键表**：清理键不可达的条目

弱键表使用 **ephemeron（短命表）** 语义——如果键只被弱键表自己引用（不通过值间接引用），则键值对被清理。这需要做不动点（fixpoint）传播。

#### 26.3 终结器（__gc）

有 `__gc` 元方法的对象在 GC 回收前会触发终结器调用：

```lua
local mt = {
    __gc = function(self)
        print("cleaning up " .. self.name)
    end
}
local obj = setmetatable({name = "test"}, mt)
obj = nil
collectgarbage()  -- 触发 "cleaning up test"
```

终结器是**一次性**的——一个对象的 `__gc` 只会被调用一次。

#### 26.4 自动 GC

VM 的执行循环中定期触发自动 GC，类似 C# 的代际回收思路。`collectgarbage` 函数提供手动控制：

```lua
collectgarbage("collect")           -- 完整回收
collectgarbage("stop")              -- 停止自动 GC
collectgarbage("restart")           -- 恢复自动 GC
collectgarbage("count")             -- 返回已用内存（KB）
collectgarbage("incremental", ...)  -- 切换到增量模式
collectgarbage("generational", ...) -- 切换到分代模式
```

> **本章源代码**：`src/Lua.Runtime/Execution/LuaState.GarbageCollection.cs`
>
> **参考文档**：`docs/053-step-16-gc-api-modes.md`、`docs/054-step-16-weak-tables.md`、`docs/055-step-16-gc-finalizers-ephemerons.md`、`docs/056-step-16-gc-count-and-official-gc.md`

---

### 第 27 章：兼容性收口

#### 27.1 官方测试套件

Lua 5.5 官方提供了一套完整的测试套件（约 30 个测试文件），覆盖语言的各个方面。`Lua.Compatibility.Tests` 项目逐个跑这些测试，量化与官方 Lua 的兼容程度。

#### 27.2 逐步补齐的过程

Step 16 是一个由测试驱动的过程：每跑一个官方测试，失败的地方暴露出实现缺口，修复后再跑下一个。形成了一条修复链：

```
userdata uservalues → 官方测试接入 → vararg 函数 → for 循环与位运算
→ local 变量属性 → goto/label → __close 修复 → return hooks
→ coroutine close continuations → yieldable protected calls → GC API
→ 弱表 → 终结器与 ephemeron → GC count
```

每一步修复都让更多的官方测试变绿。

#### 27.3 关键修复案例

**逻辑右移的无符号处理**

Lua 的右移 `>>` 是无符号的——即使操作数是负数，右移时高位也填 0。这和 C# 默认的 `>>`（对负数高位填 1）不同：

```csharp
// C# 默认：-1 >> 1 = -1（算术右移）
// Lua 要求：-1 >> 1 = 0x7FFFFFFFFFFFFFFF（逻辑右移）
result = (long)((ulong)a >> (int)b);
```

**十六进制浮点数解析**

Lua 5.5 支持 `0xF0.0` 这样的十六进制浮点数字面量。编译器需要正确解析这类格式。

**十六进制 .0 字符串输出**

`string.format("%x", 255)` 应该输出 `"ff"`，但某些边界情况下浮点转字符串的行为需要和 Lua 精确对齐。

> **参考文档**：`docs/044-step-16-userdata-uservalues.md` 至 `docs/056-step-16-gc-count-and-official-gc.md`
>
> **测试**：`test/Lua.Compatibility.Tests/OfficialLuaCompatibilityTests.cs`

---

## 附录

### A：项目文件结构

```
lua-net/
├── src/
│   ├── Lua.Runtime/          运行时（值、栈、调用帧、表、闭包、状态）
│   ├── Lua.Bytecode/         字节码（chunk 读取、指令编码、反汇编）
│   ├── Lua.Syntax/           语法前端（词法分析、语法分析、AST）
│   ├── Lua.Compiler/         编译器（AST → 字节码）
│   ├── Lua.VM/               虚拟机（取指-执行循环）
│   └── Lua.Cli/              命令行（REPL、脚本执行）
├── test/
│   ├── Lua.Runtime.Tests/
│   ├── Lua.Bytecode.Tests/
│   ├── Lua.Syntax.Tests/
│   ├── Lua.Compiler.Tests/
│   ├── Lua.VM.Tests/
│   ├── Lua.Cli.Tests/
│   ├── Lua.Compatibility.Tests/
│   └── fixtures/lua55/       测试夹具（93 组 Lua 源码 + 字节码）
├── references/
│   ├── lua-5.5.0/            官方 Lua 5.5.0 源码（参考）
│   └── luajit/               LuaJIT 源码（参考）
└── docs/                     实现文档（56 份，按步骤编号）
```

### B：85+ 操作码速查表

| 操作码 | 格式 | 说明 |
|--------|------|------|
| `MOVE` | ABC | R(A) = R(B) |
| `LOADI` | AsBx | R(A) = sBx（整数立即数） |
| `LOADK` | ABx | R(A) = K(Bx)（常量） |
| `LOADFALSE` | A | R(A) = false |
| `LOADTRUE` | A | R(A) = true |
| `LOADNIL` | A | R(A) = nil |
| `GETUPVAL` | ABC | R(A) = Upvalue[B] |
| `SETUPVAL` | ABC | Upvalue[B] = R(A) |
| `GETTABUP` | ABC | R(A) = Upvalue[B][K(C)] |
| `GETTABLE` | ABC | R(A) = R(B)[R(C)] |
| `GETI` | ABC | R(A) = R(B)[C] |
| `GETFIELD` | ABC | R(A) = R(B)[K(C)] |
| `SETTABUP` | ABC | Upvalue[A][K(B)] = K(C) |
| `SETTABLE` | ABC | R(A)[R(B)] = R(C) |
| `SETI` | ABC | R(A)[B] = R(C) |
| `SETFIELD` | ABC | R(A)[K(B)] = K(C) |
| `NEWTABLE` | ABC | R(A) = new table |
| `SELF` | ABC | R(A+1) = R(B); R(A) = R(B)[K(C)] |
| `ADD`..`SHR` | ABC | 算术/位运算 |
| `ADDK`..`SHRK` | ABC | 常量变体 |
| `ADDI`..`SHRI` | ABC | 立即数变体 |
| `MMBIN` | ABC | 触发元方法 |
| `UNM`/`BNOT`/`NOT` | A | 一元运算 |
| `LEN` | A | R(A) = #R(A) |
| `CONCAT` | ABC | R(A) = R(B)..R(C) |
| `CLOSE` | A | 关闭 upvalue |
| `TBC` | A | 标记为 to-be-closed |
| `JMP` | sJ | 无条件跳转 |
| `EQ`/`LT`/`LE` | ABC | 比较 + 条件跳转 |
| `TEST` | A | if not R(A) then jump |
| `TESTSET` | ABC | if R(C) then R(A) = R(B) else jump |
| `CALL` | ABC | 调用函数 |
| `TAILCALL` | ABC | 尾调用 |
| `RETURN` | ABC | 返回 |
| `FORLOOP` | A | 数值 for 循环步进 |
| `FORPREP` | A | 数值 for 循环准备 |
| `TFORPREP` | A | 泛型 for 循环准备 |
| `TFORCALL` | ABC | 泛型 for 循环调用迭代器 |
| `TFORLOOP` | A | 泛型 for 循环步进 |
| `SETLIST` | ABC | 批量设置数组元素 |
| `CLOSURE` | ABx | R(A) = new closure(Child[Bx]) |
| `VARARG` | ABC | R(A), .. = varargs |
| `VARARGPREP` | A | 标记为可变参数函数 |
| `EXTRAARG` | Ax | 提供额外参数 |

### C：实现文档索引

| 编号 | 文件 | 主题 |
|------|------|------|
| 001 | `001-roadmap.md` | 总路线图 |
| 002 | `002-step-01-foundation.md` | 基础基线 |
| 003 | `003-step-01-source-reference.md` | 官方源码参考策略 |
| 004 | `004-step-02-runtime-model.md` | 运行时值模型 |
| 005 | `005-step-03-bytecode-loader.md` | 字节码加载 |
| 006 | `006-step-04-vm-skeleton.md` | VM 骨架 |
| 007 | `007-step-04-table-access.md` | 表访问 |
| 008 | `008-step-05-self-call.md` | SELF 与方法调用 |
| 009 | `009-step-05-global-environment.md` | _ENV 全局环境 |
| 010 | `010-step-05-upvalue-cells.md` | 上值与闭包捕获 |
| 011 | `011-step-05-close.md` | CLOSE 指令 |
| 012 | `012-step-05-tbc.md` | TBC（to-be-closed） |
| 013 | `013-step-05-close-metamethod.md` | __close 元方法 |
| 014 | `014-step-05-close-errors.md` | __close 错误传播 |
| 015 | `015-step-04-load-opcodes.md` | 加载指令补全 |
| 016 | `016-step-04-setlist.md` | SETLIST |
| 017 | `017-step-05-vararg-open-results.md` | 可变参数与开放结果 |
| 018 | `018-step-04-loops.md` | 循环指令 |
| 019 | `019-step-04-repeat-global-checks.md` | repeat 与 global 检查 |
| 020 | `020-step-05-vararg-table.md` | vararg table |
| 021 | `021-step-06-binary-metamethods.md` | 二元元方法 |
| 022 | `022-step-06-length-concat-compare-metamethods.md` | 长度/拼接/比较元方法 |
| 023 | `023-step-06-unary-call-metamethods.md` | 一元/调用元方法 |
| 024 | `024-step-06-table-metamethods.md` | 表访问元方法 |
| 025 | `025-step-06-userdata-metamethods.md` | userdata 元方法 |
| 026 | `026-step-07-base-metatable-raw-functions.md` | 元表与 raw 函数 |
| 027 | `027-step-07-base-core-functions.md` | 基础库核心函数 |
| 028 | `028-step-07-xpcall.md` | xpcall |
| 029 | `029-step-07-number-string-conversion.md` | tonumber/tostring |
| 030 | `030-step-07-table-iteration-functions.md` | next/pairs/ipairs |
| 031 | `031-step-07-print-warn-functions.md` | print/warn |
| 032 | `032-step-07-string-metamethods.md` | 字符串元方法 |
| 033 | `033-step-07-load-dofile-functions.md` | load/dofile |
| 034 | `034-step-07-collectgarbage-require-functions.md` | collectgarbage/require |
| 035 | `035-step-08-table-math-utf8-libraries.md` | table/math/utf8 库 |
| 036 | `036-step-09-string-library-patterns.md` | string 库与模式匹配 |
| 037 | `037-step-10-coroutine-library.md` | coroutine 库 |
| 038 | `038-step-11-lexical-analysis.md` | 词法分析 |
| 039 | `039-step-12-syntax-analysis-ast.md` | 语法分析与 AST |
| 040 | `040-step-13-compiler-first-cut.md` | 编译器（第一版） |
| 041 | `041-step-14-package-system.md` | 包系统 |
| 042 | `042-step-15-bytecode-dump.md` | 字节码序列化 |
| 043 | `043-step-15-cli-repl.md` | CLI 与 REPL |
| 044 | `044-step-16-userdata-uservalues.md` | userdata 关联值 |
| 045 | `045-step-16-official-test-suite.md` | 官方测试套件接入 |
| 046 | `046-step-16-vararg-functions.md` | 可变参数函数编译 |
| 047 | `047-step-16-for-loops-bitwise.md` | for 循环与位运算修复 |
| 048 | `048-step-16-local-variable-attributes.md` | local 变量属性 |
| 049 | `049-step-16-locals-close-runtime.md` | goto/label 与运行时修复 |
| 050 | `050-step-16-return-hooks.md` | return hooks |
| 051 | `051-step-16-coroutine-close-continuations.md` | 协程 close 延续 |
| 052 | `052-step-16-yieldable-protected-calls.md` | 可 yield 的受保护调用 |
| 053 | `053-step-16-gc-api-modes.md` | GC API 与模式 |
| 054 | `054-step-16-weak-tables.md` | 弱表 |
| 055 | `055-step-16-gc-finalizers-ephemerons.md` | 终结器与 ephemeron |
| 056 | `056-step-16-gc-count-and-official-gc.md` | GC count 与官方 GC 对齐 |
