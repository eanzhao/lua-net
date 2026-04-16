# 051 Step 16: coroutine close continuation 与下一缺口收敛

## 本轮目标

上一轮把 `locals.lua` 推进到了 coroutine 相关缺口，但当时的症状还比较混杂：

- close 回调里的 `coroutine.yield` 会让函数返回值丢失
- 作用域 `CLOSE` 之后不会正确恢复后续执行
- `pcall` / `xpcall` 还把这类路径当成普通 C-call boundary

这一轮先不试图一次做完所有 coroutine 场景，而是把“普通 coroutine close continuation”这层打通，并把剩余问题收敛到更具体的 protected-call 恢复缺口。

## 主要改动

### 1. 给 frame 增加 pending close continuation

在 `CallFrame` / `LuaPendingClose` 上增加了最小的 close continuation 状态，用来记录：

- 还没执行完的 to-be-closed 寄存器
- 当前错误对象
- 当前 continuation 是“继续执行”还是“函数返回”
- return 路径下要保留的返回值
- native `coroutine.yield` 挂起后的 resume 标记

这样 close 回调在 coroutine 里挂起以后，VM 不会再把这次 close 当成已经完成。

### 2. `Return` / `Close` 路径支持 suspend-resume

VM 现在会在 coroutine 线程里，把下面两类 close 过程接成可恢复状态：

- 函数 `return` 触发的关闭
- 作用域 `CLOSE` 触发的关闭

恢复时会先继续 pending close，等 close 全部走完以后再：

- 继续执行后面的字节码
- 或者把原始返回值正确交还给调用方

这修掉了此前最明显的两个问题：

- close 回调里 yield 以后，函数返回值不再丢失
- `do ... end` 作用域里的 close yield 完成后，后续语句会继续执行

### 3. close 子调用使用专用 return target

close 回调如果是 Lua closure，VM 不再把它当成普通 host call，而是使用专门的 `CloseContinuation` 返回目标把控制权交回父 frame 的 pending close 状态。

这样 close callback 返回时，不会再走普通 host-call 返回路径，也不会把 close 过程和正常函数返回混在一起。

### 4. 非 coroutine close 继续沿用原有同步路径

主线程上的普通 close 语义仍然走原先的同步 `CloseResourcesFrom` 逻辑。

这一点很重要，因为像 `tbc inside close methods` 这类非 coroutine 路径，本来已经稳定；如果强行全量切到 continuation，会把已有行为带坏。

所以现在的策略是：

- coroutine 线程：启用新的 close continuation
- 非 coroutine 线程：保持旧的同步 close 语义

### 5. `pcall` / `xpcall` 不再把 close yield 直接判成 C-call boundary

`ExecuteNativeClosure` 里把 `pcall` / `xpcall` 从非 yieldable boundary 中排除了，`ExecuteProtectedCall` 也不再把同线程 `LuaYieldException` 直接包成失败结果。

这一步虽然还不足以让官方 coroutine close + `pcall` 全绿，但已经把现象从“立刻报 attempt to yield across a C-call boundary”推进到了更真实的下一缺口。

## 回归测试

本轮新增并固定了三组编译器集成测试：

- close 回调 yield 以后，函数返回值仍然完整返回
- 作用域 `CLOSE` yield 完成后，后续执行继续推进
- `tbc inside close methods` 中，最终错误会采用最新的 close 错误而不是旧错误

这些测试把本轮新增的 coroutine close continuation 和非 coroutine close 回归一起钉住了。

## 当前状态

`locals.lua` 现在已经通过了：

- 普通 `__close` / `error` / `traceback` 路径
- return hook 语义
- coroutine 中普通 return / scope close 的 suspend-resume

当前下一缺口已经进一步收敛到：

- `to-be-closed variables in coroutines`
- 更具体地说，是 `pcall` / `xpcall` 这类 protected call 在 child Lua 调用 yield 之后，仍然缺少可恢复的 host-call continuation

现在的最小复现不再是：

- `attempt to yield across a C-call boundary`

而已经变成更具体的：

- host-call 返回目标在 resume 之后无法继续对上，例如 `Unexpected host call id ...`

这说明下一轮应该直接集中到：

- yieldable protected call continuation
- host-style Lua 调用在 coroutine resume 之后的返回目标恢复

而不是再回头修改普通 close continuation。
