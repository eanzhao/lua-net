# Step 14：package 系统（第一轮）

## 状态

Step 14 已启动，这一轮先把 `package` 子系统从 Step 07 的“最小可用版”推进到“可扩展、可测试的一阶完整形态”。

这次不试图一次做完 `io` / `os` / `debug`，而是先把最容易影响真实模块加载体验的部分补齐：

- `package.config`
- `package.searchpath`
- `package.loadlib`
- `package.cpath`
- 默认四段 `package.searchers`
- C module / all-in-one loader 的 Lua 层语义

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/loadlib.c`
- `references/lua-5.5.0/doc/manual.html`

重点对齐的点是：

- `ll_searchpath`
- `ll_loadlib`
- `searcher_Lua`
- `searcher_C`
- `searcher_Croot`
- `ll_require`

## 本步骤范围

这一轮落地这些能力：

- 在 `package` 表里补上 `config` / `cpath` / `searchpath` / `loadlib`
- 把默认 `package.searchers` 扩成 4 个：
  - preload
  - Lua loader
  - C loader
  - C root loader
- 让 Lua searcher 不再只认 `.luac`，而是走 `load` 现有的 text/binary 双路径
- 补齐 `package.searchpath(name, path [, sep [, rep]])`
- 补齐 `package.loadlib(libname, funcname)` 的 Lua 层返回协议
- 支持 `require` 通过 `package.cpath` 加载宿主注册的 native module
- 支持 all-in-one C loader（例如 `require("a.b")` 回退到根库 `a`）

这一轮明确**不做**：

- `io` / `os` / `debug` 库
- 真实 Lua C API ABI 兼容
- 任意系统动态库的直接调用
- 环境变量驱动的默认 `path` / `cpath` 初始化

## 设计

### 1. `package` 语义尽量对齐官方，动态库接入改成宿主注册模型

官方 Lua 的 `package.loadlib` 直接面向宿主动态链接器和 `lua_CFunction` ABI。

这个项目当前是纯 C# 运行时，没有嵌入官方 Lua VM，也没有 `lua_State*` / `lua_CFunction` 那套 ABI，所以不能把任意 `.so` / `.dll` 直接当成 Lua C module 调用。

因此这一轮采用宿主注册模型：

- `LuaState.RegisterNativeLibrary(path)`
- `LuaState.RegisterNativeLibraryFunction(path, functionName, LuaNativeFunction)`
- `LuaState.RegisterNativeLibraryClosure(path, functionName, LuaClosure)`

这样做的目标不是“伪造真实 C ABI”，而是先把 **Lua 层可见的 `package` 行为** 钉住：

- `searchpath` 的路径展开
- `loadlib` 的返回值 / 错误三元组
- `require` 的 searcher 链、loader data 与缓存
- C loader / C root loader 的命名规则

后续如果要接真正的宿主扩展机制，可以沿这层抽象继续演进，而不需要推翻 Lua 侧语义。

### 2. Lua searcher 统一走 text/binary 双模加载

Step 07 的文件 searcher 只覆盖 `.luac`，因为当时文本 chunk 还没打通。

现在 Step 13 已经把文本编译链路接进 VM，所以这一轮把 Lua searcher 升级成：

- 先用 `package.searchpath` 解析 `package.path`
- 找到文件后走现有 `LoadChunk(..., mode: "bt")`

这样 `require("mod")` 可以同时覆盖：

- 纯文本模块
- 预编译二进制 chunk

而且 `load` / `loadfile` / `require` 共用同一条加载总线，不会再有两套分叉逻辑。

### 3. C loader 和 C root loader 对齐 `luaopen_` 命名规则

官方 `loadlib.c` 对 C module loader 名称有两条关键规则：

- 点号 `.` 先替换成下划线 `_`
- 如果模块名里有 `-`，优先忽略第一个 `-` 及其后的后缀

因此这一轮实现也按同样策略生成 open function 名称：

- `native.mod` → `luaopen_native_mod`
- `a.b-c` → 先试 `luaopen_a_b`，再回退老式名字

同时保留 C root loader 语义：

- `require("root.nested")`
- 先找 `root` 对应库文件
- 再在该库中查 `luaopen_root_nested`

## 当前支持范围

这一轮新增支持：

- `package.config`
- `package.path`
- `package.cpath`
- `package.searchers` 四个默认 searcher
- `package.searchpath`
- `package.loadlib`
- `require` 的 Lua text/binary 模块路径
- `require` 的 C module 与 all-in-one root module 路径

这一轮仍然缺失：

- 真实系统动态库装载
- 环境变量初始化 `LUA_PATH` / `LUA_CPATH`
- `io` / `os` / `debug`

## 测试

这一轮新增或更新了这些测试：

- `LuaStateTests`
  - 扩展 `package` 预置断言
  - `package.searchpath` 成功/失败路径
  - `package.loadlib` 三元组返回
  - `require` 通过 C searcher 与 C root searcher 加载宿主模块
- `LuaCompilerTests`
  - 文本模块 + native 模块 + `package.loadlib/searchpath` 的源码级集成测试

## 实现清单

- [x] 新增 Step 14 文档
- [x] 扩展 `package` 表字段与默认 searcher 链
- [x] 实现 `package.searchpath`
- [x] 实现 `package.loadlib`
- [x] 实现宿主 native library 注册接口
- [x] 实现 C searcher / C root searcher
- [x] 让 Lua searcher 走 text/binary 双模加载
- [x] 更新 README / roadmap
- [x] 新增运行时与集成测试

## 完成标准

本轮完成后，应满足：

- `package.searchpath` / `package.loadlib` 在 Lua 层可直接使用
- `require` 可以通过 `package.path` 加载文本模块
- `require` 可以通过 `package.cpath` 加载宿主注册的 native module
- `require("a.b")` 可以走 all-in-one C root loader
- 这些路径的成功/失败语义都有测试钉住

## 下一步

Step 14 接下来继续补：

- `io` 库与文件句柄对象
- `os` 库的宿主交互接口
- `debug` 库需要的调用栈 / 本地变量 / 上值观测能力
- 如果有必要，再评估更贴近真实 Lua C API 的宿主扩展模型
