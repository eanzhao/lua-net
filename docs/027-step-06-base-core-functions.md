# 第 6 步：基础库第一批核心函数

## 状态

已完成当前这一轮。

这一轮继续沿着基础库主线往前补，但不再只盯着 metatable 和 `raw*`。

这次落地的是 Lua 里最常用、也最容易放大运行时能力的一组核心函数：

- `type`
- `assert`
- `select`
- `pcall`

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/ldo.c`
- `references/lua-5.5.0/src/lvm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaB_type`
- `luaB_assert`
- `luaB_select`
- `luaB_pcall`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `type`
- 在 `_ENV` 中预置 `assert`
- 在 `_ENV` 中预置 `select`
- 在 `_ENV` 中预置 `pcall`
- 让 `pcall` 复用 VM 当前已经存在的 callable 解析路径
- 用真实 Lua 5.5 chunk 验证这组函数的 Lua 层行为

## 设计原则

### 1. 先补最常用、最能暴露语义边界的函数

这四个函数看起来不大，但都很适合拿来压运行时语义：

- `type` 能直接验证值类型分类
- `assert` 能直接验证真假值和错误对象传递
- `select` 能直接验证 vararg 下标和开放结果
- `pcall` 能直接验证受保护调用和错误收敛

把这组函数补上之后，后面继续做 `xpcall`、迭代器和更多基础库时，地基会稳很多。

### 2. `pcall` 不再额外造一条调用链

这一轮最关键的点，不是把 `pcall` 做成“能跑”，而是不要再单独复制一套调用逻辑。

现在 `pcall` 走的是 `LuaState.InvokeCallable`，而 `LuaVirtualMachine` 会把它接回当前统一的 callable 解析路径。

这样 `pcall` 处理的就不只是普通函数，还包括：

- Lua 闭包
- native closure
- table / userdata metatable 上的最小 `__call`

也就是说，这一轮没有新造第二套调用协议，而是把基础库函数接回了当前 VM 主线。

### 3. `assert` 和 `select` 保持 Lua 层看得见的语义

这两个函数都容易被写成“差不多”。

这一轮先把 Lua 层最容易看见的行为钉住：

- `assert(true, ...)` 原样返回所有参数
- `assert(false, err)` 直接把第二个参数作为错误对象抛出
- `assert(false)` 使用默认错误消息 `"assertion failed!"`
- `select("#", ...)` 返回 vararg 个数
- `select(n, ...)` 支持正索引和负索引

## 当前支持范围

这一轮新增支持：

- `_ENV.type`
- `_ENV.assert`
- `_ENV.select`
- `_ENV.pcall`
- `type` 的 Lua 类型名映射
- `assert` 的多返回值透传语义
- `assert` 的默认错误消息与自定义错误对象
- `select("#", ...)` 计数语义
- `select(n, ...)` 的正负索引语义
- `pcall` 的 `(true, ...)` / `(false, err)` 返回协议
- `pcall` 复用当前统一 callable 解析路径

当前这一轮还没有展开的是：

- `xpcall`
- 更完整的错误处理级别与 traceback
- `tonumber` / `tostring`
- `next` / `pairs` / `ipairs`
- 更系统的 `Lua.StandardLib` 模块拆分

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/type_chunk.lua`
- `test/fixtures/lua55/chunks/type_chunk.luac`
- `test/fixtures/lua55/source/select_chunk.lua`
- `test/fixtures/lua55/chunks/select_chunk.luac`
- `test/fixtures/lua55/source/pcall_chunk.lua`
- `test/fixtures/lua55/chunks/pcall_chunk.luac`

它们分别覆盖：

- `type(nil)`、`type(number)`、`type(string)`、`type(table)`、`type(function)`、`type(userdata)`
- `assert` 的多返回值透传
- `select("#", ...)` 与正负索引
- `pcall(function() ... end)` 的成功路径
- `pcall(ud, ...)` 走 userdata `__call` 的路径
- `pcall(erroringFn)` 和 `pcall(assert, false, "...")` 的失败路径

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `type`
- [x] 在 `_ENV` 中注册 `assert`
- [x] 在 `_ENV` 中注册 `select`
- [x] 在 `_ENV` 中注册 `pcall`
- [x] 为 `LuaState` 增加统一 callable 调用入口
- [x] 让 `LuaVirtualMachine` 把 `pcall` 接回当前 callable 解析路径
- [x] 新增对应运行时测试
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用 `type` / `assert` / `select` / `pcall`
- 这组函数能在真实 Lua 5.5 chunk 中跑通
- `pcall` 不需要单独维护第二套调用协议
- `pcall` 对普通函数和最小 `__call` 路径都可用

## 下一步

接下来继续往下补：

- `xpcall`
- `tonumber` / `tostring`
- `next` / `pairs` / `ipairs`
- 更完整的错误处理与标准库拆分
