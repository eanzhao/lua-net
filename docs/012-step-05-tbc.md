# 第 5 步补充：TBC 的最小快速路径

## 状态

已完成当前这一轮。

这一轮继续沿着上值生命周期往前推，但范围刻意收得很小，只先把 `TBC` 在 `nil` 和 `false` 上的快速路径接起来。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lfunc.c`
- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lobject.h`

关键点是：

- `OP_TBC`
- `l_isfalse(o)` 等价于 `false` 或 `nil`

在官方实现里，这两种值都不需要真正进入 to-be-closed 列表。

## 本步骤范围

本轮先落这几件事：

- 在 VM 中支持 `TBC`
- 让 `nil` 和 `false` 直接走无副作用快速路径
- 对其他需要 `__close` 的值明确报“暂未实现”
- 用真实 Lua 5.5 chunk 验证这条最小路径

## 设计原则

### 1. 先把正确的无副作用路径接上

`TBC` 完整语义不只是“打个标记”，它最终会牵涉到：

- `__close`
- 关闭顺序
- 错误传播
- to-be-closed 链表

这些内容现在都还不轻。

所以这一轮先把官方已经定义好的“无需关闭”分支接通，保证：

- `<close> = nil`
- `<close> = false`

这两种合法代码可以直接执行。

### 2. 暂不伪造 `__close` 语义

如果值真的需要关闭方法，而我们还没有元方法和 native 调用那套能力，这一轮就明确报未实现，不用半套逻辑冒充完整支持。

## 当前支持范围

这一轮新增支持：

- `TBC`
- `nil` 的快速路径
- `false` 的快速路径

当前还没有进入这些内容：

- `__close` 方法查找
- to-be-closed 链表
- 关闭方法调用
- 错误传播与嵌套关闭顺序

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/tbc_nil_chunk.lua`
- `test/fixtures/lua55/chunks/tbc_nil_chunk.luac`
- `test/fixtures/lua55/source/tbc_false_chunk.lua`
- `test/fixtures/lua55/chunks/tbc_false_chunk.luac`

这两份脚本都很小，但正好能验证：

- 编译器确实会生成 `TBC`
- 对 `nil` / `false` 执行时不应产生额外副作用

## 实现清单

- [x] 编写本轮文档
- [x] 支持 `TBC`
- [x] 支持 `nil` / `false` 的快速路径
- [x] 对其他 to-be-closed 值明确报未实现
- [x] 新增真实 `tbc_nil_chunk.luac`
- [x] 新增真实 `tbc_false_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- 含有 `local x <close> = nil` 的真实 chunk 可以执行
- 含有 `local x <close> = false` 的真实 chunk 可以执行
- VM 对未实现的 `__close` 路径不会假装成功

## 下一步

接下来继续往下补：

- `__close` 与 to-be-closed 生命周期第一版已经拆到 `docs/013-step-05-close-metamethod.md`
- 更完整的错误传播与嵌套关闭语义
- 更完整的调用协议
- `LOADF`、`LOADKX`、`EXTRAARG` 的剩余路径
