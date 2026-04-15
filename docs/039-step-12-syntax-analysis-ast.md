# Step 12：语法分析与 AST

## 状态

已完成当前这一轮。

这一轮把 `Lua.Syntax` 从“只能稳定分词”推进到了“可以把 Lua 5.5 源码解析成带位置信息的 AST”，为 Step 13 的源码编译器提供结构化输入。

这一轮依然**没有**把文本源码接到 `load` / `loadfile` 的执行路径上，因为 AST 到字节码的降级还在下一步；本轮只解决“如何把 token 流组织成可靠语法树”。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/doc/manual.html`
- `references/lua-5.5.0/src/lparser.c`

重点对齐的点是：

- Lua 5.5 完整语法定义
- 二元/一元运算符优先级与结合性
- `global` 声明与 `global *`
- `varargparam ::= ... [Name]`
- 语法错误位置报告

## 本步骤范围

这一轮落地这些能力：

- 新增 `Lua.Syntax.Ast`
- 新增 `Lua.Syntax.Parsing`
- 定义 chunk / block / statement / expression AST 节点
- 实现表达式解析（优先级攀爬）
- 实现前缀表达式、函数调用、方法调用、表构造器
- 实现语句解析：
  - 赋值、函数调用语句
  - `if` / `elseif` / `else`
  - `while` / `repeat` / 数值 `for` / 泛型 `for`
  - `do` / `return` / `break`
  - `goto` / label
  - `function` / `local function` / `global function`
  - `local` / `global` 声明
  - `global [attrib] *`
- 对局部/全局 attribute 做基础语法校验
- 用带源码位置的 `LuaSyntaxException` 报告 parser 错误

## 设计

### 1. AST 先按“编译器可直接消费”的粒度建模

这一步不是做一个只适合打印的语法树，而是为 Step 13 做准备，所以 AST 不是只保留“语义等价”的信息，而是尽量保留编译阶段会关心的结构：

- `LuaFunctionNameSyntax` 保留点号链和可选方法名
- `LuaFunctionBodySyntax` 保留具名 vararg 参数
- `LuaDeclarationNameListSyntax` 保留前置 attribute 和每个名字自己的后置 attribute
- `LuaCallArgumentsSyntax` 保留 `()` / table constructor / literal string 三种调用参数形态
- `LuaParenthesizedExpressionSyntax` 单独成节点，避免后续编译阶段丢掉括号语义

这样后续 compiler 不需要再从“已经被抹平的 AST”里倒推语法糖细节。

### 2. 表达式解析直接对齐官方优先级表

表达式用了和 `lparser.c` 一样的优先级攀爬策略：

- 一元运算优先级：`12`
- `^` 和 `..` 右结合
- `and` / `or` 保持最低优先级

这样可以稳定覆盖：

- `a .. b .. c`
- `-a^b`
- 位运算和算术混排
- 比较运算与逻辑运算组合

不用再手写一串容易出错的递归层级。

### 3. 语句层保留 Lua 原始语法糖，而不是提前降级

函数声明、方法声明、`global function`、`local function` 这些语法糖在 AST 里保留为独立 statement 节点，没有在 parser 阶段提前改写成赋值。

这样做有两个好处：

- parser 逻辑更贴近手册语法，测试更直接
- Step 13 可以在 compiler 里集中做语义化降级，不把“语法识别”和“语义展开”混在一起

### 4. parser 只做必要的语法级校验

这一轮 parser 会拦这些高价值错误：

- `break` 出现在循环外
- 非 vararg 函数里使用 `...`
- 未闭合的 `end`
- 未知 attribute
- `global<close>` 这类非法全局 attribute
- 一个局部声明列表里出现多个 to-be-closed 变量

但它**还不做**更深的语义解析，例如：

- `goto` 目标是否存在
- label 是否重复
- 全局声明遮蔽规则的完整静态检查

这些更适合在后续编译阶段或更细的语义分析层处理。

## 当前支持范围

这一轮新增支持：

- 完整 chunk / block AST
- 主要语句 AST 与表达式 AST
- 表构造器字段三种形态
- 普通函数调用与 `:` 方法调用
- `function` / `local function` / `global function`
- `local` / `global` 声明及属性
- `global<const> *`
- Lua 5.5 具名 vararg 参数
- parser 级位置化错误

这一轮明确**还不做**：

- AST 到字节码编译
- `load` / `loadfile` 的文本源码执行
- `goto` / label 的完整静态合法性校验
- 作用域解析、上值解析、寄存器分配

## 测试

这一轮新增 `LuaParserTests`，覆盖：

- `..` 的右结合
- `-a^b` 的一元/幂运算优先级
- 函数声明、方法名和具名 vararg 参数
- `global<const> *`
- 局部/全局声明与 attribute
- `if` / `elseif` / `else`、循环、数值/泛型 `for`
- 赋值左值与表构造器字段
- `break`、非法 `...`、非法 attribute、多 `close` 变量
- 缺失 `end` 时的带 source name 错误

## 实现清单

- [x] 新增 AST 节点模型
- [x] 新增 `LuaParser`
- [x] 实现表达式优先级解析
- [x] 实现主要语句解析
- [x] 支持 Lua 5.5 `global` 相关语法
- [x] 新增 parser 测试
- [x] 更新 roadmap / README

## 完成标准

本轮完成后，应满足：

- 一段 Lua 5.5 源码可以被稳定解析为 AST
- AST 节点保留编译阶段需要的关键语法结构
- `global` / `global *` / 具名 vararg 参数进入 AST
- 常见语法错误以带位置的异常形式暴露

## 下一步

接下来进入：

- Step 13：编译器（AST → 字节码）
