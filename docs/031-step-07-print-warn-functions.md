# 第 7 步：基础库——print / warn

## 状态

已完成当前这一轮。

这一轮继续沿着基础库主线补最常用的输出接口，把 `print` 和 `warn` 接到了当前运行时和 VM 主线上。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/lauxlib.c`
- `references/lua-5.5.0/src/lua.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaB_print`
- `luaB_warn`
- `luaL_tolstring`
- `luaL_checkstring`
- `@on` / `@off`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `print`
- 在 `_ENV` 中预置 `warn`
- 让 `print` 复用当前 `tostring` 语义
- 让 `warn` 支持标准 warning 控制消息 `@on` / `@off`
- 给 `LuaState` 增加宿主可注入的输出与告警 sink
- 用真实 Lua 5.5 chunk 验证这组函数的 Lua 层行为

## 设计原则

### 1. 运行时负责语义，宿主负责真正输出到哪里

这一轮没有把 `print` / `warn` 直接写死到 `Console.Out` 或 `Console.Error`。

`LuaState` 只负责：

- 生成要输出的最终文本
- 维护 warning 的开关与拼接状态

至于文本最终去哪里，由宿主通过 sink 注入。

这样做的好处是：

- 运行时不会和控制台直接耦合
- 测试可以稳定捕获输出
- 后面做 CLI、REPL 或嵌入式宿主时更容易复用

### 2. `print` 走 `tostring`，而不是直接偷看值对象

Lua 里的 `print` 并不是简单把值 `ToString()` 一下。

它会先走 `tostring` 路径，所以这一轮保持同样的结构：

- 每个参数先经 `tostring`
- 参数之间用制表符 `\t` 连接
- 最终生成一整行输出

这样 table / userdata 上已有的 `__tostring` 路径也会自然生效。

### 3. `warn` 对齐标准 warning 函数的最小行为

`warn` 的重点不只是“发一条文本”，还包括控制消息。

这一轮先把最关键的语义钉住：

- `warn("@on")` 打开 warning
- `warn("@off")` 关闭 warning
- 单条以 `@` 开头的未知控制消息被忽略
- 多参数 warning 按片段拼接
- 最终输出带 `Lua warning: ` 前缀

当前这轮没有继续展开更复杂的 C API warning 定制，只先对齐标准 warning 函数的 Lua 层可见结果。

## 当前支持范围

这一轮新增支持：

- `_ENV.print`
- `_ENV.warn`
- `print` 的制表符分隔输出
- `print` 对 `tostring` / `__tostring` 的复用
- `warn` 的字符串拼接
- `warn` 的 `@on` / `@off` 控制消息
- warning 默认关闭
- 宿主可注入的 print / warning sink

当前这一轮还没有展开的是：

- `load` / `dofile` / `loadfile`
- `collectgarbage` 的最小版本
- `require` 的最小版本
- 字符串元表 `__index`
- 字符串算术元方法

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/print_warn_chunk.lua`
- `test/fixtures/lua55/chunks/print_warn_chunk.luac`

它覆盖：

- `warn("@on")` / `warn("@off")`
- `warn("a", "b", "c")` 的拼接结果
- `print("head", 42)` 的基础输出
- `print(obj)` 对 `__tostring` 的复用

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `print`
- [x] 在 `_ENV` 中注册 `warn`
- [x] 为 `LuaState` 增加输出 sink
- [x] 为 `LuaState` 增加最小 warning 状态机
- [x] 新增对应运行时测试
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用 `print` / `warn`
- `print` 能与现有 `tostring` 路径协作
- `warn` 的控制消息和输出格式已经被测试钉住
- 真实 Lua 5.5 chunk 可以覆盖 `print` / `warn` 的关键路径

## 下一步

接下来继续往下补：

- `load` / `dofile`
- 字符串元表 `__index`
- 字符串算术元方法
- `collectgarbage` / `require` 的最小版本
