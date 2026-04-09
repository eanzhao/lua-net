# 第 4 步补充：表访问元方法分发

## 状态

已完成当前这一轮。

这一轮继续沿着对象访问主线往前补，把 table metatable 上的 `__index` / `__newindex` 接进 `GET*` / `SET*` 这组指令里。

这次主要打通的是：

- table `__index` 走 table fallback
- table `__index` 走 function fallback
- table `__newindex` 走 table fallback
- table `__newindex` 走 function fallback
- 已有原始键命中时绕过 `__newindex`

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltable.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaV_finishget`
- `luaV_finishset`
- `OP_GETTABUP` / `OP_GETTABLE` / `OP_GETI` / `OP_GETFIELD`
- `OP_SETTABUP` / `OP_SETTABLE` / `OP_SETI` / `OP_SETFIELD`
- `TM_INDEX`
- `TM_NEWINDEX`

## 本步骤范围

本轮先落这几件事：

- 在 `LuaTable` 里补原始命中判断
- 让 `GET*` 在原始 miss 时继续查找 `__index`
- 支持 `__index` 为 table 和 function 两种路径
- 让 `SET*` 在原始 miss 时继续查找 `__newindex`
- 支持 `__newindex` 为 table 和 function 两种路径
- 保持已有原始键更新时绕过 `__newindex`
- 用真实 Lua 5.5 chunk 验证以上几条路径

## 设计原则

### 1. 先把“原始命中”和“原始 miss”区分清楚

`__newindex` 的关键，不是“有没有元方法”，而是“当前表上这个键是不是已经原始存在”。

所以这一轮先给 `LuaTable` 补了原始 `TryGetValue`，让 VM 能明确区分：

- 原始命中，直接写当前表
- 原始 miss，再决定是否查 `__newindex`

这样 `t.answer = 42` 覆盖已有字段时，才能对齐 Lua 5.5，不误走元方法。

### 2. 沿用官方的链式 finish 结构

官方 `luaV_finishget` / `luaV_finishset` 的思路很直接：

- 原始访问成功就结束
- 没有对应元方法也结束
- 元方法是函数就调用
- 元方法不是函数，就把目标切到这个值上继续走

这一轮也按这条线来做，而不是把 `table` fallback 和 `function` fallback 拆成两套互不相干的分支。

### 3. 先把 table 路径做稳

这一轮仍然只先补最常见、最好用真实 chunk 钉住的路线：

- table
- table metatable
- `__index`
- `__newindex`

userdata 和更完整的非 table 错误细节，后面再接。

## 当前支持范围

这一轮新增支持：

- table `__index` 的 table fallback
- table `__index` 的 function fallback
- table `__newindex` 的 table fallback
- table `__newindex` 的 function fallback
- 已有原始键命中时绕过 `__newindex`
- `__index` / `__newindex` 的最小链长保护

当前这一轮还没有展开的是：

- userdata 的 `__index` / `__newindex`
- 非 table 目标的完整错误消息
- 更一般的对象访问元方法组合

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/meta_index_table_chunk.lua`
- `test/fixtures/lua55/chunks/meta_index_table_chunk.luac`
- `test/fixtures/lua55/source/meta_index_function_chunk.lua`
- `test/fixtures/lua55/chunks/meta_index_function_chunk.luac`
- `test/fixtures/lua55/source/meta_newindex_table_chunk.lua`
- `test/fixtures/lua55/chunks/meta_newindex_table_chunk.luac`
- `test/fixtures/lua55/source/meta_newindex_function_chunk.lua`
- `test/fixtures/lua55/chunks/meta_newindex_function_chunk.luac`
- `test/fixtures/lua55/source/meta_newindex_existing_chunk.lua`
- `test/fixtures/lua55/chunks/meta_newindex_existing_chunk.luac`

它们分别覆盖：

- `__index = fallbackTable`
- `__index = function(self, key) ... end`
- `__newindex = fallbackTable`
- `__newindex = function(self, key, value) ... end`
- 已有字段更新时绕过 `__newindex`

## 实现清单

- [x] 编写本轮文档
- [x] 为 `LuaTable` 补原始命中判断
- [x] 在 `GET*` 中支持最小 `__index` 分发
- [x] 在 `SET*` 中支持最小 `__newindex` 分发
- [x] 处理 table fallback 和 function fallback
- [x] 处理已有原始键绕过 `__newindex`
- [x] 新增对应真实 fixture
- [x] 新增对应运行时测试与 VM 测试

## 完成标准

本轮完成后，应满足：

- table 的字段读取在原始 miss 时可以继续走 `__index`
- table 的字段写入在原始 miss 时可以继续走 `__newindex`
- `__index` / `__newindex` 的 table 和 function 两种 fallback 都能被真实 chunk 覆盖
- 已有键更新不会错误触发 `__newindex`

## 下一步

接下来继续往下补：

- userdata 路径
- 更完整的对象访问错误细节
- 更一般的元方法调度
