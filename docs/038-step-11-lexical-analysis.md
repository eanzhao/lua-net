# Step 11：词法分析

## 状态

已完成当前这一轮。

这一轮把源码前端的第一层落到了独立的 `Lua.Syntax` 项目里，目标很明确：先把 Lua 5.5 源码稳定切成 token 流，并把位置信息钉住，为 Step 12 的 parser 和 Step 13 的 compiler 提供可靠输入。

这一轮**还没有**接 `load` / `loadfile` 的文本 chunk 执行路径，因为语法分析和字节码生成还没完成；这一步只解决“如何把源码读对”，不假装已经“能编译源码”。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/llex.c`
- `references/lua-5.5.0/src/llex.h`
- `references/lua-5.5.0/src/lctype.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

重点对齐的点是：

- 保留字集合
- `global` 作为 Lua 5.5 新保留字
- 数字字面量扫描边界
- 短字符串与长字符串
- 短注释与长注释
- 行列位置追踪

## 本步骤范围

这一轮落地这些能力：

- 新建 `src/Lua.Syntax`
- 新建 `test/Lua.Syntax.Tests`
- 定义 `LuaTokenKind`
- 定义 `LuaToken`
- 定义 `LuaSourcePosition` / `LuaSourceRange`
- 定义 `LuaSyntaxException`
- 实现 `LuaLexer`
- 支持保留字、标识符、运算符、分隔符扫描
- 支持数字字面量扫描与基本格式校验
- 支持短字符串与长字符串
- 支持短注释与长注释
- 支持 token 的源码位置追踪

## 设计

### 1. 先把语法前端独立成 `Lua.Syntax`

到 Step 10 为止，仓库只有 runtime / bytecode / VM 三层；如果继续把 lexer 直接塞进 runtime 或 VM，后面 parser、AST、compiler 会越来越难整理。

所以这一轮直接把词法分析单独放进：

- `src/Lua.Syntax`

这样后续 Step 12 可以自然继续往这个项目里加 parser 和 AST，而不用再做代码迁移。

### 2. token 同时保留 raw lexeme 和解码后的字符串值

词法分析里最容易后悔的设计，是只保留一种形式：

- 只保留 raw 文本：后面每次处理字符串字面量都要重新解码
- 只保留解码结果：报错和调试时又丢了原始源码形态

这一轮 `LuaToken` 采用的是折中设计：

- `Lexeme`：保留源码里的原始片段
- `StringValue`：只在字符串 token 上保存解码后的值

这样后续 parser / compiler：

- 看位置和报错时，可以直接回到原 lexeme
- 处理字符串常量时，不用重复跑一遍 escape 解析

### 3. 注释不进入 token 流，但位置仍然精确推进

Step 12 目前不需要保留 trivia 树，所以这一轮不把注释产生成 token，而是直接跳过：

- `-- ...`
- `--[=[ ... ]=]`

但跳过不等于忽略位置：

- 所有换行都会更新 `line`
- token 位置用一基 line/column 记录
- `Offset` 保留源码中的原始字符偏移

这样 parser 之后报错时，可以稳定地给出源码位置。

### 4. 长字符串/长注释按 Lua 规则处理

这一轮对齐了 Lua long bracket 的关键行为：

- 支持 `[[]]`
- 支持 `[=[...]=]` / `[==[...]==]`
- opening delimiter 后如果立刻换行，会先跳过这一行结束符
- 内部所有换行统一规范化为 `\n`

这能保证后续 parser 拿到的字符串内容和 Lua 行为一致，不会在 CRLF/LF 差异上埋雷。

### 5. 短字符串这一轮把常用 escape 一次做全

为了避免 Step 12/13 再回头返工，这一轮直接把 Lua 5.5 短字符串主路径逃逸序列做完整：

- `\a` `\b` `\f` `\n` `\r` `\t` `\v`
- `\\` `\"` `\'`
- `\xXX`
- `\ddd`
- `\u{XXX}`
- `\z` 吞掉后续空白
- 反斜杠续行

同时对：

- 非法 escape
- 未结束字符串
- 十进制/十六进制 escape 越界
- 非法 long string delimiter

都给出带位置的 `LuaSyntaxException`。

### 6. 标识符字符分类先保持简单、稳定

这一轮标识符起始/延续字符采用：

- ASCII letter
- `_`
- 后续可跟数字

这是一个有意为之的收口：

- 先保证 lexer 行为稳定、可测
- 先满足当前 Step 11-13 的编译链需要
- 如果后续要按更宽的 Unicode/locale 规则放开，再单独扩展，不把这一步做成模糊实现

## 当前支持范围

这一轮新增支持：

- 23 个保留字
- `global` 保留字
- 标识符
- 数字字面量
- 短字符串
- 长字符串
- 短注释
- 长注释
- `//` `..` `...` `==` `>=` `<=` `~=` `<<` `>>` `::`
- 单字符运算符与分隔符
- UTF-8 BOM 跳过
- Unix shebang 跳过

这一轮明确**还不做**：

- 语法分析
- AST
- 源码到字节码编译
- `load` / `loadfile` 的文本 chunk 执行

## 测试

这一轮新增 `Lua.Syntax.Tests`，覆盖：

- 保留字、标识符和运算符分词
- 注释跳过后的 line/column 位置
- 十进制、科学计数法、十六进制、十六进制浮点数字面量
- 短字符串 escape 解析
- 长字符串与长注释
- UTF-8 BOM 与 shebang
- 非法 long string delimiter
- malformed number

## 实现清单

- [x] 新建 `Lua.Syntax` 项目
- [x] 新建 `Lua.Syntax.Tests` 项目
- [x] 定义 token / 位置 / 词法异常类型
- [x] 实现 `LuaLexer`
- [x] 支持 Lua 5.5 `global`
- [x] 支持字符串与注释主路径
- [x] 支持数字字面量基本格式校验
- [x] 新增语法层测试
- [x] 更新 solution / README / roadmap

## 完成标准

本轮完成后，应满足：

- 一段 Lua 5.5 源码可以被稳定切成 token 流
- token 都带有可靠的源码位置
- `global` 已作为保留字进入 token 流
- 长字符串、长注释和短字符串 escape 主路径可用
- 词法错误会以带位置信息的异常形式暴露

## 下一步

接下来进入：

- Step 12：语法分析与 AST
