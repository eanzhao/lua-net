# 第 5 步补充：`__close` 与 to-be-closed 生命周期第一版

## 状态

已完成当前这一轮。

这一轮把 `TBC` 从“只支持 `nil/false` 快速路径”继续往前推进，接上了真正可执行的第一版资源关闭链路。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lfunc.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/lapi.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `OP_TBC`
- `OP_CLOSE`
- `__close`
- `setmetatable`

目标不是一次把完整资源释放语义做满，而是先把最小可跑通的真实路径接起来。

## 本步骤范围

本轮先落这几件事：

- 为运行时补上最小 native closure 承载结构
- 在 `_ENV` 中预置最小 `setmetatable`
- 为 `LuaTable` 补上 metatable 挂载与 `__close` 查找
- 在 `CallFrame` 中记录 to-be-closed 寄存器
- 在 `CLOSE` 和函数退出时按逆序调用 `__close`
- 用真实 Lua 5.5 chunk 验证块关闭和函数退出两条路径

## 设计原则

### 1. 只做真实闭环，不做泛化包装

这一轮不打算先搭一整套“标准库框架”或者“通用元方法系统”，而是只补当前闭环真正依赖的最小能力：

- native closure
- `setmetatable`
- table metatable
- `__close`

先把真实 chunk 跑起来，再继续往更一般的元方法调度扩展。

### 2. 先把关闭时机钉住

这一轮最重要的不是“找得到 `__close`”，而是关闭时机必须正确：

- 块结束时的 `CLOSE`
- 函数返回前的剩余资源关闭
- 同一作用域里多个 to-be-closed 变量按逆序关闭

只有这三件事都对，后面往完整错误传播扩展才有意义。

### 3. 先支持表路径

Lua 的 `__close` 不只可能落在表上，但当前真实 fixture 走的是：

- `setmetatable(table, mt)`
- `mt.__close`

所以这一轮先把表路径做扎实，其他对象类型后续再补。

## 当前支持范围

这一轮新增支持：

- `LuaNativeClosureBody`
- `_ENV.setmetatable`
- `LuaTable.Metatable`
- `LuaTable.TryGetMetamethod`
- `TBC` 的真实登记路径
- `CLOSE` 与函数退出时的 to-be-closed 关闭
- `__close(self, nil)` 的最小调用路径

当前还没有进入这些内容：

- `__close` 的完整错误对象传播
- 关闭过程中继续关闭后续资源的完整错误恢复策略
- userdata 的 `__close`
- 更一般的元方法分发

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/tbc_close_chunk.lua`
- `test/fixtures/lua55/chunks/tbc_close_chunk.luac`

这份脚本同时覆盖了几条关键路径：

- `setmetatable` 返回的表被声明为 `<close>`
- 块作用域里的 `<close>` 局部变量在 `CLOSE` 时触发 `__close`
- 函数作用域内剩余的 `<close>` 局部变量在返回时触发 `__close`
- 多个 to-be-closed 变量按逆序执行关闭

最终期望结果是：

- 返回值仍然是 `42`
- 关闭日志是 `xba`

这说明：

- 块里的 `x` 先关
- 函数退出时 `b` 后于 `a` 注册，所以先关 `b`
- 最后再关 `a`

## 实现清单

- [x] 编写本轮文档
- [x] 为运行时补上 native closure 承载
- [x] 在 `_ENV` 里注册最小 `setmetatable`
- [x] 为 `LuaTable` 补上 metatable
- [x] 为 `CallFrame` 补上 to-be-closed 寄存器登记
- [x] 在 VM 中支持 `__close` 调用
- [x] 新增真实 `tbc_close_chunk.luac`
- [x] 新增对应运行时测试与 VM 测试

## 完成标准

本轮完成后，应满足：

- `<close>` 表值不再只停留在快速路径
- `TBC` 会登记真正需要关闭的值
- `CLOSE` 可以触发 `__close`
- 函数返回时会关闭剩余 to-be-closed 值
- 真实 Lua 5.5 fixture 可以验证关闭顺序

## 下一步

接下来继续往下补：

- `__close` 的错误传播与继续关闭已经拆到 `docs/014-step-05-close-errors.md`
- userdata 路径
- 更完整的元方法分发
- 更完整的调用协议
- `LOADF`、`LOADKX`、`EXTRAARG` 的剩余路径
