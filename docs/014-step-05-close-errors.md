# 第 5 步补充：`__close` 的错误传播与继续关闭

## 状态

已完成当前这一轮。

这一轮继续沿着 to-be-closed 生命周期往下补，把“关闭过程中抛错怎么办”这条线接上了。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lfunc.c`
- `references/lua-5.5.0/src/ldo.c`
- `references/lua-5.5.0/src/lvm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- 运行中的原始错误对象
- `__close(self, err)`
- 关闭方法再次抛错时的后续处理

官方实现的关键点不是“只要出错就立刻停”，而是：

- 把当前错误对象传给 `__close`
- 就算某个 `__close` 又抛错，后面的资源也还得继续关
- 最后把最新错误向外抛出

## 本步骤范围

本轮先落这几件事：

- 增加运行时异常对象，承载 Lua 层错误值
- 在 `_ENV` 里预置最小 `error`
- 让 VM 在关闭阶段记住当前错误对象
- 如果某个 `__close` 抛出新错误，继续关闭剩余资源
- 关闭结束后抛出最新错误
- 用真实 Lua 5.5 chunk 验证错误传递和关闭顺序

## 设计原则

### 1. 先把错误对象传对

这一轮最重要的是把 `err` 这个第二参数传对：

- 正常关闭时传 `nil`
- 运行过程先抛错时，把原始错误对象传进去
- 某个 `__close` 再抛新错时，后续 `__close` 看到的是新错误对象

这条链不通，后面的资源管理语义就会一直偏。

### 2. 先做“继续关闭”，再谈更细的状态码

Lua 官方内部有更细的状态码和恢复路径，但现在主线还没有把整套异常模型都搭出来。

所以这一轮先把用户最能感知的核心行为做对：

- 发生错误后不能漏关后续资源
- 最终抛出的错误要反映最新失败点

### 3. 先提供最小 `error`

真实 Lua fixture 想测这条语义，最直接的办法就是在脚本里主动调用 `error(...)`。

所以这一轮在 `_ENV` 中补了一个最小 `error` native closure，只支持第一参数作为错误对象，不扩展栈层级等附加行为。

## 当前支持范围

这一轮新增支持：

- `LuaRuntimeException`
- `_ENV.error`
- `__close(self, err)` 的错误对象传递
- `__close` 失败后继续关闭剩余资源
- 关闭结束后抛出最新错误

当前还没有进入这些内容：

- `error` 的层级参数
- 关闭过程中的完整状态码建模
- userdata 的 `__close`
- 更一般的元方法分发

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/tbc_error_chunk.lua`
- `test/fixtures/lua55/chunks/tbc_error_chunk.luac`

这份脚本覆盖的路径是：

- 运行过程先 `error("boom")`
- `b` 的 `__close` 先收到 `boom`
- `b` 的 `__close` 再 `error("close-b")`
- `a` 的 `__close` 仍然会继续执行，并收到更新后的错误对象 `close-b`

最终观察点有两个：

- 全局 `log` 应该是 `[b:boom][a:close-b]`
- 最终抛出的错误对象应该是 `close-b`

## 实现清单

- [x] 编写本轮文档
- [x] 增加 `LuaRuntimeException`
- [x] 在 `_ENV` 中注册最小 `error`
- [x] 在 VM 中保留并更新关闭阶段的错误对象
- [x] 在 `__close` 抛错后继续关闭剩余资源
- [x] 新增真实 `tbc_error_chunk.luac`
- [x] 新增对应运行时测试与 VM 测试

## 完成标准

本轮完成后，应满足：

- 运行过程中的错误对象可以传入 `__close`
- 关闭阶段的新错误会覆盖后续看到的错误对象
- 即使一个 `__close` 失败，剩余资源也会继续关闭
- 最终向外抛出的是最新错误
- 真实 Lua 5.5 fixture 可以验证这条完整路径

## 下一步

接下来继续往下补：

- 剩余加载路径已经拆到 `docs/015-step-04-load-opcodes.md`
- userdata 路径
- 更一般的元方法分发
- 更完整的调用协议
- `LOADF`、`LOADKX`、`EXTRAARG` 的剩余路径
