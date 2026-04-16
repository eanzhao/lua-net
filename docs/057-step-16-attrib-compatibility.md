# 057 Step 16: `attrib.lua` 兼容性收口

## 本轮目标

在 `gc.lua` 转绿之后，Step 16 的下一条兼容性主线切到了官方 `attrib.lua`。

这份脚本覆盖的不是单一子系统，而是一组“真实脚本运行时会同时依赖”的边角协议：

- 相对路径文件访问是否以脚本工作目录为基准
- `io` 写出的文本文件是否带 BOM
- 主 chunk 是否像官方 Lua 一样天然支持 vararg
- `package.loadlib` 的返回协议是否和 Lua 5.5 对齐
- 大整数与精确可表示浮点之间的相等性、表索引归一化是否一致

这一轮的目标，就是把这些散落在 runtime / compiler / compatibility harness 里的协议补齐，并把官方 `attrib.lua` 正式推成绿测。

## 本轮范围

本轮覆盖：

- `loadfile` / `dofile` / `io.*` / `os.rename` / `os.remove` / `package` 的相对路径解析
- 文本文件读写统一改成无 BOM UTF-8
- 主 chunk vararg 语义
- `package.loadlib` 缺库时的错误种类字符串
- `nil` 表读、large integer/float 数值比较与表键归一化
- `attrib.lua` compatibility 绿测接入

明确不做：

- 真正加载宿主动态库来跑官方 `lib2-v2`
- 处理 `attrib.lua` 之外的新官方脚本缺口

## 主要实现

### 1. 相对路径统一锚定到 `LuaState.WorkingDirectory`

官方 `attrib.lua` 会直接在脚本目录下读写文件，并通过 `package` 搜索相对路径模块。

仓库之前更多是按宿主当前目录去解释这些路径，导致同一份脚本在测试进程、CLI 进程和 compatibility harness 里行为不稳定。为了解掉这个问题，这一轮在 `LuaState` 上显式引入了：

- `WorkingDirectory`
- `ResolveFilePath`
- `TryReadFileBytes`

然后把这些入口统一接到同一套解析规则：

- `loadfile` / `dofile`
- `io.open`、`file:lines`、`file:write`
- `os.remove` / `os.rename`
- `package.searchpath`
- `package.loadlib`

这样脚本看到的相对路径语义，就从“取决于宿主测试进程在哪里启动”收敛成了“始终相对当前 Lua state 的工作目录”。

### 2. 文本 IO 改成无 BOM UTF-8

`attrib.lua` 会直接检查生成文件的字节内容。仓库之前用默认 `StreamWriter`，会写出 UTF-8 BOM，这和官方 Lua 的文本文件输出不一致。

这一轮在 `LuaState.Io` 里统一改成显式的无 BOM UTF-8 编码：

- `StreamReader`
- `StreamWriter`
- 标准输入输出包装

这样通过 Lua 写出来的文件，首字节不会再多出 BOM，也就不会被官方断言误判。

### 3. 主 chunk 对齐官方 Lua 的 vararg 语义

Lua 的主 chunk 本身就是 vararg 函数体，因此顶层脚本可以直接读取 `...`。

源码编译路径之前把主 chunk 当成普通非 vararg 函数编译，结果官方脚本里依赖顶层参数传递的分支会直接跑偏。这一轮把 `CompileChunk` 生成的主原型 flag 改成带 `VarArgFunctionFlag`，让：

- CLI 脚本参数
- `loadfile(...)`
- 顶层 `...`

统一回到和官方一致的语义。

### 4. `package.loadlib` 协议和 `attrib.lua` 对齐

`attrib.lua` 会检查 `package.loadlib` 在找不到动态库时返回的错误分类。

仓库之前返回的是 `"open"`，但官方 Lua 5.5 这一路径上期待的是 `"absent"`。这一轮把 runtime 的错误种类改成了 `"absent"`，同时让注册过的 native library 路径在 raw path 和 resolved path 两边都能命中。

compatibility harness 这边，官方脚本依赖的 `lib2-v2` 没有必要真的走宿主动态库加载，因此改成在启动 `attrib.lua` 前通过 CLI `-e` 预先注册：

```lua
package.preload["lib2-v2"] = function (...)
  return {
    id = function (...) return true end,
    newstr = function (s) return s end,
  }
end
```

这样可以把测试焦点留在 Lua 协议本身，而不是宿主平台差异。

### 5. 补平 large integer/float 边界比较

`locals.lua` 和 `attrib.lua` 都会打到“大整数整数值”和“精确可表示的 float 值”之间的交界。

这轮把数值辅助逻辑集中收紧到了 `LuaValueHelper`：

- 浮点转整数改成基于 IEEE 754 位模式的精确判定
- mixed integer/float 比较不再依赖 `decimal` 转换
- `2^50` 这类精确可表示浮点，现在能和整数 `1125899906842624` 正确比较为相等
- 表键归一化同步复用这套整数判定，保证 float key 和 integer key 命中同一槽位

顺手还补了两个相关协议：

- 表对 `nil` 读取返回 `nil`，而不是抛宿主异常
- `AreRawEqual` / VM 比较统一走同一套数值比较 helper

## 回归与兼容性

### 新增正式 compatibility 绿测

这一轮把 `attrib.lua` 正式接进了 `Lua.Compatibility.Tests`。

当前官方脚本绿测变成：

- `bwcoercion.lua`
- `bitwise.lua`
- `locals.lua`
- `gengc.lua`
- `gc.lua`
- `attrib.lua`

### 关键回归

除了 compatibility 绿测，这一轮还补了这些直接回归：

- 相对路径 `loadfile` 基于 `WorkingDirectory`
- `io` / `os` 相对路径 API 基于 `WorkingDirectory`
- 文本输出不写 BOM
- large exactly-representable float key 会归一化到整数键
- `2^50` 和整数 `1125899906842624` 的 `==` / `<=` / `>=` 一致成立

## 当前状态

Step 16 现在已经从“GC 兼容线收口”推进到了“更多官方脚本持续转绿”的阶段。

当前兼容性正式绿测共有 6 个官方脚本，说明：

- 工作目录和文件系统协议已经足够支撑真实脚本
- 主 chunk 参数传递语义已经和官方 Lua 对齐
- 大整数/浮点交界这类容易漏掉的数值细节已经进入回归覆盖

下一步可以继续沿官方测试集往前推，优先级建议转到：

- `api.lua`
- 以及其他还未接入 compatibility harness 的官方脚本
