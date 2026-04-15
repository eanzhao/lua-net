# 第 6 步：二元算术与位运算元方法分发

## 状态

已完成当前这一轮。

这一轮继续沿着 VM 主线往前补，把 `MMBIN`、`MMBINI`、`MMBINK` 这组三元方法分发指令接上了。

这次主要打通的是：

- 二元算术快速路径失败后的元方法分发
- 立即数参与运算时的元方法分发
- 常量 `K` 参与运算时的元方法分发
- 常量在左侧、寄存器在右侧时的 flipped 参数路径

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lcode.c`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/ltm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心指令是：

- `MMBIN`
- `MMBINI`
- `MMBINK`

## 本步骤范围

本轮先落这几件事：

- 在二元算术和位运算快速路径失败时，不再直接抛出未实现异常
- 让 VM 在遇到 `MMBIN` / `MMBINI` / `MMBINK` 时，按官方 5.5 的套路继续查找元方法
- 先支持 table metatable 上的二元元方法查找
- 用真实 Lua 5.5 chunk 验证寄存器、立即数、翻转立即数、K 常量四条路径

## 设计原则

### 1. 快速路径成功就跳过，失败就交给后续 `MMBIN*`

Lua 5.5 的这组指令不是独立运算，而是紧跟在二元算术和位运算后面：

- 快速路径成功
  就跳过下一条 `MMBIN*`
- 快速路径失败
  就继续执行下一条 `MMBIN*`

这一轮沿用了这个结构，而不是把“快速路径”和“元方法分发”硬塞进同一条分支里。

### 2. 结果寄存器仍然以原始算术指令为准

`MMBIN*` 自己不携带结果寄存器，它真正要写回的位置来自前一条算术或位运算指令。

所以这一轮在执行 `MMBIN*` 时，会反向读前一条指令，再把元方法返回值写回原始目标寄存器。

### 3. 先把 table metatable 这条真实常见路径做稳

这一轮没有试图一次把所有对象种类和所有元方法做满，而是先补最常见也最容易用真实 chunk 验证的路线：

- `setmetatable(table, mt)`
- `mt.__add`
- `MMBIN` / `MMBINI` / `MMBINK`

userdata 和更一般的对象元方法分发，留到后续阶段继续扩展。

## 当前支持范围

这一轮新增支持：

- `MMBIN`
- `MMBINI`
- `MMBINK`
- `__add` / `__sub` / `__mul` / `__mod` / `__pow` / `__div` / `__idiv`
- `__band` / `__bor` / `__bxor` / `__shl` / `__shr`

当前这一轮还没有展开的是：

- `UNM` / `BNOT` 对应的一元元方法
- `LEN` / `CONCAT` / 比较运算的元方法
- userdata 和更一般对象种类的元方法查找

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/meta_add_chunk.lua`
- `test/fixtures/lua55/chunks/meta_add_chunk.luac`
- `test/fixtures/lua55/source/meta_addi_chunk.lua`
- `test/fixtures/lua55/chunks/meta_addi_chunk.luac`
- `test/fixtures/lua55/source/meta_flip_chunk.lua`
- `test/fixtures/lua55/chunks/meta_flip_chunk.luac`
- `test/fixtures/lua55/source/meta_addk_chunk.lua`
- `test/fixtures/lua55/chunks/meta_addk_chunk.luac`

它们分别覆盖：

- `MMBIN` 的寄存器对寄存器路径
- `MMBINI` 的普通立即数路径
- `MMBINI` 的 flipped 参数路径
- `MMBINK` 的常量 `K` 路径

## 实现清单

- [x] 编写本轮文档
- [x] 在二元算术快速路径失败时保留 `MMBIN*` 分发机会
- [x] 支持 `MMBIN`
- [x] 支持 `MMBINI`
- [x] 支持 `MMBINK`
- [x] 新增真实 `meta_add_chunk.luac`
- [x] 新增真实 `meta_addi_chunk.luac`
- [x] 新增真实 `meta_flip_chunk.luac`
- [x] 新增真实 `meta_addk_chunk.luac`
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- table metatable 上的二元元方法可以通过真实 chunk 被调用
- `MMBIN` / `MMBINI` / `MMBINK` 三条路径都能跑通
- flipped 参数顺序和原始运算顺序一致

## 下一步

接下来继续往下补：

- 更一般的元方法分发
- `LEN` / `CONCAT` / 比较运算的元方法
- userdata 路径
