# Step 13：编译器（第一版）

## 状态

已完成 Step 13 的第一版闭环，但这一轮**还没有**覆盖完整 Lua 5.5 语法。

这一轮的目标不是“一次写完全部 compiler”，而是先把源码执行链路真正打通：

- `Lua.Syntax` AST 可以降级成 `LuaPrototype`
- `LuaVirtualMachine` 可以直接执行编译出的文本 chunk
- `load` / `loadfile` / `dofile` 不再停留在“text chunks are not supported yet”

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lparser.c`
- `references/lua-5.5.0/src/lcode.c`
- `references/lua-5.5.0/src/lfunc.c`
- `references/lua-5.5.0/doc/manual.html`

重点对齐的点是：

- 局部变量与 `_ENV` 的词法解析
- 嵌套函数上值捕获
- 表达式与语句的字节码降级方向
- 文本 chunk 与 `load` / `loadfile` 的接线方式

## 本步骤范围

这一轮落地这些能力：

- 新增独立 `Lua.Compiler` 项目
- 新增 `LuaCompiler` 与 `LuaCompilerException`
- 把文本 chunk 编译接入 `LuaVirtualMachine`
- 支持这一版 compiler 的核心子集：
  - 局部变量声明与普通赋值
  - 普通函数声明、局部函数、方法声明、匿名函数
  - 上值捕获与闭包生成
  - `return`
  - `do` / `if` / `while` / `repeat` / `break`
  - 普通函数调用与 `:` 方法调用
  - 表构造器、成员访问、下标访问
  - 基本一元/二元表达式
  - `_ENV` 驱动的全局读写
- 为文本 `load` / `loadfile` 新增集成测试

## 设计

### 1. 先做“能执行”的 compiler，不先追求官方字节码形状

这一轮 compiler 的目标是生成**可被现有 VM 正确执行**的 `LuaPrototype`，而不是马上逐条对齐官方 `luac` 的产物。

所以第一版选择：

- 优先使用现有 VM 已稳定覆盖的 opcode
- 先走通正确语义，再考虑更细的 opcode 选择和优化
- 常量折叠、`SETLIST`、比较 immediate / constant 变体先不强求

这让 step 13 可以尽快从“前端 AST”进入“真正执行文本源码”。

### 2. `_ENV` 按词法名字解析，而不是做特殊全局分支

Lua 里的全局访问本质上是 `_ENV["name"]`。

第一版 compiler 没把全局读写硬编码成独立语义，而是按下面的顺序做解析：

- 先找局部变量
- 再找上值
- 找不到时，把名字降级成当前词法作用域里的 `_ENV` 表访问

这样一来：

- 裸名字 `x` 能走标准全局路径
- 本地 `_ENV` 遮蔽可以自然生效
- 子函数里的全局访问会自动捕获父作用域里的 `_ENV`

### 3. 局部寄存器先“不复用”，换更简单稳定的作用域行为

第一版没有激进地复用已经离开作用域的局部寄存器，而是选择：

- 局部变量寄存器一旦分配，本函数内不再复用
- 临时寄存器单独按表达式求值增长
- 通过词法作用域控制“名字可见性”，不是靠寄存器回收驱动语义

这样做的代价是：

- 生成的 `MaxStackSize` 可能比官方编译器更大

但收益很直接：

- 上值捕获不会因为寄存器复用而被污染
- 不需要在第一版就把 `CLOSE` / 作用域回收做得很复杂
- 更适合先把闭包、块作用域和文本执行链路钉稳

### 4. 多返回值先只覆盖高价值位置

Lua 的多返回值传播很容易把 compiler 复杂度拉高。

第一版只覆盖最需要的几个位置：

- `return f()` 的尾部开放返回
- 赋值/局部声明里，最后一个调用结果按剩余变量数补齐
- 调用参数里，最后一个调用参数可以开放传递

其余位置默认按单值收缩，这样既能支持日常脚本，也能避免一开始把整个调用结果传播系统铺太大。

## 当前支持范围

这一轮新增支持：

- 文本 chunk 基础编译与执行
- 局部作用域与上值捕获
- 方法定义与方法调用
- 基本控制流和表构造器
- `load` / `loadfile` 的文本路径

这一轮明确**还不做**：

- `global` 声明 / `global *`
- `const` / `close` attribute
- `goto` / label
- 数值 `for` / 泛型 `for`
- vararg 函数与 `...`
- 更接近官方 `luac` 的 opcode 选择与优化
- 完整行号表、本地变量调试信息、常量折叠

这些会继续留在 Step 13 后续小轮次里补。

## 测试

这一轮新增 `Lua.Compiler.Tests`，覆盖：

- 算术与局部变量
- `if` / `while` / `break`
- 嵌套闭包与上值捕获
- 表构造器、字段读写、方法调用
- `load` / `loadfile` 的文本 chunk 路径
- 未支持语法的编译错误

同时更新了 `LuaStateTests`，把原来的 “text chunks are not supported yet” 改成“未配置 text loader 时的显式报错”。

## 实现清单

- [x] 新增 `Lua.Compiler` 项目
- [x] 实现 AST → `LuaPrototype` 基础降级
- [x] 实现局部变量 / 上值 / `_ENV` 解析
- [x] 接入 `LuaVirtualMachine` 的文本 chunk loader
- [x] 新增 compiler 集成测试
- [x] 更新 roadmap / README

## 完成标准

本轮完成后，应满足：

- 可以直接把一段 Lua 文本源码编译成 `LuaChunk`
- VM 可以执行编译出的文本 chunk
- `load` / `loadfile` 能在当前支持子集上运行文本源码
- 未覆盖的语法会以显式 `LuaCompilerException` 暴露，而不是静默生成错误字节码

## 下一步

Step 13 接下来继续补：

- `for` / `goto` / label / vararg
- `global` 相关语义
- attribute、`close`、更多调试信息
- 更贴近官方编译器的 opcode 选择和优化
