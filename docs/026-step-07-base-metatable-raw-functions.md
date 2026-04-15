# 第 7 步：基础库——元表与 raw 函数

## 状态

已完成当前这一轮。

这一轮开始正式往基础库主线迈一步，先把最小但很关键的一组函数挂到 `_ENV` 里：

- `getmetatable`
- `rawget`
- `rawset`
- `rawlen`
- `rawequal`

同时把 `setmetatable` 的保护元表语义补齐。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/ldebug.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaB_getmetatable`
- `luaB_setmetatable`
- `luaB_rawequal`
- `luaB_rawlen`
- `luaB_rawget`
- `luaB_rawset`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `getmetatable`
- 在 `_ENV` 中预置 `rawget` / `rawset` / `rawlen` / `rawequal`
- 让 `setmetatable` 支持 protected metatable 保护
- 让 `getmetatable` 返回 `__metatable` 保护字段
- 用真实 Lua 5.5 chunk 验证这组函数的基础行为
- 把 userdata 注入 `_ENV`，验证 userdata 与 `getmetatable` 的协作路径

## 设计原则

### 1. 先补最能放大运行时能力的基础函数

这一轮选的不是“看起来最显眼”的函数，而是最能把前面几轮工作串起来的函数。

例如：

- `getmetatable` 能直接验证 table / userdata 的 metatable 承载
- `rawget` / `rawset` 能直接验证“原始访问”和“元方法访问”的边界
- `rawlen` 能直接验证字符串与表的原始长度语义
- `rawequal` 能直接验证不经过元方法的原始相等规则

这几项补上之后，后面做更复杂的标准库和调试路径都会轻松很多。

### 2. 保护元表和原始访问要分开看

这一轮里有两个容易混在一起的概念：

- `getmetatable` / `setmetatable` 的保护元表语义
- `rawget` / `rawset` / `rawlen` / `rawequal` 的“绕过元方法”语义

前者处理的是“能不能看、能不能改元表”。

后者处理的是“访问值时要不要绕过元方法”。

这两条线虽然都围绕 metatable，但语义不是一回事，所以实现上也分开处理了。

### 3. 真实 chunk 继续只做 Lua 层看得见的那一面

这一轮的 VM fixture 仍然尽量写成正常的 Lua 源码，然后用官方 `luac 5.5.0` 编译。

其中 userdata 那条线依然沿用上一轮的方式：

- Lua 源码只访问 `_ENV` 里的 `ud`、`mt`
- C# 测试在执行前把这些值注入到 `_ENV`

这样既能保持 chunk 是官方产物，又不用为了这一步提前把更大的宿主 API 一口气做完。

## 当前支持范围

这一轮新增支持：

- `_ENV.getmetatable`
- `_ENV.rawget`
- `_ENV.rawset`
- `_ENV.rawlen`
- `_ENV.rawequal`
- `setmetatable` 的 protected metatable 检查
- `getmetatable` 返回 `__metatable` 保护字段
- `rawget(nilKey)` / `rawget(nanKey)` 返回 `nil`
- `rawset(nilKey)` / `rawset(nanKey)` 抛出 Lua 风格错误
- `rawequal(1, 1.0)` 的原始数值相等语义

当前这一轮还没有展开的是：

- `pcall` / `xpcall`
- `type` / `assert` / `select`
- 更完整的基础库错误对象和栈信息
- 更大的标准库模块拆分

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/raw_metatable_chunk.lua`
- `test/fixtures/lua55/chunks/raw_metatable_chunk.luac`
- `test/fixtures/lua55/source/protected_metatable_chunk.lua`
- `test/fixtures/lua55/chunks/protected_metatable_chunk.luac`
- `test/fixtures/lua55/source/userdata_getmetatable_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_getmetatable_chunk.luac`

它们分别覆盖：

- `rawget` / `rawset` / `rawlen` / `rawequal` / `getmetatable`
- protected metatable 的可见值
- userdata 的 `getmetatable`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `getmetatable`
- [x] 在 `_ENV` 中注册 `rawget` / `rawset` / `rawlen` / `rawequal`
- [x] 为 `setmetatable` 补 protected metatable 检查
- [x] 为 `getmetatable` 补 `__metatable` 可见值语义
- [x] 新增对应运行时测试
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用这组基础库函数
- `getmetatable` / `setmetatable` 的保护元表语义可用
- `raw*` 函数能绕开元方法，回到原始访问行为
- 真实 chunk 可以覆盖 table 与 userdata 的关键路径

## 下一步

接下来继续往下补：

- `type` / `assert` / `select`
- `pcall` / `xpcall`
- 更完整的基础库错误信息
- 更系统的 `Lua.StandardLib` 模块拆分
