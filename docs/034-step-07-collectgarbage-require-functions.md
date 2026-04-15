# 第 7 步：基础库——collectgarbage / require

## 状态

已完成当前这一轮。

这一轮把 Step 07 剩下的两个缺口补齐了：

- `collectgarbage` 的最小版本
- `require` 的最小版本

做完这一轮之后，Step 07 约定的基础库核心函数范围已经收口。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/loadlib.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮重点对齐的点是：

- `luaB_collectgarbage`
- `ll_require`
- `searcher_preload`
- `searcher_Lua`
- `findloader`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `collectgarbage`
- 在 `_ENV` 中预置 `require`
- 在 `_ENV` 中预置最小 `package` 表
- 先支持 `package.loaded` / `package.preload` / `package.path` / `package.searchers`
- 让 `require` 先支持 preload searcher
- 让 `require` 先支持基于 `package.path` 的二进制 Lua searcher
- 让 `require` 支持模块缓存
- 让 `collectgarbage` 先支持 `stop` / `restart` / `collect` / `count` / `step` / `isrunning`
- 用真实 Lua 5.5 chunk 验证 GC 与模块加载行为

## 设计原则

### 1. `collectgarbage` 先对齐 Lua 层可见结果，不提前实现真实 GC 子系统

当前项目还没有自己的垃圾回收器实现，所以这一轮不试图伪造完整的 Lua GC 状态机。

最小版本只先保证 Lua 层能看到稳定、可测试的行为：

- `isrunning`
- `stop` / `restart`
- `count`
- `collect`
- `step`

具体做法是：

- 运行状态由 `LuaState` 自己维护
- `count` 用宿主当前托管堆大小近似表示
- `collect` 直接委托宿主 GC
- `step` 先固定返回 `false`

这足以支撑当前阶段的脚本兼容性，同时不和未来真正的 GC 子系统绑定死。

### 2. `require` 只实现最小 package 骨架，不提前展开完整 package 系统

完整 package 系统属于 Step 14。

所以这一轮不去碰：

- `package.cpath`
- C module searcher
- `loadlib`
- `searchpath`
- 系统默认安装目录和平台相关细节

当前最小实现只保留真实脚本最常用的部分：

- `package.loaded`
- `package.preload`
- `package.path`
- `package.searchers`
- `require`

并且 searcher 只先放两种：

- preload
- Lua binary chunk file searcher

### 3. 模块缓存按 Lua 语义处理 nil 返回

`require` 的关键不是“加载文件”，而是“缓存模块值”。

这一轮按官方语义处理：

- loader 返回非 `nil`：缓存该值
- loader 返回 `nil` 且模块自己没写 `package.loaded[name]`：缓存 `true`
- 后续再次 `require` 同一模块时，直接返回缓存值

这样 `package.preload` 模块、文件模块和“只做副作用不返回值”的模块都能对齐 Lua 的真实行为。

### 4. 先把 file searcher 和现有 `loadfile` 二进制路径打通

项目上一轮已经有了 `load` / `loadfile` / `dofile` 的二进制 chunk 路径。

这一轮没有重新做一套文件加载，而是让 `require` 的 Lua searcher 直接复用当前 runtime 的 chunk 读取与 closure 生成链路。

这样好处很直接：

- `require` 和 `loadfile` 的二进制加载行为一致
- 模块系统和 VM 之间没有重复实现
- 后面做文本 chunk 支持时，也只需要沿现有入口继续扩展

## 当前支持范围

这一轮新增支持：

- `_ENV.collectgarbage`
- `_ENV.require`
- `_ENV.package`
- `package.loaded`
- `package.preload`
- `package.path`
- `package.searchers`
- `require("mod")` 读取 `package.preload`
- `require("mod")` 按 `package.path` 查找 `.luac`
- `require` 的模块缓存
- `collectgarbage("count")`
- `collectgarbage("isrunning")`
- `collectgarbage("stop")`
- `collectgarbage("restart")`
- `collectgarbage("collect")`
- `collectgarbage("step")`

这一轮明确**还不支持**：

- `package.cpath`
- C module searcher
- `package.loadlib`
- `package.searchpath`
- 平台默认 module 搜索路径
- `collectgarbage` 的 `generational` / `incremental` / `param`

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/collectgarbage_chunk.lua`
- `test/fixtures/lua55/chunks/collectgarbage_chunk.luac`
- `test/fixtures/lua55/source/require_file_chunk.lua`
- `test/fixtures/lua55/chunks/require_file_chunk.luac`
- `test/fixtures/lua55/source/require_chunk.lua`
- `test/fixtures/lua55/chunks/require_chunk.luac`

它们覆盖：

- GC 运行状态切换
- `collectgarbage("count")` 的 Lua 层可见结果
- `package.preload`
- `package.path`
- 文件模块 searcher
- `require` 缓存
- loader 返回 `nil` 时缓存 `true`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `collectgarbage`
- [x] 在 `_ENV` 中注册 `require`
- [x] 在 `_ENV` 中注册最小 `package` 表
- [x] 新增 preload searcher
- [x] 新增二进制 Lua file searcher
- [x] 实现模块缓存
- [x] 实现 `collectgarbage` 最小选项集
- [x] 新增运行时测试
- [x] 新增真实 fixture
- [x] 新增 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用 `collectgarbage` / `require`
- `package.preload` 和 `package.path` 两条基本模块加载路径已经可用
- `require` 的缓存行为已有测试钉住
- `collectgarbage` 的最小可见语义已有测试钉住
- Step 07 计划范围已经收口

## 下一步

Step 07 到这里结束。

接下来进入：

- Step 08：`table` / `math` / `utf8` 基础库
- Step 09：完整 `string` 库与模式匹配
- Step 14：完整 package / C module / `loadlib`
