# 第 7 步：基础库——字符串元表与最小 string 支持

## 状态

已完成当前这一轮。

这一轮把 Step 07 里和字符串直接相关的缺口先补上，让 Lua 5.5 的字符串元表路径在当前运行时和 VM 里真正可用。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lstrlib.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮重点对齐的点是：

- `createmetatable`
- `str_upper` / `str_lower` / `str_len`
- 字符串类型元表的 `__index`
- Lua 5.5 新增的字符串算术元方法

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置最小 `string` 表
- 先实现 `string.upper` / `string.lower` / `string.len`
- 为字符串类型挂统一元表
- 让字符串元表的 `__index` 指向 `string` 表
- 让 `("lua"):upper()` 和 `("abc").upper("abc")` 工作
- 为字符串补 `__add` / `__sub` / `__mul` / `__mod` / `__pow` / `__div` / `__idiv` / `__unm`
- 用真实 Lua 5.5 chunk 验证方法调用与字符串算术行为

## 设计原则

### 1. 先做最小 string 表，不提前展开完整 string 库

Step 07 的目标不是一次性把 `string` 库做完，而是先补齐真实脚本最容易撞到的基础能力。

所以这一轮只先加：

- `string.upper`
- `string.lower`
- `string.len`

其余 `string.byte`、`string.sub`、模式匹配等仍然留到 Step 09。

### 2. 字符串元表走类型级注册，而不是伪造每个字符串对象

Lua 的字符串是不可变值，不适合像 table / userdata 那样给单个对象挂 metatable。

这一轮在 `LuaState` 里引入按 `LuaValueKind` 管理的类型元表：

- table / userdata 继续走对象自己的 metatable
- string 走状态级的类型元表

这样 `getmetatable("lua")`、`("lua"):upper()`、`"10" + 1` 都能走到统一的字符串元表逻辑。

### 3. 字符串算术复用现有数值运算逻辑

这一轮没有在 `LuaState` 里重复写一套算术规则，而是把基础数值运算提到 `LuaValueHelper` 里复用。

这样有两个直接收益：

- VM 自己的算术路径和字符串元表共享同一套数值结果规则
- 整数除法、取模、幂运算这类边界行为不会在两处漂移

### 4. 左侧字符串算术失败时，仍然要给右侧元方法机会

Lua 5.5 的字符串算术是通过字符串元表提供的。

但当左侧是字符串、右侧是带元方法的 table / userdata，而且字符串本身又不能把参数转成数值时，不能直接报错，还要把机会交给右侧的对应元方法。

这一轮把这条链路也补上了，这样类似：

- `"x" + obj`

只要 `obj` 有 `__add`，仍然能继续成立。

## 当前支持范围

这一轮新增支持：

- `_ENV.string`
- `string.upper` / `string.lower` / `string.len`
- `getmetatable("lua")`
- 字符串元表 `__index`
- `("lua"):upper()` 这类字符串方法调用
- `("abc").upper("abc")` 这类直接字段访问再调用
- `"10" + 1`
- `"10" - 1`
- `"6" * 7`
- `"9" % 4`
- `"2" ^ 3`
- `"7" / 2`
- `"7" // 2`
- `-"5"`

这一轮还没有展开的是：

- `load` / `dofile` / `loadfile`
- `collectgarbage` 的最小版本
- `require` 的最小版本
- 完整 `string` 库和模式匹配

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/string_method_chunk.lua`
- `test/fixtures/lua55/chunks/string_method_chunk.luac`
- `test/fixtures/lua55/source/string_arith_chunk.lua`
- `test/fixtures/lua55/chunks/string_arith_chunk.luac`

它们覆盖：

- 字符串 `:` 方法调用
- 字符串字段访问后的普通函数调用
- 字符串元表 `__index`
- 字符串算术元方法
- 左侧字符串算术失败后委托右侧 `__add`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册最小 `string` 表
- [x] 为 `LuaState` 增加字符串类型元表
- [x] 为字符串注册 `__index`
- [x] 为字符串注册最小算术元方法
- [x] 把共享数值运算逻辑提到 `LuaValueHelper`
- [x] 新增运行时测试
- [x] 新增真实 fixture
- [x] 新增 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接访问全局 `string`
- 字符串可以通过元表访问 `upper` / `lower` / `len`
- 字符串方法调用已经走通 VM 的 `__index` 链
- Lua 5.5 新增字符串算术行为已有测试钉住
- 左右操作数元方法协作路径已经覆盖

## 下一步

接下来继续往下补：

- `load` / `dofile` / `loadfile`
- `collectgarbage` 的最小版本
- `require` 的最小版本
