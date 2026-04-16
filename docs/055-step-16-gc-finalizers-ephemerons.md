# 055 Step 16: `__gc` 终结器、自动 GC 与 ephemeron 收口

## 本轮目标

上一轮把弱表基础语义补齐之后，`gengc.lua` 已经转绿，但 `gc.lua` 还卡在真正的 GC 周期语义上。

这条线的真实缺口不只一个点，而是几层连在一起：

- table / userdata 还没有真正的 `__gc` 终结器调度
- weak-key 表还没有 ephemeron 固定点传播
- 自动 GC 还不会在脚本执行过程中自己触发
- `collectgarbage("step")` 的完成判定太粗，和官方脚本的 `steps` 段对不上

这一轮的目标，就是把这几层一起补起来，把 GC 兼容线从“弱表基础可用”推进到“终结器 + ephemeron 主干可用”。

## 本轮范围

本轮覆盖：

- table / userdata 的 `__gc` 注册与一次性终结器调度
- 基于 Lua 可达性的多阶段 GC sweep
- weak value 先清、finalizer 再跑、weak key 最后清的时序
- weak-key 表的 ephemeron 传播
- 执行过程中的自动 GC 触发
- `collectgarbage("step")` 的 host collect 节奏修正

明确不做：

- 完整的 Lua 内存计量模型
- 官方 `gc.lua` 全量转绿
- `testC` 相关的 userdata C API 路径

## 主要实现

### 1. `LuaTable` / `LuaUserData` 增加终结器注册与对象注册表

为了让 `__gc` 真正可调度，table 和 userdata 现在都会：

- 进入对象注册表，供 GC 周期扫描
- 在 metatable 首次带上非 nil `__gc` 时，标记为“需要终结”
- 在终结器跑过一次之后，标记为“已终结”，避免重复执行

这里的关键点不是“当前 metatable 里有 `__gc`”，而是“这个对象是否已经进入 finalizable 集合”。

这样就能对齐 Lua 的两个基本约束：

- 对象是否需要终结，是在 metatable 赋值时决定的
- 真正执行哪个 `__gc`，则按回收当下 metatable 上的当前值再解析

### 2. `LuaState` 的 GC 改成多阶段 sweep

原来的实现本质上还是：

- 先跑宿主 `GC.Collect()`
- 再根据弱引用结果清表

这对弱表基础场景够用，但对 `gc.lua` 里的 bug 5.1、ephemeron、`__gc x weak tables` 都不够。

这一轮改成了更接近 Lua atomic phase 的顺序：

1. 从 `LuaState` 根集合做强引用遍历
2. 对可达 weak-key 表做 ephemeron 固定点传播
3. 先按当前可达图清 weak value
4. 找出当前不可达但需要终结的对象
5. 把这些对象重新标成可达，再跑一轮强引用 + ephemeron 传播
6. 最后再清 weak key，并保留“本轮正在终结”的 key
7. 按后进先出顺序调用 `__gc`

这几个顺序里最关键的是第 3 步和第 6 步：

- weak value 必须在 finalizer 之前清掉
- weak key 里的 finalizing object 必须多活一轮

也正是这两个点，让官方脚本里 `C.key == nil` / `type(next(C1)) == 'table'` 这一组断言能同时成立。

### 3. weak-key 表补上 ephemeron 固定点传播

之前 weak-key 表的行为还是“key 弱、value 强”，但没有做 Lua ephemeron 的固定点传播。

结果就是这种链：

```lua
a[n] = {k = {x}}
x = n
```

在 key 通过 value 间接重新可达时，GC 不会继续传播，整条链会提前断掉。

现在 `LuaTable` 增加了专门的 ephemeron 传播入口：

- 只对 weak-key / strong-value 表生效
- 当 key 已在当前可达图里时，才把 value 继续入队
- 反复迭代直到没有新增对象

这样 `gc.lua` 里的两段 ephemeron 链都已经能跑通。

### 4. VM 执行循环接入自动 GC

官方 `GC1()` / `GC2()` 依赖的不是显式 `collectgarbage()`，而是“对象不断创建时，GC 会自己推进”。

这一轮在 VM 主循环里增加了最小自动 GC 触发：

- 只在 GC 运行状态下生效
- 按指令债务阈值定期触发
- 自动触发只跑 Lua 语义 GC 周期，不强制每次都做 host full collect

这足够让如下模式真正停下来：

```lua
repeat u = {} until finish
```

也就是说，`__gc` 不再只能靠显式 `collectgarbage()` 才能被看见。

### 5. `collectgarbage("step")` 改成“步进 + 达标后 full collect”

上一轮虽然已经把 debt 累计补上了，但 `step` 每次都直接做 full collect，导致大步长会太早返回“本轮 cycle 已完成”。

这一轮把它拆成了两层：

- 每次 `step` 先跑一轮 Lua 语义步进
- 只有累计 debt 达到 `stepsize` 时，才做一次 host full collect 并返回完成

这样官方脚本前面的 `steps` 段已经能继续往后走，不会再在大步长参数上过早结束。

## 回归与兼容性

### 新增回归

本轮新增 VM 回归主要覆盖：

- 自动 GC 会在执行过程中触发表终结器
- weak value 会在 finalizer 之前被清掉
- weak-key ephemeron 链会完整传播并在根断开后收掉

这些回归都已经进 `Lua.VM.Tests`。

### 官方兼容性推进

现有正式 compatibility 绿测仍然是：

- `bwcoercion.lua`
- `bitwise.lua`
- `locals.lua`
- `gengc.lua`

手工推进 `gc.lua` 时，这一轮已经能越过：

- `steps`
- `weak tables`
- ephemeron
- `__gc x weak tables`

当前新的最早缺口已经收敛到：

- `collectgarbage("count")` 对长字符串场景的宿主内存统计口径

更具体地说，Lua 语义上的对象已经被收掉，但仓库现在的 `count` 仍然直接映射宿主 CLR heap 视角，和官方 Lua 的内存模型还不是一回事。

## 当前状态

GC 主干现在已经比上一轮完整很多：

- 弱表基础语义：已补
- `__gc` 终结器调度：已补
- ephemeron：已补
- 自动 GC：已补
- `gengc.lua`：已绿

Step 16 在 GC 这条线上，当前剩下的最明显缺口已经不再是“不会回收”，而是“内存计量口径还不对”。

所以下一轮如果继续推 `gc.lua`，优先级就会变成：

- `collectgarbage("count")` 的 Lua 侧内存统计模型
- 然后再把 `gc.lua` 接成正式 compatibility 绿测
