# 第 7 步：基础库——load / loadfile / dofile

## 状态

已完成当前这一轮。

这一轮把 `load`、`loadfile`、`dofile` 的最小可执行路径补上了，但范围明确收在**二进制 chunk**。

当前项目还没有词法分析、语法分析和源码编译链路，所以这一轮不尝试伪实现文本源码加载；文本 chunk 会明确返回失败，而不是默默走非标准路径。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/lauxlib.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮重点对齐的点是：

- `luaB_load`
- `luaB_loadfile`
- `luaB_dofile`
- `load_aux`
- `generic_reader`
- `luaL_loadfilex`
- `luaL_loadbufferx`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `load`
- 在 `_ENV` 中预置 `loadfile`
- 在 `_ENV` 中预置 `dofile`
- 让 `load` 支持二进制字符串输入
- 让 `load` 支持 reader function 输入
- 让 `loadfile` 从文件系统读取 `.luac`
- 让 `dofile` 直接加载并执行 `.luac`
- 让 `load` / `loadfile` 支持 `env` 覆盖首个 upvalue
- 文本 chunk 先明确返回失败
- 用真实 Lua 5.5 chunk 验证这组函数的 Lua 层行为

## 设计原则

### 1. `Lua.Runtime` 不直接依赖 `Lua.Bytecode`

`load` 这组函数需要“把一段 chunk 变成可执行 closure”，但 `Lua.Runtime` 不应该反向依赖 `Lua.Bytecode` 或 VM。

所以这一轮在 `LuaState` 上增加的是**可注入的 binary chunk loader**：

- `LuaState` 负责参数语义、mode 检查、reader function、文件读取和错误整理
- `LuaVirtualMachine` 负责把字节数据接到 `LuaChunkReader + CreateClosure` 上

这样模块边界保持清楚：

- runtime 不知道字节码细节
- VM 继续作为真正的执行宿主

### 2. 先只支持 binary chunk，不假装支持 text chunk

官方 `load` / `loadfile` 既能吃文本源码，也能吃二进制 chunk。

但当前仓库还在 Step 07，源码编译链路还没做出来。这个时候如果偷偷接系统 Lua、临时 shell out，或者塞一个和后续编译器完全不同的“临时 parser”，都会让架构往错误方向漂。

所以这一轮的取舍是：

- 二进制 chunk：支持
- 文本 chunk：明确返回失败

这样语义边界是诚实的，也不会和后面的 Step 11-13 冲突。

### 3. `env` 直接绑定到根 closure 的第一个 upvalue

官方 `load_aux` 的行为是：

- 如果用户传了 `env`
- 就把它设置为加载结果 closure 的第一个 upvalue

这一轮按同样思路实现，但把动作落在 VM 的 root closure 构建阶段：

- 默认情况下，根 closure 的 `_ENV` 还是 `State.GlobalEnvironment`
- 如果 `load` / `loadfile` 提供了 `env`
- 那么根 closure 的 `_ENV` upvalue 会被替换为该值

这样 loaded chunk 里的全局访问会自然走到新环境里，嵌套 closure 也会沿着已有 upvalue 共享链路继承它。

### 4. `dofile` 是 `loadfile + execute`，错误直接抛出

`load` / `loadfile` 的失败路径是返回：

- `nil`
- error message

而 `dofile` 的语义不同：

- 加载失败直接报错
- 加载成功就立刻执行，并把执行结果原样返回

这一轮保持了同样的分工，所以运行时和 VM 的测试里会看到：

- `loadfile` 失败是二返回值
- `dofile` 失败是 `LuaRuntimeException`

## 当前支持范围

这一轮新增支持：

- `_ENV.load`
- `_ENV.loadfile`
- `_ENV.dofile`
- `load(binaryString, chunkname, "b", env)`
- `load(readerFunction, chunkname, "b", env)`
- `loadfile(path, "b", env)`
- `dofile(path)`
- binary chunk 的 mode 检查
- reader function 的多段拼接
- loaded chunk 的 `_ENV` 覆盖

这一轮明确**还不支持**：

- `load("return 1")` 这类文本 chunk
- `loadfile("script.lua")` 的源码编译路径
- `loadfile()` / `dofile()` 从标准输入读取

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/load_env_chunk.lua`
- `test/fixtures/lua55/chunks/load_env_chunk.luac`
- `test/fixtures/lua55/source/load_chunk.lua`
- `test/fixtures/lua55/chunks/load_chunk.luac`

它们覆盖：

- `load` 的二进制字符串路径
- `load` 的 reader function 路径
- `loadfile` 的文件路径加载
- `dofile` 的直接执行
- `env` 覆盖 `_ENV`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `load`
- [x] 在 `_ENV` 中注册 `loadfile`
- [x] 在 `_ENV` 中注册 `dofile`
- [x] 给 `LuaState` 增加可注入的 binary chunk loader
- [x] 给 `LuaState` 增加可注入的文件读取入口
- [x] 在 VM 中把 loader 接到 `LuaChunkReader`
- [x] 让 root closure 支持 `env` 覆盖
- [x] 新增运行时测试
- [x] 新增真实 fixture
- [x] 新增 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以调用 `load` / `loadfile` / `dofile`
- `.luac` 可以通过内存字符串、reader function、文件路径三种方式被加载
- `load` / `loadfile` 的 `env` 行为已有测试钉住
- `dofile` 能直接执行 loaded chunk 并返回结果
- 文本 chunk 的“不支持”路径是显式且可预测的

## 下一步

接下来继续往下补：

- `collectgarbage` 的最小版本
- `require` 的最小版本
- Step 11-13 完成后，再把 `load` / `loadfile` 的文本源码路径接上
