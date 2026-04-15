# 第 6 步：userdata 元方法分发

## 状态

已完成当前这一轮。

这一轮继续沿着元方法主线往前补，把 `LuaUserData` 真正接进统一的 metatable 查找里，让前面已经打通的那批元方法路径开始覆盖 userdata。

这次主要打通的是：

- userdata `__index`
- userdata `__newindex`
- userdata `__call`
- userdata `__len`
- userdata `__unm`
- userdata `__eq`
- userdata `__close`

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/ldebug.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaT_gettmbyobj`
- `luaV_finishget`
- `luaV_finishset`
- `luaG_typeerror`
- userdata 与 table 共用的大部分元方法分发入口

## 本步骤范围

本轮先落这几件事：

- 在 `LuaUserData` 中补 metatable 挂载与元方法查找
- 让 VM 的通用元方法查找同时覆盖 table 和 userdata
- 让 `__close` 也开始支持 userdata
- 把 `index`、`call`、`get length of` 这几类类型错误改成更接近 Lua 的报错格式
- 用真实 Lua 5.5 chunk 验证 userdata 的几条代表性路径

## 设计原则

### 1. 不为 userdata 单独再造一套分发器

这一轮最重要的取舍，是不另外复制一套：

- userdata 算术分发
- userdata 调用分发
- userdata 表访问分发

而是直接把 `LuaUserData` 接进现有的通用 metatable 查找。

这样前面已经打通的：

- `__call`
- `__len`
- `__unm`
- `__eq`
- `__index`
- `__newindex`
- `__close`

就都能顺着同一套入口继续工作。

### 2. 先靠宿主注入 userdata，先把 VM 语义钉住

当前仓库还没有完整标准库，也没有在 Lua 层直接构造 userdata 的能力。

所以这一轮的真实 chunk 验证方式是：

- Lua 源码里直接访问全局变量 `ud`、`ud2`、`sink`
- C# 测试在执行前把这些全局值注入 `_ENV`

这样能保持：

- fixture 仍然是官方 Lua 5.5 `luac` 编出来的真实 chunk
- VM 走的仍然是正式的取指执行路径
- 不需要为了这轮先把 `io`、`debug` 之类标准库提前做出来

### 3. 先补 Lua 风格的类型错误出口

userdata 接进来以后，原来那些“暂未实现”的错误出口就更别扭了。

所以这一轮顺手把几类常见错误统一成了更接近官方的格式：

- `attempt to index a <type> value`
- `attempt to call a <type> value`
- `attempt to get length of a <type> value`

现在虽然还没有补变量名、局部名这些调试信息，但至少主错误形状已经和 Lua 对齐了。

## 当前支持范围

这一轮新增支持：

- `LuaUserData` 的 metatable 挂载
- userdata 的 `__index` / `__newindex`
- userdata 的 `__call`
- userdata 的 `__len`
- userdata 的 `__unm`
- userdata 的 `__eq`
- userdata 的 `__close`
- userdata 相关的最小 Lua 风格类型错误

当前这一轮还没有展开的是：

- Lua 层直接创建 userdata 的能力
- userdata 的宿主资源模型
- 更完整的 userdata 标准库接入
- 带局部变量名的详细错误信息

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/userdata_index_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_index_chunk.luac`
- `test/fixtures/lua55/source/userdata_newindex_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_newindex_chunk.luac`
- `test/fixtures/lua55/source/userdata_call_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_call_chunk.luac`
- `test/fixtures/lua55/source/userdata_len_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_len_chunk.luac`
- `test/fixtures/lua55/source/userdata_unm_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_unm_chunk.luac`
- `test/fixtures/lua55/source/userdata_eq_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_eq_chunk.luac`
- `test/fixtures/lua55/source/userdata_close_chunk.lua`
- `test/fixtures/lua55/chunks/userdata_close_chunk.luac`

它们分别覆盖：

- userdata 读字段
- userdata 写字段
- userdata 调用
- userdata 取长度
- userdata 一元负号
- userdata 相等比较
- userdata `__close`

## 实现清单

- [x] 编写本轮文档
- [x] 为 `LuaUserData` 补 metatable 与元方法查找
- [x] 让 VM 的通用元方法查找覆盖 userdata
- [x] 让 userdata 支持 `__close`
- [x] 统一一批 Lua 风格类型错误出口
- [x] 新增对应真实 fixture
- [x] 新增对应运行时测试与 VM 测试

## 完成标准

本轮完成后，应满足：

- userdata 可以挂 metatable
- 现有那批已经实现的元方法分发入口可以复用于 userdata
- 真实 chunk 可以覆盖 userdata 的读取、写入、调用、长度、一元运算、比较和关闭路径
- 常见类型错误不再落到“未实现”异常上

## 下一步

接下来继续往下补：

- 更完整的对象访问错误细节
- userdata 的宿主接入与标准库路径
- 更一般的元方法组合与边界语义
