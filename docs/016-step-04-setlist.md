# 第 4 步补充：`SETLIST` 与数组批量写入

## 状态

已完成当前这一轮。

这一轮继续沿着 VM 的剩余执行路径往前补，把表构造里还没接上的 `SETLIST` 补上了。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lvm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心指令是：

- `SETLIST`
- `EXTRAARG`

它对应的是 Lua 里最常见的一类代码：

- 数组式 table constructor

也就是：

```lua
local t = {1, 2, 3}
```

当元素很多时，编译器会按批次生成多条 `SETLIST`，最后一批还可能走 `k + EXTRAARG`。

## 本步骤范围

本轮先落这几件事：

- 在 VM 中支持固定元素个数的 `SETLIST`
- 在 VM 中支持 `SETLIST` 的 `k + EXTRAARG` 路径
- 用真实 Lua 5.5 chunk 验证小数组构造
- 用真实 Lua 5.5 chunk 验证大数组构造与最后一批 `EXTRAARG`

## 设计原则

### 1. 先把固定批量路径做稳

官方 `SETLIST` 里还有一个分支：

- `B == 0`

这意味着元素个数要从当前 `top` 推出来，也就是它依赖 open result / open argument 那套动态栈顶语义。

这条链还没做完，所以这一轮先只做：

- `B > 0`

先把最常见、最稳定的数组批量写入路径做扎实。

### 2. 先把 `k + EXTRAARG` 接上

`SETLIST` 的 `C` 只有 10 位。

当数组下标批次超过这个范围时，编译器会把高位拆到后面的 `EXTRAARG`，也就是：

- `SETLIST ... k`
- 下一条 `EXTRAARG`

如果这里只实现普通 `SETLIST`，一旦表字面量稍大，行为就会断。

所以这一轮把这个分支一起接上。

### 3. 用真实 chunk 验证编译器分批模式

`SETLIST` 最值得验证的不是单条语义，而是：

- 编译器如何分批
- 最后一批什么时候需要 `EXTRAARG`

所以这一轮用了两份真实 `luac` 产物：

- 小数组：验证普通 `SETLIST`
- 大数组：验证多批 `SETLIST`，以及最后一批的 `k + EXTRAARG`

## 当前支持范围

这一轮新增支持：

- 固定元素个数 `SETLIST`
- `SETLIST` 的 `k + EXTRAARG`
- 真实数组 table constructor 的批量数组写入

当前还没有进入这些内容：

- `SETLIST` 的 `B == 0` 路径
- `CALL` / `RETURN` / `VARARG` 的 open result / open argument 路径
- 迭代器与 vararg 的完整动态栈顶协议

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/setlist_chunk.lua`
- `test/fixtures/lua55/chunks/setlist_chunk.luac`
- `test/fixtures/lua55/source/setlist_extraarg_chunk.lua`
- `test/fixtures/lua55/chunks/setlist_extraarg_chunk.luac`

它们分别覆盖：

- 小数组构造的单条 `SETLIST`
- 大数组构造的多条 `SETLIST`
- 最后一批 `SETLIST ... k` 后接 `EXTRAARG`

大数组 fixture 最终验证的是：

- `t[1]`
- `t[1024]`
- `t[1050]`
- `t[1100]`

这样可以直接看出：

- 前面的批量写入没丢
- 超过 `vC` 范围后的高位偏移也没错

## 实现清单

- [x] 编写本轮文档
- [x] 支持固定元素个数 `SETLIST`
- [x] 支持 `SETLIST` 的 `k + EXTRAARG`
- [x] 新增真实 `setlist_chunk.luac`
- [x] 新增真实 `setlist_extraarg_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- 小数组 table constructor 可以执行
- 大数组 table constructor 可以执行
- `SETLIST` 能正确写入数组索引
- `SETLIST ... k` 能正确读取后续 `EXTRAARG`

## 下一步

接下来继续往下补：

- `VARARG`
- `CALL` / `RETURN` / `SETLIST` 的 open result / open argument 路径
- 更完整的调用协议
- 更一般的元方法分发
