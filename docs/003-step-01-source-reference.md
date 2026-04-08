# 第 1A 步：官方源码参考策略

## 状态

规划已建立，参考源码已落地。

官方 Lua 5.5.0 源码已经拉到本仓库：

- `references/lua-5.5.0/`

本地副本来自官方发布包：

- <https://www.lua.org/ftp/lua-5.5.0.tar.gz>

该正式版的发布日期是：

- 2025-12-15

## 为什么一定要把源码放到本地

这个项目的目标不是“做一个像 Lua 的解释器”，而是“用 C# 重新实现 Lua 5.5，并把它当作学习材料”。

既然目标是重新实现，而且最终希望尽量兼容官方 Lua 5.5 能接受的源码，那只看手册是不够的。

本地保留官方源码的意义主要有这些：

- 补齐手册里没有完全展开的解析细节
- 理清编译阶段的降级规则
- 对照字节码编码方式
- 查运行时边角行为
- 对照标准库的细节
- 在语义有歧义时提供可核对的依据

## 我们会怎么用这份源码

官方 C 源码是参考实现，不是逐行翻译模板。

建议工作方式如下：

1. 先看手册，确认用户可见语义
2. 再看对应的 C 源码，确认具体行为和边界条件
3. 在 C# 里设计更清楚、更适合维护的抽象
4. 用测试把行为钉住

## 关键参考文件

下面这些文件是后续实现时最常会对照的部分：

- `references/lua-5.5.0/src/llex.c`
  词法分析行为
- `references/lua-5.5.0/src/lparser.c`
  语法分析和作用域处理
- `references/lua-5.5.0/src/lcode.c`
  代码生成
- `references/lua-5.5.0/src/lopcodes.h`
  指令布局和字段定义
- `references/lua-5.5.0/src/lopcodes.c`
  指令元数据
- `references/lua-5.5.0/src/lundump.c`
  二进制块加载
- `references/lua-5.5.0/src/ldump.c`
  二进制块写出
- `references/lua-5.5.0/src/lvm.c`
  虚拟机执行行为
- `references/lua-5.5.0/src/ltable.c`
  表结构和访问行为
- `references/lua-5.5.0/src/lstring.c`
  字符串处理
- `references/lua-5.5.0/src/ltm.c`
  元方法行为
- `references/lua-5.5.0/src/lbaselib.c`
  基础库
- `references/lua-5.5.0/src/lstrlib.c`
  字符串库
- `references/lua-5.5.0/src/ltablib.c`
  表库
- `references/lua-5.5.0/src/lmathlib.c`
  数学库
- `references/lua-5.5.0/src/lutf8lib.c`
  `utf8` 库

## 实现原则

即使已经把官方源码放到本地，实现时仍然坚持下面这些原则：

- 以语义对齐为目标，而不是逐行照搬 C 文件
- 结合 C# 的表达能力设计更清楚的抽象
- 优先沉淀测试，再固化边界行为
- 让结构可读、可学、可维护

## 兼容性目标

兼容性目标是：

- 官方 Lua 5.5 能接受的 Lua 源码，我们的编译器和运行时也应该尽量接受并正确执行

这里说的是 Lua 语言和标准行为层面的兼容。

## 实现规则

如果手册写得比较抽象，但官方 C 源码体现了更具体的行为，那么在实现之前或实现过程中，应该先把这个行为落成测试。

测试才是我们后续维护时真正可执行的契约。

## 下一步重点

既然官方源码已经在本地，下一步就应该进入运行时模型设计，先把这些核心概念定义清楚：

- `LuaValue`
- `LuaState`
- `LuaStack`
- `CallFrame`
- 闭包与函数抽象
