# 056 Step 16: `collectgarbage("count")` 对齐与 `gc.lua` 转绿

## 本轮目标

上一轮把 `__gc`、自动 GC 和 ephemeron 主干补上之后，`gc.lua` 已经只剩两个收尾问题：

- `collectgarbage("count")` 仍然直接映射宿主 CLR heap，长字符串场景会被 host 保活噪声污染
- `__gc` 内部再次调用 `collectgarbage()` 时，返回值还没有按 Lua 的“不可重入”语义对齐

这一轮的目标，就是把这两个收尾点补平，并把官方 `gc.lua` 正式接成绿测。

## 本轮范围

本轮覆盖：

- `collectgarbage("count")` 的 Lua 侧内存估算
- `__gc` 期间 `collectgarbage()` / `step` 的不可重入返回语义
- 自动 GC 的触发条件收紧，避免普通大分配循环被无意义的全量 sweep 拖慢
- 官方 `gc.lua` compatibility 绿测接入

明确不做：

- 完整复刻 Lua 内部字节级 allocator 统计
- `testC` / C API 相关的 userdata GC 路径

## 主要实现

### 1. `count` 不再读取宿主总内存

之前 `collectgarbage("count")` 直接返回 `GC.GetTotalMemory()`。

这在宿主层面看起来简单，但和 Lua 语义差了两层：

- CLR 可能继续保留逻辑上已经死亡的对象或字符串
- CLR 统计的是整个进程/托管堆视角，不是当前 Lua state 的对象图

这会直接卡死官方 `gc.lua` 里的长字符串断言：

```lua
assert(collectgarbage("count") <= m + 1)
```

因为 Lua 侧已经把对象收掉了，但宿主堆上的长字符串未必立刻消失。

现在 `count` 改成 Lua 侧估算：

- table / userdata / closure / thread 通过注册表快照累计近似尺寸
- string 不再看宿主总内存，而是扫描当前 Lua 对象图和活跃栈槽去重后累计

这样 `count` 反映的是“当前 Lua 还持有多少东西”，而不是宿主 GC 恰好何时回收。

### 2. 自动 GC 只在真的有 GC 工作时才自推进

上一轮自动 GC 是“按固定指令债务周期触发”。

这对 `GC1()` / `GC2()` 足够，但在 `gc.lua` 的 `long list` 段里会退化成高频全量 sweep，纯粹拖慢执行。

这一轮把触发条件收紧成：

- GC 处于运行状态
- 当前不在 GC 自身内部
- 至少存在待终结对象

这样：

- 依赖自动 `__gc` 的路径仍然能推进
- 单纯的大量普通 table 分配不会被无意义的自动 sweep 拖住

`gc.lua` 的 `long list` / `self-referenced threads` 段因此能顺利跑完。

### 3. `__gc` 内部的 `collectgarbage()` 改成返回假值

官方脚本最后有一个很小但很关键的协议检查：

```lua
setmetatable({}, {__gc = function ()
  res = collectgarbage()
end})
collectgarbage()
assert(not res)
```

Lua 的语义不是“抛错”，而是“GC 不可重入，因此返回假值”。

仓库之前虽然已经避免了真正重入，但 base API 仍然把 `collectgarbage()` 当成正常调用返回 `0`，而在 Lua 里 `0` 是 truthy，断言自然失败。

这一轮把这层也对齐了：

- 如果当前正在 GC 内部，`collectgarbage()` 返回 `false`
- `collectgarbage("step")` 同样返回 `false`

这样 reentrant GC 协议就和官方脚本一致了。

## 回归与兼容性

### 新增正式 compatibility 绿测

这一轮把 `gc.lua` 正式接进了 `Lua.Compatibility.Tests`。

当前官方脚本绿测变成：

- `bwcoercion.lua`
- `bitwise.lua`
- `locals.lua`
- `gengc.lua`
- `gc.lua`

### 关键手工回归

除了正式绿测外，这一轮还额外验证了：

- 长字符串 weak-table 清理后，`count` 能回到基线附近
- `collectgarbage("stop")` 期间，反复分配 table 会让 `count` 持续增长
- `__gc` 内部再调 `collectgarbage()` 会返回假值而不是 `0`

## 当前状态

GC 兼容线现在已经从“主干可用”推进到了“官方脚本可跑”：

- `gengc.lua`：绿
- `gc.lua`：绿

这意味着 Step 16 里最重的一条 GC 兼容性主线，已经基本收口。

下一步如果继续沿官方测试集往前推，优先级就可以从 GC 转向别的剩余脚本，例如：

- `api.lua`
- `attrib.lua`
- 以及其他还没有接入 compatibility harness 的官方脚本
