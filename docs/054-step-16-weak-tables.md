# 054 Step 16: 弱表语义收口与 `gengc.lua` 转绿

## 本轮目标

上一轮把 `collectgarbage` 的 mode / param API 补齐之后，`gengc.lua` 的最早真实缺口已经收敛到弱表。

但真正开始补弱表时，暴露出来的不只是 `LuaTable` 自身的问题，还有一层更隐蔽的来源：

- 表内部需要区分强 key / 弱 key、强 value / 弱 value
- `collectgarbage("step")` 的 debt 不能每次都被 full collect 清零
- 源码编译路径里，上一条语句留下的临时寄存器值会让“只剩弱引用”的对象假活着

这一轮的目标，就是把这些点一起收掉，让 `gengc.lua` 真正转绿。

## 本轮范围

本轮覆盖：

- `__mode = "k" / "v" / "kv"` 的弱表存储语义
- `collectgarbage("step")` 的累计 debt 语义
- 源码编译器对临时寄存器的运行时清理
- 按 Lua 可达性而不是纯宿主 GC 结果来清理弱表

明确不做：

- 完整的 ephemeron 语义
- table / userdata 的真正 `__gc` 终结器调度
- 官方 `gc.lua` 全量转绿

## 主要实现

### 1. `LuaTable` 改成 entry 级别的强弱引用存储

`LuaTable` 不再只是一层 `Dictionary<LuaValue, LuaValue>`。

现在每条 entry 都会显式记录：

- key 是强引用还是弱引用
- value 是强引用还是弱引用

这样同一张表就能正确表达：

- weak-key 表
- weak-value 表
- all-weak 表

同时，表会在访问和 GC 清理时剔除已经失效的弱 entry。

### 2. GC 清理改成“Lua 可达性 sweep + 宿主弱引用兜底”

如果只靠 .NET 的 `WeakReference`，`gengc.lua` 还是过不去。

根因是：源码 chunk 在执行时，上一条语句留下来的寄存器值、解释器调用栈上的宿主局部变量，都可能让对象在宿主看来还活着，但从 Lua 语义上它其实已经不可达。

这一轮改成了两层清理：

- 先从 `LuaState` 的根对象出发，按 Lua 语义遍历强引用图
- 对所有可达弱表执行 sweep：弱 key / 弱 value 如果不在这张可达图里，就从表里删掉
- 最后再用宿主 `WeakReference` 做一层兜底清理

这样弱表是否清掉对象，取决于 Lua 自己的可达性，而不是宿主 JIT 恰好把哪个局部留在栈上。

### 3. 编译器在语句边界显式清空临时寄存器

这一轮还补了一个源码编译器侧的关键细节。

之前 `ResetTemps()` 只是在“编译期”回收 temp register 计数，但运行时并不会把这些槽位写回 `nil`。

结果就是像下面这种代码：

```lua
local t = setmetatable({}, {__mode = "v"})
t[1] = {10}
collectgarbage()
```

虽然从 Lua 语义看 `{10}` 只剩弱引用了，但运行时寄存器槽位还残留着旧值，弱 value 就不会被清掉。

现在 `ResetTemps()` 会发出显式 `LOADNIL`，在下一条语句开始前把已经死亡的 temp register 清空。

### 4. `LuaPrototype` / `CallFrame` 增加 live register top 提示

为了让弱表 sweep 只把“当前真正活着的寄存器值”当成根，本轮还给源码编译结果增加了 `RegisterTopHints`。

VM 在执行源码编译出来的 prototype 时，会把当前 program counter 对应的 live register top 写回 frame。

弱表 sweep 读取这个 live top 后，就不会把整段 stack frame 的所有槽位都当成强根，从而避免把已经死亡的 temp 值错误保活。

### 5. `collectgarbage("step")` 改成真正累计 debt

之前 `step` 每次都会走一遍宿主 full collect，但同时也把 `_gcStepDebt` 清零了。

这会导致官方 `gc.lua` 里的：

```lua
repeat
until collectgarbage("step", siz)
```

对很多 `siz` 永远不会完成。

现在改成：

- `collect` 保持 reset debt
- `step` 触发宿主回收，但不重置 debt
- debt 达到 `stepsize` 时，当前 step 返回完成

这样 `gc.lua` 已经能越过最前面的 `steps` 段落。

## 回归与兼容性

### 单元/集成回归

本轮新增回归主要覆盖：

- weak-key / weak-value / 模式切换后的重建
- `collectgarbage("collect")` 会清掉弱表失效项
- `step` debt 会跨多次调用累计
- 源码路径里“上一条语句构造的弱 value”能在下一条 `collectgarbage()` 后被真正清掉

### 官方兼容性进展

这一轮把 `gengc.lua` 从诊断红测推进成了正式绿测。

当前兼容性状态变成：

- `bwcoercion.lua`：绿
- `bitwise.lua`：绿
- `locals.lua`：绿
- `gengc.lua`：绿

## 当前状态

GC 兼容线已经明显往前推进了一截：

- `collectgarbage` API：已补
- 弱表基础语义：已补到能跑通 `gengc.lua`
- `gc.lua`：已经不再卡在 `steps`

手工量化后的下一缺口，已经推进到 `gc.lua` 里 `weak tables` 后半段的 ephemeron / `GC()` 路径。

更具体地说，当前剩下的大块是：

- 真正的 table / userdata `__gc` 终结器调度
- ephemeron 语义
- `gc.lua` 后半段依赖完整 GC 周期的路径

所以下一轮的优先级应该直接转向：

- `__gc` 终结器
- ephemeron
- 然后再回头继续推进 `gc.lua`
