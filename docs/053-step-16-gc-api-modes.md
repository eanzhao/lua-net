# 053 Step 16: `collectgarbage` 模式与参数 API 对齐

## 本轮目标

`locals.lua` 转绿之后，Step 16 剩下的大块兼容性工作开始转到 GC 相关。

官方 `gc.lua` / `gengc.lua` 最先撞上的不是弱表或 `__gc` 本身，而是更前面的 API 层：

- `collectgarbage("incremental")`
- `collectgarbage("generational")`
- `collectgarbage("param", ...)`

这些在仓库里原本都还没有实现，所以官方脚本一进入 GC 测试就会直接报 `invalid option`。

这一轮先把 GC 模式切换和参数读写协议补齐，把缺口从“API 不认识”推进到“真实 GC 语义还没做完”。

## 本轮范围

只覆盖 `collectgarbage` 的宿主 API 协议：

- `incremental` / `generational` 模式切换
- `param` 参数读写：`pause` / `stepmul` / `stepsize`
- `step` 的最小模拟语义

明确不做：

- 真正的增量 GC 状态机
- 分代 GC barrier / age / color 语义
- 弱表清理
- `__gc` 终结器调度

## 主要实现

### 1. LuaState 内部增加 GC 模式与参数状态

在 `LuaState` 上增加了最小的 GC 配置状态：

- 当前模式：`incremental` / `generational`
- `pause`
- `stepmul`
- `stepsize`
- 手动 `step` 的宿主侧累计进度

这让 `collectgarbage` 不再只是几个硬编码分支，而是有了可查询、可切换、可恢复的内部状态。

### 2. `collectgarbage("incremental")` / `"generational"` 返回上一模式

现在这两个选项会按 Lua 5.5 习惯：

- 切换当前模式
- 返回切换前的模式字符串

这样官方脚本里这类断言已经能通过：

- 连续切到 `generational` 时返回前一模式
- 再切回 `incremental` 时返回 `generational`

### 3. `collectgarbage("param", ...)` 支持读写 pause / stepmul / stepsize

`param` 现在支持：

- `collectgarbage("param", "pause")`
- `collectgarbage("param", "pause", value)`
- `stepmul`
- `stepsize`

行为对齐为：

- 读：返回当前值
- 写：更新参数并返回旧值

这足够让官方脚本前面的参数扫描和恢复逻辑先跑起来。

### 4. `step` 提供最小可推进的宿主模拟

仓库还没有真正的 Lua GC 状态机，所以 `collectgarbage("step")` 现在仍然不是官方实现。

但为了让脚本不再卡死在最前面的 API 检查，本轮给它加了一个最小的宿主模拟：

- 每次 `step` 仍然会触发宿主 `GC.Collect()`
- 通过内部 step debt 模拟“一个 collection cycle 是否完成”
- `stepsize == 0` 时，`step` 直接返回完成

这不是最终语义，但足够把“完全不认识 step API”推进到“真正的 GC 行为还不完整”。

## 回归与兼容性

### 运行时测试

运行时测试补到了：

- mode 切换返回前一模式
- `param` 的读写返回值
- `stepsize = 0` 时 `step` 立即完成

### 官方兼容性量化

本轮没有把 `gc.lua` / `gengc.lua` 转绿，但把最早缺口明显往后推进了：

- 之前：一进脚本就报 `invalid option 'incremental'` / `'generational'`
- 现在：`gengc.lua` 已经进入真正的 generational 断言，当前下一缺口收敛到弱表语义

所以兼容性测试新增了一个诊断红测，用来固定这个新位置，避免后续回退。

## 当前状态

GC 这条线现在的分层已经比较清楚：

- API 协议：已补到可用
- 弱表：未实现，是 `gengc.lua` 当前最早真实缺口
- `__gc` 终结器：仍未实现，`gc.lua` 还没法整体转绿

下一轮应该直接进：

- 弱表（`__mode = "k" / "v" / "kv"`）
- 然后再看 `__gc` 终结器和真正的 GC 周期语义
