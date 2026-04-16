# Step 15：CLI、脚本执行与 REPL

## 状态

Step 15 这一轮把剩余的“可执行入口”部分补齐了：

- `Lua.Cli` 控制台项目
- `lua-net script.lua [args...]` 脚本执行入口
- 交互式 REPL
- 基础命令行参数处理

到这一轮为止，Step 15 里原本拆开的两条主线已经都落地：

- `docs/042-step-15-bytecode-dump.md`：字节码序列化
- 本文：CLI / REPL / 脚本执行

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lua.c`
- `references/lua-5.5.0/doc/manual.html`

重点对齐的点是：

- “无参数进交互模式”的启动体验
- `lua script.lua arg1 arg2` 这类脚本执行形态
- `arg` 全局表
- `-e` / `-i` / `-v` / `--help` 这类基础命令行选项

## 本步骤范围

这一轮落地这些能力：

- 新增 `src/Lua.Cli`
- 新增 `LuaCliArgumentParser`
- 新增 `LuaCliApplication`
- 新增 `LuaReplSession`
- 支持：
  - 无参数进入 REPL
  - `lua-net script.lua [args...]`
  - `-e <chunk>`
  - `-i`
  - `-v`
  - `-h` / `--help`
  - `--` 停止参数解析
- 在 CLI 启动时注入全局 `arg`
- REPL 支持 `=expr` 速记和基础多行续输

这一轮明确**不做**：

- 完整对齐官方 `lua` 的所有命令行选项
- 负索引 `arg` 表（保存脚本名前的选项）
- stdin 脚本加载
- 复杂 traceback / 调试器级交互
- `luac` 风格独立编译工具

## 设计

### 1. 单独引入 `Lua.Cli`，不把宿主逻辑塞回 VM

roadmap 里一开始就把目标结构写成了：

- `Lua.Runtime`
- `Lua.Bytecode`
- `Lua.Syntax`
- `Lua.Compiler`
- `Lua.VM`
- `Lua.Cli`

这一轮按这个方向正式把 `Lua.Cli` 建出来。

原因很直接：

- REPL、参数解析、帮助文本、控制台 I/O 都是宿主层逻辑
- 这些逻辑不应该污染 `LuaState` 或 `LuaVirtualMachine`
- 以后如果要接 GUI、LSP、测试驱动宿主，也能直接复用 VM 而不是依赖控制台入口

所以这一轮的边界是：

- `Lua.VM` 负责执行
- `Lua.Cli` 负责“怎么把命令行和控制台接到 VM 上”

### 2. REPL、`-e` 和脚本模式共用同一个 VM

CLI 启动后只创建一个 `LuaVirtualMachine`，然后把所有入口都串到这同一个实例上：

- `-e` 执行的 chunk
- 脚本文件
- 后续 `-i` 进入的 REPL

这样有两个好处：

- 全局环境会自然共享，跟官方宿主的直觉一致
- 先执行 `-e "x = 41"`，再跑脚本或进 REPL，可以直接读到 `x`

这比给每条入口单独建 VM 更接近真实 Lua 的使用方式。

### 3. 脚本执行复用现有 `loadfile` 路径

脚本入口没有重新发明一套“文件读入 + 判断 text/binary + 编译/加载”的逻辑，而是直接复用已有能力：

- `loadfile(path, "bt")`
- 失败时返回 `nil, error`
- 成功后拿到 closure，再由 VM 调用

这样脚本模式天然覆盖：

- 文本 chunk
- binary chunk

同时也避免 CLI 和运行时各自维护两套加载语义。

### 4. `arg` 先做简化版，优先覆盖常见脚本需求

这一轮 CLI 在启动时会注入一个全局 `arg` 表：

- `arg[0]`：脚本路径；若没有脚本则为 `lua-net`
- `arg[1...]`：脚本参数

这已经能覆盖大部分常见脚本需求，例如：

- 读取脚本文件名
- 读取位置参数

官方独立解释器还会在 `arg` 里保留脚本名前的选项，并用负索引编码。
这一轮没有跟到那一步，因为它不会影响核心“脚本可执行”能力，只会让参数协议更细。

### 5. REPL 采用“尝试编译，不完整则续输”的最小模型

REPL 每读入一行，就尝试把当前缓冲区当作一个 chunk 编译执行：

- 编译成功：执行并清空缓冲
- 命中“不完整输入”特征：继续进入续输提示符
- 真正错误：输出错误并清空缓冲

同时支持 `=expr` 速记：

- 输入 `=1 + 1`
- 实际执行 `return 1 + 1`

这一轮的不完整输入判断采用的是最小启发式，不是完整 parser 状态机。
目标是先把最常见的多行场景接通，例如：

- `function ... end`
- `if ... then ... end`
- 表 / 括号 / 字符串尚未闭合

## 当前支持范围

这一轮新增支持：

- `dotnet run --project src/Lua.Cli`
- `dotnet run --project src/Lua.Cli -- script.lua a b`
- `dotnet run --project src/Lua.Cli -- -e "print(1)"`
- `dotnet run --project src/Lua.Cli -- -i`
- REPL `=expr`
- REPL 基础多行提交

这一轮仍然缺失：

- stdin 脚本模式
- 更完整的 `arg` 负索引兼容
- 更强的 REPL 不完整输入判断
- 文本主 chunk 对 `...` 的脚本参数访问

最后一点是当前编译器能力边界导致的：

- CLI 已经把脚本参数传给了根 closure
- 但 Step 13 的文本编译器还不支持 vararg 主 chunk
- 所以文本脚本当前应通过全局 `arg` 取参数，而不是 `...`

## 测试

这一轮新增 `Lua.Cli.Tests`，覆盖：

- 参数解析
- `-e` / `-i` 组合
- 脚本执行与 `arg` 注入
- REPL `=expr`
- REPL 多行续输

## 实现清单

- [x] 新增 `Lua.Cli` 控制台项目
- [x] 实现基础参数解析
- [x] 实现脚本执行入口
- [x] 注入全局 `arg`
- [x] 实现交互式 REPL
- [x] 支持 `=expr`
- [x] 支持基础多行续输
- [x] 新增 CLI 测试
- [x] 更新 README / 文档索引

## 完成标准

本轮完成后，应满足：

- 仓库可以直接启动一个 REPL
- 可以执行 `script.lua`
- 可以把额外命令行参数通过 `arg` 传给脚本
- 可以用 `-e` 执行一段 Lua 代码
- CLI 路径由自动化测试钉住

## Step 15 收口结论

到这里，Step 15 规划的四项内容都已经有对应实现：

- `string.dump()`
- 交互式 REPL
- 脚本执行入口
- 命令行参数处理

因此 Step 15 可以标记为**已完成**。

## 延期内容

后续如果继续增强宿主体验，可以考虑：

- 更完整的官方 `lua` 参数兼容
- `stdin` chunk 执行
- 更强的 REPL 续输判断和 traceback 展示
- `luac` 风格预编译工具
