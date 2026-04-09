# 第 4 步补充：剩余加载路径

## 状态

已完成当前这一轮。

这一轮继续沿着 VM 基础指令往前补，把前面还空着的几条加载路径接上了。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lvm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心指令是：

- `LOADF`
- `LOADKX`
- `LFALSESKIP`
- `EXTRAARG`

这几条看起来都不大，但它们正好补齐了“常量加载”和“条件转布尔值”里剩下的缺口。

## 本步骤范围

本轮先落这几件事：

- 在 VM 中支持 `LOADF`
- 在 VM 中支持 `LOADKX`
- 在 VM 中支持 `LFALSESKIP`
- 用真实 Lua 5.5 chunk 验证 `LOADF` 和 `LFALSESKIP`
- 用手工 proto 验证 `LOADKX` 和 `EXTRAARG`

## 设计原则

### 1. 能用真实 chunk 的先用真实 chunk

`LOADF` 和 `LFALSESKIP` 都能很容易通过小脚本让官方编译器生成，所以这一轮直接加了真实 fixture。

这样做的好处很直接：

- 能验证 opcode 本身
- 也能验证编译器生成模式和我们理解的一致

### 2. `LOADKX` 先钉语义，不强塞超大 fixture

`LOADKX` 只有在常量索引超出 `Bx` 可表示范围时才会出现。

Lua 5.5 这里的 `Bx` 是 17 位，也就是：

- 最大常量索引是 `131071`

所以如果想用真实 chunk 固化 `LOADKX`，就得在仓库里放一个包含十几万常量的超大脚本和对应产物。这一轮先不这么做。

当前策略是：

- 用临时探针脚本确认官方 `luac` 的确会生成 `LOADKX`
- 在仓库测试里用手工 proto 把 `LOADKX + EXTRAARG` 的执行语义钉住

这样更轻，也更适合当前阶段。

### 3. 先补执行语义，不扩展额外优化

这一轮只补最小执行行为：

- `LOADF` 直接把 `sBx` 转成浮点值
- `LOADKX` 从后一条 `EXTRAARG` 取常量索引
- `LFALSESKIP` 写入 `false` 并跳过下一条指令

不顺手去扩展别的加载相关优化。

## 当前支持范围

这一轮新增支持：

- `LOADF`
- `LOADKX`
- `LFALSESKIP`
- `LOADKX` 对 `EXTRAARG` 的读取路径

当前还没有进入这些内容：

- `SETLIST` 对大索引 `EXTRAARG` 的完整路径已经拆到 `docs/016-step-04-setlist.md`
- `VARARG` / `RETURN` 的 open 结果路径
- 更完整的迭代器和循环指令

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/loadf_chunk.lua`
- `test/fixtures/lua55/chunks/loadf_chunk.luac`
- `test/fixtures/lua55/source/lfalseskip_chunk.lua`
- `test/fixtures/lua55/chunks/lfalseskip_chunk.luac`

它们分别覆盖：

- `LOADF`
- `LTI + JMP + LFALSESKIP + LOADTRUE`

`LOADKX` 这轮没有新增仓库内真实 fixture，而是用手工 proto 测试补上执行验证。

## 实现清单

- [x] 编写本轮文档
- [x] 支持 `LOADF`
- [x] 支持 `LOADKX`
- [x] 支持 `LFALSESKIP`
- [x] 新增真实 `loadf_chunk.luac`
- [x] 新增真实 `lfalseskip_chunk.luac`
- [x] 新增 `LOADKX` 手工 proto 测试

## 完成标准

本轮完成后，应满足：

- 真实 `LOADF` chunk 可以执行
- 真实 `LFALSESKIP` chunk 可以执行
- `LOADKX` 能正确读取后续 `EXTRAARG`
- 这几条路径不会影响现有 VM 行为

## 下一步

接下来继续往下补：

- `SETLIST` 的大索引路径已经拆到 `docs/016-step-04-setlist.md`
- `VARARG` / `RETURN` 的 open 结果路径
- 更完整的调用协议
- 更一般的元方法分发
