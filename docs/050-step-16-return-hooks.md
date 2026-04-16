# 050 Step 16: `locals.lua` return hook 兼容性推进

## 本轮目标

上一轮把 `locals.lua` 的缺口收敛到了：

- `debug.sethook` / `debug.gethook`
- return hook 的触发时机
- `__close` 回调在 hook 里的可见名字

这一轮的目标就是把 `locals.lua` 从 “`__close vs. return hooks in Lua functions` 失败” 推进到下一个真实缺口。

## 主要改动

### 1. 给线程补上 hook 配置状态

在 `LuaThread` 上增加了：

- 当前 hook function
- hook mask
- hook count
- hook 执行深度

这样 `debug.sethook` / `debug.gethook` 不再是空壳，而是有真实的线程级状态可以读写。

### 2. `debug.sethook` / `debug.gethook` 支持基础语义

`LuaState.Debug` 现在支持：

- `debug.sethook(hook, mask, count)`
- `debug.sethook()` / `debug.sethook(nil)` 清空 hook
- `debug.gethook()` 返回 `function, mask, count`
- 可选的首参 thread 解析

这一轮只实现了当前 `locals.lua` 需要的 return-hook 路径，没有提前把 line/count/call 全部做完。

### 3. Lua / native 函数返回时触发 return hook

VM 现在会在两类路径上触发 return hook：

- Lua bytecode frame 在 `TryCompleteFrame` 成功关闭资源之后、真正弹栈之前
- native closure 在执行结束、真正弹出 native frame 之前

这个顺序很关键，因为官方脚本依赖：

- `debug.sethook` 自己返回时先打到 `return sethook`
- `__close` 回调返回时再打到 `return close`
- 外层函数 `foo` 的 `return foo` 必须发生在所有 close 完成之后

### 4. hook 执行期间禁用递归 hook

为了避免 hook 回调自己再触发 hook，线程上增加了 hook 执行深度计数。hook 正在运行时，新的 return hook 会被抑制。

这让 `debug.getinfo(2)`、`debug.sethook(nil)` 这类 hook 内部调用不会把事件序列打乱。

### 5. 匿名 `__close` 在 hook 里统一显示为 `close`

源码里的匿名关闭回调原本会退化成 `function@line`。为了对齐 `locals.lua` 的断言，VM 在调用 `__close` 时会为匿名 closure 建一个仅运行时使用的 `close` 名字视图。

因此，hook 里通过 `debug.getinfo(2).name` 观察关闭回调时，现在能拿到 `close`，不再是 `function@14` 这类编译器生成名。

## 回归测试

本轮新增两组测试：

- 编译器集成测试：钉住 `return sethook -> return close -> x -> return close -> return foo` 的顺序
- Runtime 单元测试：钉住 `debug.sethook` / `debug.gethook` 的基础存取与清空

官方 compatibility 诊断也同步更新：`locals.lua` 已经不再停在 return hook，而是继续推进到了 coroutine 里的 to-be-closed/yield 组合语义。

## 当前状态

`locals.lua` 现在已经通过了：

- `__close vs. return hooks in Lua functions`
- `debug.sethook` / `debug.gethook` 的基础 return-hook 路径

当前下一缺口已经推进到：

- `to-be-closed variables in coroutines`
- 更具体地说，是 close 回调里 `coroutine.yield` 的暂停/恢复语义

这说明下一轮需要集中到 coroutine close continuation，而不是继续补普通 return hook。
