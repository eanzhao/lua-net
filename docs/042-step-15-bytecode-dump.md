# Step 15：字节码序列化（第一轮）

## 状态

Step 15 已启动。

这一轮先不碰 REPL、命令行入口和脚本执行器，而是把 Step 15 里最核心、也最适合独立闭环落地的一块先做完：

- 二进制 chunk 写出器
- `string.dump`
- `strip` 调试信息裁剪
- 从 Lua 层走通 `dump -> load` 的回环

这样可以先把 Step 03 的“读 chunk”补成一对对称能力，再为后面的 REPL、脚本缓存和离线编译工具留出稳定底座。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/ldump.c`
- `references/lua-5.5.0/src/lstrlib.c`
- `references/lua-5.5.0/doc/manual.html`

重点对齐的点是：

- chunk header 与 prototype 序列化顺序
- MSB varint / Lua integer 编码
- `string.dump(function [, strip])` 的 Lua 层语义
- `strip = true` 时 `source` / line info / local vars / upvalue names 的裁剪规则

## 本步骤范围

这一轮落地这些能力：

- 在 `Lua.Bytecode` 中新增 `LuaChunkWriter`
- 支持把 `LuaChunk` / `LuaPrototype` 写回 Lua 5.5 binary chunk
- 在 `LuaState` 中新增字节码导出委托，由 VM 注入具体实现
- 在 `string` 库中新增 `string.dump`
- 支持 `string.dump(func, true)` 输出剥离 debug info 的 chunk
- 用测试钉住 writer roundtrip 和 Lua 层 `dump -> load` 路径

这一轮明确**不做**：

- REPL
- `lua script.lua`
- 命令行参数处理
- 独立 `luac` 风格工具
- 跨版本 / 跨端序 / 跨字长兼容导出

## 设计

### 1. `Lua.Runtime` 只暴露导出入口，不直接依赖 VM / Bytecode 细节

`string.dump` 是标准库函数，定义在 `Lua.Runtime`。

但真正能否导出一个 closure，取决于它是不是 bytecode closure，以及它背后挂的 `LuaPrototype` 是什么。这些知识都属于 `Lua.VM` / `Lua.Bytecode`，不应该反向塞进 runtime 层。

因此这里沿用 `load` / `loadfile` 已有的注入模式：

- `LuaState.SetBytecodeChunkDumper(...)`
- `LuaState.TryDumpBytecodeChunk(...)`

这样 `Lua.Runtime` 只负责 Lua 层参数检查和返回值拼装：

- 非函数或 native closure：报错
- bytecode closure：交给 VM 导出

模块边界保持清楚：

- `Lua.Runtime` 负责 API surface
- `Lua.VM` 负责从 closure 提取 prototype
- `Lua.Bytecode` 负责实际二进制写出

### 2. `LuaChunkWriter` 与现有 `LuaChunkReader` 按同一格式对称实现

writer 这一轮只解决“把当前内存模型稳定写回去”，不顺手引入额外格式改造。

具体策略：

- header 继续写 Lua 5.5 的固定签名和数值标记
- 整数继续使用和 reader 对称的 MSB varint
- `lua_Integer` 继续使用与 `ldump.c` 一致的正负交错编码
- `code` / `abslineinfo` 继续按 4 字节对齐
- string reuse 索引这轮不做压缩优化，统一直接写字面量字符串

最后一点是有意为之。

官方 dumper 会跟踪已保存字符串并复用索引，但 loader 接受“始终写字面量字符串”的编码，因此这一轮先优先保证实现简单、稳定、可验证，再考虑空间优化。

### 3. `strip` 只裁剪 debug 信息，不改函数主体语义

`strip = true` 时，本轮 writer 按官方规则裁剪这些字段：

- `source`
- `lineinfo`
- `abslineinfo`
- `locvars`
- upvalue names

不裁剪这些字段：

- 指令流
- 常量表
- upvalue 元数据（`instack` / `idx` / `kind`）
- 嵌套 prototype 结构

这保证 stripped chunk 仍然可执行，只是失去调试辅助信息。

### 4. `string.dump` 只接受 Lua bytecode closure

Lua 手册要求 `string.dump` 接受 Lua function，而不是任意可调用对象或 C/native function。

因此这一轮明确采用下面的行为：

- bytecode closure：返回 binary chunk 字符串
- native closure：报 `Lua function expected`
- 非函数值：同样报 `Lua function expected`

这也和当前系统的能力边界一致，因为只有 bytecode closure 才有稳定的 `LuaPrototype` 可导出。

## 当前支持范围

这一轮新增支持：

- `LuaChunkWriter.Write(chunk, stripDebugInformation: false)`
- `LuaChunkWriter.Write(chunk, stripDebugInformation: true)`
- `string.dump(function [, strip])`
- `load(string.dump(f), ..., "b", env)` 回读执行

这一轮仍然缺失：

- REPL 交互输入循环
- CLI / script runner
- chunk 写出时的字符串去重压缩
- 更丰富的导出验证工具（例如类 `luac -l` 对拍）

## 测试

这一轮新增测试覆盖：

- `LuaChunkReaderTests`
  - 真实 fixture roundtrip：`read -> write -> read`
  - `strip = true` 后 debug info 被清空，但 chunk 结构仍可读
- `LuaVirtualMachineTests`
  - `string.dump -> load` 的 Lua 层闭环
  - `string.dump(..., true)` 的 strip 行为
  - native closure 被 `string.dump` 拒绝

## 实现清单

- [x] 新增 Step 15 文档
- [x] 新增 `LuaChunkWriter`
- [x] 在 `LuaState` 中补字节码导出注入点
- [x] 在 VM 中把 bytecode closure 接到 writer
- [x] 新增 `string.dump`
- [x] 支持 `strip` debug info 裁剪
- [x] 新增 roundtrip / integration 测试
- [x] 更新 README 状态与文档索引

## 完成标准

本轮完成后，应满足：

- 任意当前 VM 可执行的 bytecode closure 都可以通过 `string.dump` 导出
- 导出的 chunk 可以被现有 `load(..., "b")` 路径重新加载
- `strip = true` 会稳定移除调试信息，而不会破坏可执行性
- writer 与 reader 的基础 roundtrip 由测试钉住

## 延期内容

Step 15 后续继续补：

- REPL
- 脚本执行入口
- CLI 参数解析
- 可能的预编译 chunk 文件输出工具
- 如果后续需要，再评估 writer 的字符串去重和更严格的官方字节级对拍
