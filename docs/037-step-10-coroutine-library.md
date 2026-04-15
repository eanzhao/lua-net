# Step 10：`coroutine` 库

## 目标

这一轮补上 Lua 5.5 的 `coroutine` 主路径：

- `coroutine.create`
- `coroutine.resume`
- `coroutine.yield`
- `coroutine.wrap`
- `coroutine.status`
- `coroutine.isyieldable`
- `coroutine.close`
- `coroutine.running`

同时把 VM 的调用模型从“C# 递归调用”改成“显式 Lua 调用栈驱动”，否则 `yield` 时无法保留中间 Lua 帧，也无法在 `resume` 时从原位置继续执行。

## 问题拆解

在 Step 09 结束时，运行时里虽然已经有 `thread` 值类型，但它只是一个占位对象：

- 没有独立的栈
- 没有独立的调用帧列表
- 没有挂起态 / 死亡态 / resume parent 等协程状态
- VM 的 `CALL` / `TAILCALL` 依赖 C# 递归返回结果

这种实现方式下，一旦 `coroutine.yield` 抛出异常回到 `resume`，中间所有 C# 调用帧都已经消失，Lua 层的待返回调用点也就跟着丢了。

## 设计

### 线程上下文下沉到 `LuaThread`

把协程自己的执行状态挂到 `LuaThread` 上：

- `LuaStack`
- 当前活动的 `CallFrame` 列表
- entry closure
- `HasStarted` / `IsDead` / `IsYieldSuspended`
- `ResumeParent` / `IsResumingChild`
- `ErrorObject`
- resume 时要回填给 `yield` 调用点的参数

`LuaState` 只保留全局共享部分（全局环境、标准库、类型元表、package 表等），并通过 `CurrentThread` 暴露当前正在运行的线程上下文。

### open upvalue 不再依赖当前 state 栈

原先 `LuaUpvalue` 的 open 态通过：

- `CallFrame`
- `registerIndex`
- `state.Stack`

来间接定位值槽。

这在单线程模型里没问题，但协程切换后，`CurrentThread.Stack` 会变化，open upvalue 不能再依赖“当前线程栈”。

因此改成直接保存：

- `LuaStack`
- 绝对栈索引

这样 open upvalue 永远绑定到它真实所属的栈。

### VM 改成显式调用栈驱动

这一轮最核心的改动是 VM 的调用路径：

- bytecode `CALL` 遇到 Lua closure 时不再递归进入新的 `ExecuteClosure`
- 而是直接压入新的 `CallFrame`
- 由统一的解释器循环继续驱动当前顶层 frame

对应地，需要为 frame 增加两类元数据：

- `ReturnTarget`
  - host call
  - caller register 写回
  - coroutine thread root
- `PendingCall`
  - 普通寄存器写回
  - tail-return 挂起点

这样当 `coroutine.yield` 发生时：

1. 当前 Lua frame 保留下来
2. pending call 保留下来
3. `resume` 把参数写回原调用点
4. 解释器从原 program counter 继续执行

### `coroutine.yield` 与 `resume`

实现上使用专门的 `LuaYieldException` 作为控制流信号：

- `coroutine.yield` 在可 yield 的线程里抛出 `LuaYieldException`
- `coroutine.resume` 捕获它并返回 `true, ...yieldValues`
- 下一次 `resume` 时，把传入参数回填到挂起的调用点

这条路径现在已经覆盖：

- 普通 `CALL`
- `TAILCALL`
- `coroutine.wrap`
- thread root 返回

### `coroutine.close`

`close` 需要和 Step 05 的 `__close` 行为接上。

实现策略：

- 对 suspended coroutine：主动清空它的活动 frame 栈
- 在清理过程中复用现有 `CloseResourcesFrom`
- 让 to-be-closed 变量按既有 `__close` 语义执行
- 无错误时返回 `true`
- 原协程错误或关闭阶段报错时返回 `false, err`

对于 running coroutine（非主线程）：

- `coroutine.close()` 不在当前协程里返回
- 直接结束当前协程
- 外层 `resume` 收到 `true`

这和 Lua 5.5 手册行为保持一致。

## 当前范围与取舍

这一步已经打通标准主路径，但仍保留一个明确边界：

- 通过当前“同步 native 调用边界”进入的 Lua 调用，暂时仍视为 non-yieldable

这意味着：

- 纯 Lua 调用链里的 `yield/resume` 已经可用
- `wrap` / `close` / `running` / `status` 主路径可用
- 更细粒度的“跨特定 native helper 继续恢复”还没有做 continuation 级别建模

对当前 Step 10 目标来说，这个边界是可接受的，后续如果要做更完整的 Lua C continuation 语义，再单独展开。

## 测试

这一轮新增了两层测试：

- runtime 测试：确认 `coroutine` 表和 8 个入口函数已注册
- VM fixture 测试：新增真实 `coroutine_chunk.luac`

fixture 覆盖了：

- main thread 的 `running` / `isyieldable`
- `create` + `resume` + `yield`
- `status` 在 `running` / `suspended` / `normal` / `dead` 的主路径
- `wrap`
- yielded coroutine 的 `close`
- dead coroutine 的 `close`
- errored coroutine 的 `close`
- running self close
- `__close` 与 `coroutine.close` 的联动

## 完成标准

本轮完成后，应满足：

- 一个真实 Lua 5.5 chunk 可以通过 `coroutine` 库完成挂起与恢复
- `yield` 不会破坏中间 Lua 调用帧
- `wrap` / `close` / `status` / `running` 主路径可用
- `coroutine.close` 能触发 to-be-closed 资源清理

## 下一步

Step 10 到这里收口。

接下来进入：

- Step 11：词法分析
