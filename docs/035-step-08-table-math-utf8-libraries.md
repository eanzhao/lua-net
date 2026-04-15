# 第 8 步：`table` / `math` / `utf8` 基础库

## 状态

已完成当前这一轮。

这一轮把 Step 08 路线图里约定的三组基础库补上了：

- `table`
- `math`
- `utf8`

做完这一轮之后，项目已经具备一套可直接给真实 Lua 5.5 chunk 使用的常用标准库骨架。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/ltablib.c`
- `references/lua-5.5.0/src/lmathlib.c`
- `references/lua-5.5.0/src/lutf8lib.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮重点对齐的点是：

- `tconcat` / `tinsert` / `tremove` / `tmove` / `sort` / `tpack` / `tunpack`
- `math_abs` / `math_floor` / `math_ceil` / `math_fmod` / `math_modf` / `math_log`
- `math_min` / `math_max` / `math_toint` / `math_type` / `math_ult`
- `byteoffset` / `codepoint` / `utfchar` / `utflen` / `iter_codes`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `table` / `math` / `utf8`
- 实现 `table.concat` / `table.insert` / `table.remove` / `table.move`
- 实现 `table.sort` / `table.pack` / `table.unpack`
- 实现 `math.abs` / `math.ceil` / `math.floor` / `math.max` / `math.min`
- 实现 `math.sqrt` / `math.log` / `math.exp`
- 实现 `math.sin` / `math.cos` / `math.tan` / `math.asin` / `math.acos` / `math.atan`
- 实现 `math.deg` / `math.rad` / `math.fmod` / `math.modf` / `math.tointeger` / `math.type`
- 预置 `math.pi` / `math.huge` / `math.maxinteger` / `math.mininteger`
- 实现 `math.ult`
- 实现 `utf8.offset` / `utf8.codepoint` / `utf8.char` / `utf8.len` / `utf8.codes`
- 预置 `utf8.charpattern`
- 用真实 Lua 5.5 chunk 验证三组基础库

## 设计原则

### 1. `table` 库先直接建立在当前 `LuaTable` 之上

当前 runtime 还没有拆分真正的 array part / hash part，也没有单独的 table library 子系统。

所以这一轮不提前做复杂抽象，而是直接复用当前已有的：

- `GetSequenceLength()`
- `GetValue()`
- `SetValue()`

这样能把 `table.insert` / `remove` / `move` / `pack` / `unpack` 先稳定落地，同时不和未来更细的 table 内部结构绑定死。

### 2. `math` 库尽量保留 Lua 的整数 / 浮点结果形状

Lua 5.5 的 `math` 库里有一批函数会区分：

- 原值本来就是整数
- 结果虽然经由浮点计算，但可以无损收回整数

这一轮实现里沿用了当前 runtime 的 number 转换入口，并补了一个统一的数值归一化出口：

- 该保留整数的结果返回 `LuaValueKind.Integer`
- 其余结果返回 `LuaValueKind.Float`

这样 `ceil` / `floor` / `modf` / 常量边界值的 Lua 层可见行为更稳定，也更接近官方库。

### 3. `utf8` 库按“字节位置”语义实现，而不是按 .NET 字符索引实现

Lua 手册里的 `utf8.offset` / `utf8.codepoint` / `utf8.len` 都是**按 UTF-8 字节位置**工作的，不是按 Unicode 字符索引工作的。

这一轮实现明确按 UTF-8 字节流处理：

- 先把 runtime 里的 `string` 转成 UTF-8 bytes
- 再按官方 `lutf8lib.c` 的 decode 规则做偏移、计数和遍历

这样 `π`、汉字等多字节字符的索引结果能和 Lua 5.5 对齐。

### 4. `utf8.char` 先对齐 Unicode scalar range

当前 runtime 的字符串载体仍然是 C# `string`，不是原始 byte string。

所以这一轮的 `utf8.char` 先稳定支持：

- `0x0000` 到 `0x10FFFF`
- 排除 surrogate range

这已经覆盖正常 UTF-8 文本路径；如果后面项目要进一步支持“非 Unicode scalar 的原始字节字符串”场景，再单独扩展字符串模型。

## 当前支持范围

这一轮新增支持：

- `_ENV.table`
- `_ENV.math`
- `_ENV.utf8`
- `table.concat`
- `table.insert`
- `table.remove`
- `table.move`
- `table.sort`
- `table.pack`
- `table.unpack`
- `math.abs`
- `math.ceil`
- `math.floor`
- `math.max`
- `math.min`
- `math.sqrt`
- `math.log`
- `math.exp`
- `math.sin`
- `math.cos`
- `math.tan`
- `math.asin`
- `math.acos`
- `math.atan`
- `math.deg`
- `math.rad`
- `math.fmod`
- `math.modf`
- `math.tointeger`
- `math.type`
- `math.ult`
- `math.pi`
- `math.huge`
- `math.maxinteger`
- `math.mininteger`
- `utf8.offset`
- `utf8.codepoint`
- `utf8.char`
- `utf8.len`
- `utf8.codes`
- `utf8.charpattern`

这一轮明确**还不支持**：

- `table.create`
- `math.random` / `math.randomseed`
- `math.frexp` / `math.ldexp`
- 依赖后续模式匹配引擎的 `utf8.charpattern` 实际消费路径
- 超出当前 runtime 字符串模型能力的“非 Unicode scalar 输出”路径

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/table_library_chunk.lua`
- `test/fixtures/lua55/chunks/table_library_chunk.luac`
- `test/fixtures/lua55/source/math_library_chunk.lua`
- `test/fixtures/lua55/chunks/math_library_chunk.luac`
- `test/fixtures/lua55/source/utf8_library_chunk.lua`
- `test/fixtures/lua55/chunks/utf8_library_chunk.luac`

它们覆盖：

- `table` 的拼接、插入、删除、搬移、排序、pack/unpack
- `math` 的基础数值函数、三角函数、转换、常量与 `ult`
- `utf8` 的偏移、码点、重建、长度与迭代器

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `table`
- [x] 在 `_ENV` 中注册 `math`
- [x] 在 `_ENV` 中注册 `utf8`
- [x] 实现 `table` 目标函数集
- [x] 实现 `math` 目标函数集
- [x] 实现 `utf8` 目标函数集
- [x] 新增 LuaState 单元测试
- [x] 新增真实 fixture
- [x] 新增 VM fixture 测试

## 完成标准

本轮完成后，应满足：

- 真实 Lua 5.5 chunk 可以直接访问 `table` / `math` / `utf8`
- Step 08 路线图列出的核心函数都已经有最小可用实现
- `table` / `math` / `utf8` 的关键路径都有单元测试或 fixture 测试钉住
- Step 08 的计划范围已经收口

## 下一步

Step 08 到这里结束。

接下来进入：

- Step 09：完整 `string` 库与模式匹配
- Step 10：`coroutine` 库
