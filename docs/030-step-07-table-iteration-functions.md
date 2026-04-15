# 第 7 步：基础库——next / pairs / ipairs

## 状态

已完成当前这一轮。

这一轮继续沿着基础库主线补 Lua 最常用的表迭代接口，把 `next`、`pairs`、`ipairs` 接到了当前运行时和 VM 主线上。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/lapi.c`
- `references/lua-5.5.0/src/ltable.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaB_next`
- `luaB_pairs`
- `luaB_ipairs`
- `__pairs`
- `invalid key to 'next'`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `next`
- 在 `_ENV` 中预置 `pairs`
- 在 `_ENV` 中预置 `ipairs`
- 让 `pairs` 支持 table / userdata 上的 `__pairs`
- 给 `LuaTable` 增加最小可控的遍历入口，供 `next` 复用
- 用真实 Lua 5.5 chunk 验证这组函数的 Lua 层行为

## 设计原则

### 1. 先把迭代入口收进运行时，而不是散落在 VM 和基础库里

这一轮新增的是基础库函数，但真正被复用的核心逻辑是“表如何给出下一个键值对”。

所以没有把 `next` 的遍历规则直接写死在 `LuaState` 里，而是把最小遍历能力收进 `LuaTable`，再让 `next` 走这条入口。

这样做的好处是：

- 运行时和基础库职责更清楚
- 后面继续做更完整的表布局时，有明确替换点
- `pairs` 默认路径和 `next` 可以天然复用同一份逻辑

### 2. `pairs` 先补最关键的 `__pairs` 分支

Lua 5.5 里 `pairs` 不是单纯返回 `next, t, nil`。

如果目标值有 `__pairs` 元方法，`pairs` 应该先调用它，再把它返回的迭代器状态透传出去。

这一轮先把最重要的 Lua 层可见语义钉住：

- 没有 `__pairs` 时，返回默认 `next` 迭代器
- 有 `__pairs` 时，走元方法返回值
- 元方法结果不足 4 个时，按 Lua 调用协议补 `nil`

### 3. `ipairs` 先对齐最小数组迭代快速路径

`ipairs` 的核心语义是：

- 从索引 `1` 开始
- 每次加一
- 遇到第一个 `nil` 就停

这一轮先把这条最常用的路径补齐，用来支撑普通数组 table 的迭代。

当前还没有把 `lua_geti` 的更完整宿主行为全部搬过来，所以这轮重点仍然是普通 table 的整数索引访问。

## 当前支持范围

这一轮新增支持：

- `_ENV.next`
- `_ENV.pairs`
- `_ENV.ipairs`
- `next(t)` / `next(t, key)` 的最小遍历语义
- `next` 对无效当前键抛出 `"invalid key to 'next'"`
- `pairs` 默认返回 `next, state, nil, nil`
- `pairs` 的 `__pairs` 元方法路径
- `ipairs` 的顺序整数索引迭代
- `ipairs` 在首个空洞处停止

当前这一轮还没有展开的是：

- 更完整的表内部遍历顺序对齐
- `ipairs` 对 `lua_geti` 全路径语义的覆盖
- `next` 迭代期间修改表的更多边界行为
- `table` 库、`string` 库、`math` 库、`coroutine` 基础版

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/iterators_chunk.lua`
- `test/fixtures/lua55/chunks/iterators_chunk.luac`

它覆盖：

- `next` 的起始与结束返回
- 普通 `pairs` 对 table 的遍历
- `pairs` 的 `__pairs` 元方法路径
- `ipairs` 在首个 `nil` 处停止

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `next`
- [x] 在 `_ENV` 中注册 `pairs`
- [x] 在 `_ENV` 中注册 `ipairs`
- [x] 为 `LuaTable` 增加最小遍历入口
- [x] 为 `pairs` 增加 `__pairs` 支持
- [x] 新增对应运行时测试
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用 `next` / `pairs` / `ipairs`
- 真实 Lua 5.5 chunk 可以覆盖普通表迭代路径
- `pairs` 已经能和当前元方法路径协作
- `ipairs` 的顺序迭代与空洞停止行为已经被测试钉住

## 下一步

接下来继续往下补：

- `table` 库
- `string` 库
- `math` 库
- `coroutine` 基础版
- 更完整的错误处理与 traceback
