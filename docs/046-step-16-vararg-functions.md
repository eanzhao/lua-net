# Step 16：vararg 函数定义与 `...` 编译补齐

## 状态

这一轮把官方 `bitwise.lua` 前面的 vararg 编译缺口补上了：

- 编译器现在接受 `function f(...)` 和 `function f(...args)`
- `...` 表达式支持单值、多值和开放返回语义
- 表构造器最后一个数组字段现在会正确展开 `...` / 函数调用的多返回结果
- 官方 `bitwise.lua` 的阻塞点从“vararg functions are not supported yet”推进到了“for loops are not supported yet”

这说明 Step 16 的兼容性工作又向前走了一层，而且推进的是官方测试集上的真实缺口，不是仓库内自造场景。

## 背景与参考

这一轮主要参考：

- `test/fixtures/lua55/official/lua-5.5.0-tests/bitwise.lua`
- `test/fixtures/lua55/source/vararg_fixed_chunk.lua`
- `test/fixtures/lua55/source/vararg_all_chunk.lua`
- `test/fixtures/lua55/source/vararg_table_return_chunk.lua`
- `references/lua-5.5.0/doc/manual.html`

官方 `bitwise.lua` 暴露出的关键路径是：

- vararg 函数定义本身必须能编译
- `local arg = {...}` 不能只取第一个值
- `return ...`、`local a, b = ...`、`f(...)` 这些位置都要遵守 Lua 的多返回规则

如果只把函数定义语法放开，而不补后两条，兼容性测试会从“编译错误”退化成“静默语义错误”，价值反而更低。

## 本步骤范围

这一轮落地这些内容：

- vararg 函数原型 flag 生成
- 具名 vararg 参数的本地寄存器注入与 `VARARGPREP`
- `...` 在赋值、返回、调用参数中的编译支持
- 表构造器最后一个数组字段的多返回展开
- 官方 `bitwise.lua` 诊断测试推进到下一真实缺口

这一轮明确不做：

- 数值 `for` / 泛型 `for` 的源码编译
- `goto` / `label`
- 完整 variable attribute 支持
- 官方 `bitwise.lua` 转绿
- `all.lua` 汇总执行

## 设计

### 1. 函数原型直接带 vararg 语义

之前编译器在看到 `body.VarargParameter` 时直接报错。

这一轮改成：

- 有 `...` 就给 prototype 打上 vararg flag
- 有具名参数（例如 `...args`）就额外打上 vararg-table flag

这样 VM 现有的 vararg 路径就能直接接住，不需要再发明一套新的运行时表示。

### 2. 具名 vararg 参数沿用现有 VM 约定

VM 里 `VARARGPREP` 已经约定：

- 如果 prototype 使用 vararg table
- 就把构造出的表写到 `prototype.NumberOfParameters` 对应寄存器

所以编译器这轮做的是：

- 先按固定参数顺序分配寄存器
- 如果有 `...args`，再把 `args` 当作一个普通 local 加进去
- 在函数体开始处发出 `VARARGPREP`

这样生成的寄存器布局正好和 VM 对齐，不需要再加桥接逻辑。

### 3. `...` 也要被当成多返回表达式

Lua 里 `...` 和函数调用一样，都属于“只有在特定位置才展开”的多返回表达式。

这一轮编译器把这些路径统一起来了：

- `return ...`
- `local a, b, c = ...`
- `f(...)`
- `{...}`

实现方式是把 `LuaVarargExpressionSyntax` 纳入 `IsMultiResultExpression`，再复用和函数调用一致的“固定结果数”与“开放结果数”两套降级路径。

### 4. `{...}` 不能退化成单值写入

官方 `bitwise.lua` 里的这句是这一轮的关键：

- `local arg = {...}`

如果还按单字段写入，表里只会保留第一个 vararg 值。

所以这轮补了表构造器最后一个数组字段的多返回展开，具体做法是：

- 最后一个数组字段如果是多返回表达式
- 先把开放结果写到表寄存器之后的连续寄存器
- 再用 `SETLIST` 一次性灌入数组部分

这样能覆盖：

- `{...}`
- `{1, ...}`
- `{f()}`

并且和 VM 已有的 `SETLIST` 行为保持一致。

## 当前结果

这一轮之后，官方兼容性推进情况变成：

- `bwcoercion.lua`：通过
- `bitwise.lua`：失败，但当前失败点已经前进到 `for` 循环源码编译

具体诊断信息是：

- `bitwise.lua:124:5: for loops are not supported yet`

这比之前的：

- `bitwise.lua:118:29: vararg functions are not supported yet`

更接近真实剩余缺口，也更能指导 Step 16 的下一步实现顺序。

## 测试

这一轮新增或更新了这些覆盖：

- `Lua.Compiler.Tests`
  - vararg 函数定义
  - `...` 到赋值 / 返回 / 调用参数
  - `...args` 具名 vararg 表
  - `{...}` 与 `{"head", ...}` 的表构造展开
- `Lua.Compatibility.Tests`
  - `bitwise.lua` 诊断点从 vararg 缺口更新为 `for` 循环缺口

## 实现清单

- [x] 编译器接受 vararg 函数定义
- [x] prototype 写入 vararg 相关 flag
- [x] `...args` 注入 local 并发出 `VARARGPREP`
- [x] `...` 支持固定结果数编译
- [x] `...` 支持开放结果数编译
- [x] 表构造器最后字段支持多返回展开
- [x] 新增编译器测试覆盖
- [x] 更新官方兼容性诊断测试
- [x] 更新 README / 文档索引

## 完成标准

本轮完成后，应满足：

- `function f(...)` 和 `function f(...args)` 都能编译并执行
- `...` 在返回、赋值、调用参数位置的结果数符合 Lua 规则
- `{...}` 不会退化成只保留第一个值
- 官方 `bitwise.lua` 不再卡在 vararg 函数定义

## 延期内容

这一轮之后，Step 16 下一优先级建议调整为：

- 数值 `for` / 泛型 `for` 的源码编译
- `goto` / `label`
- 更完整的 variable attribute 支持
- 扩大官方测试集通过面
