# 052 Step 16: yieldable protected call 与 error-unwind close 收口

## 本轮目标

上一轮把 coroutine close continuation 做到了普通 `return` / `CLOSE` 路径，但 `locals.lua` 还卡在：

- `pcall` / `xpcall` 里的 Lua 子调用一旦 `yield`，resume 之后会丢失 protected-call 上下文
- 函数体报错时，error unwind 里的 `__close` 还是走同步弹栈清理，close callback 里 `yield` 之后无法继续

这一轮的目标就是把这两条线接通，让官方 `locals.lua` 整体转绿。

## 主要改动

### 1. 给 `pcall` / `xpcall` 增加可恢复的 pending protected-call 状态

在 `CallFrame` 上新增 `LuaPendingProtectedCall`，记录：

- 当前 phase 是目标函数还是 message handler
- 挂起中的 host-call id
- direct native `yield` 的 resume 等待状态
- 已完成但还没向外层交付的 protected-call 结果

这样 `pcall` / `xpcall` 不再只是一次同步 helper 调用，而是可以在 coroutine 里跨 `yield` 保存和恢复状态。

### 2. `pcall` / `xpcall` 的 resume 现在由解释器接管

`ExecuteProtectedCall` 在首次 `yield` 时不再丢上下文；恢复时由 VM 在主循环里继续推进：

- child Lua 调用返回后，把结果重新包成 `true, ...`
- child Lua 调用报错后，继续走 `false, err` 或 `xpcall` 的 message handler
- native `yield` 和 bytecode `yield` 两条路径都统一回到 pending protected-call 状态

这修掉了此前 `Unexpected host call id ...` 这一类 resume 之后的错位问题。

### 3. error unwind 里的 `__close` 也改成 continuation

此前函数体报错时，`ExecuteClosure` 会直接走 `AbortFramesToDepth -> CleanupFramesToDepth -> CloseResourcesFrom`。

这条路径会先弹掉当前 frame，再同步调用 `__close`，所以 close callback 一旦 `yield`，后续恢复就没有原 frame 可接。

现在 VM 会在 interpreter 内部把这类场景改成 `LuaPendingCloseContinuationKind.Error`：

- 出错 frame 先保留在栈上
- 关闭过程按 pending close continuation 继续推进
- 全部 close 完成后再弹 frame 并重新抛出错误

这样函数体报错时的 `__close` callback 也能像 return / scope close 一样 suspend-resume。

### 4. coroutine close error 的选择规则按官方行为收紧

这一轮还顺手把 close error 的覆盖规则收紧到了更接近官方 `locals.lua` 的行为：

- 同一个 resume 片段里，后面的 close 错误仍然可以覆盖前面的 close 错误
- 但一旦 close continuation 已经带着 close 错误 `yield` 过，后续 resume 片段里的 close 错误不再覆盖它

这正好覆盖了官方脚本里两类看起来相反、但实际都存在的场景：

- 普通 `errors in __close`：后一个 close 错误继续覆盖
- coroutine `to-be-closed variables in coroutines`：跨 resume 片段后保留第一次 close 错误

## 回归测试

本轮新增并固定了两类回归：

- 编译器集成测试：`pcall` 遇到 yieldable `__close` 后，success path 仍能按 `x -> y -> z -> true, 10, 20` 恢复
- 官方兼容性测试：`locals.lua` 从诊断红测转成正式绿测

其中 `locals.lua` 现在已经完整输出 `OK`，说明本轮处理的不只是最小复现，而是整段官方 coroutine / `__close` / `pcall` 交互语义都已经打通。

## 当前状态

Step 16 现在又收掉了一块高耦合缺口：

- `locals.lua` 已绿
- `bwcoercion.lua` 已绿
- `bitwise.lua` 已绿

剩下的 Step 16 工作就不再是 `locals.lua` 这条线，而是继续推进路线图里还没接入或还没量化的兼容性条目。
