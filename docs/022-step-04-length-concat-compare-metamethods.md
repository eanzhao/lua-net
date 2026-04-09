# 第 4 步补充：长度、拼接与比较元方法分发

## 状态

已完成当前这一轮。

这一轮继续沿着元方法主线往前补，把 `LEN`、`CONCAT`、`EQ`、`LT`、`LE` 以及立即数比较这条线接到了 metatable 上。

这次主要打通的是：

- table metatable 上的 `__len`
- table metatable 上的 `__concat`
- table metatable 上的 `__eq`、`__lt`、`__le`
- `LTI` / `LEI` / `GTI` / `GEI` 的立即数比较元方法路径

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/ldebug.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaV_objlen`
- `luaV_concat`
- `luaV_equalobj`
- `luaV_lessthan`
- `luaV_lessequal`

## 本步骤范围

本轮先落这几件事：

- 在 `LEN` 中支持 table metatable 的 `__len`
- 在 `CONCAT` 中支持 table metatable 的 `__concat`
- 在 `EQ` 中支持 table metatable 的 `__eq`
- 在 `LT` / `LE` 中支持 table metatable 的 `__lt` / `__le`
- 在 `LTI` / `LEI` / `GTI` / `GEI` 中支持立即数比较元方法
- 用真实 Lua 5.5 chunk 验证上述路径

## 设计原则

### 1. 先保留快速路径，再补元方法兜底

这一轮没有把原有的数值和字符串快速路径推倒重写，而是保持顺序：

- 能走原始语义时，优先走原始语义
- 原始语义走不通时，再查 metatable

这样做的好处是：

- 行为更接近官方 VM
- 代码更容易逐步扩展
- 后续补 userdata 时也更自然

### 2. `CONCAT` 按右侧开始归并

Lua 5.5 的 `CONCAT` 在 VM 里是从右往左归并的。

这一轮也按这个方向处理：

- 先尝试右侧两个值
- 成功后把结果写回前一个位置
- 再继续向左归并

这样不仅字符串结果正确，元方法参与时的调用顺序也更接近官方。

### 3. 立即数比较也按官方分成原始路径和元方法路径

`LTI` / `LEI` / `GTI` / `GEI` 不是“只会比较数字”的专用指令。

如果原始数值比较走不通，官方 VM 仍然会继续尝试顺序比较元方法。这一轮也把这条路补上了。

## 当前支持范围

这一轮新增支持：

- `LEN` 的 table `__len`
- `CONCAT` 的 table `__concat`
- `EQ` 的 table `__eq`
- `LT` / `LE` 的 table `__lt` / `__le`
- `LTI` / `LEI` / `GTI` / `GEI` 的 table 元方法路径

当前这一轮还没有展开的是：

- userdata 的长度、拼接与比较元方法
- `EQI` 之外更多特殊比较细节
- 更完整的错误对象与错误消息对齐

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/meta_len_chunk.lua`
- `test/fixtures/lua55/chunks/meta_len_chunk.luac`
- `test/fixtures/lua55/source/meta_concat_chunk.lua`
- `test/fixtures/lua55/chunks/meta_concat_chunk.luac`
- `test/fixtures/lua55/source/meta_eq_chunk.lua`
- `test/fixtures/lua55/chunks/meta_eq_chunk.luac`
- `test/fixtures/lua55/source/meta_lt_chunk.lua`
- `test/fixtures/lua55/chunks/meta_lt_chunk.luac`
- `test/fixtures/lua55/source/meta_le_chunk.lua`
- `test/fixtures/lua55/chunks/meta_le_chunk.luac`
- `test/fixtures/lua55/source/meta_lti_chunk.lua`
- `test/fixtures/lua55/chunks/meta_lti_chunk.luac`
- `test/fixtures/lua55/source/meta_gti_chunk.lua`
- `test/fixtures/lua55/chunks/meta_gti_chunk.luac`
- `test/fixtures/lua55/source/meta_lei_chunk.lua`
- `test/fixtures/lua55/chunks/meta_lei_chunk.luac`
- `test/fixtures/lua55/source/meta_gei_chunk.lua`
- `test/fixtures/lua55/chunks/meta_gei_chunk.luac`

它们分别覆盖：

- `#x`
- `x .. y`
- `x == y`
- `x < y`
- `x <= y`
- `x < 5`
- `2 < x`
- `x <= 5`
- `5 <= x`

## 实现清单

- [x] 编写本轮文档
- [x] 在 `LEN` 中支持 `__len`
- [x] 在 `CONCAT` 中支持 `__concat`
- [x] 在 `EQ` 中支持 `__eq`
- [x] 在 `LT` / `LE` 中支持 `__lt` / `__le`
- [x] 在 `LTI` / `LEI` / `GTI` / `GEI` 中支持立即数比较元方法
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- table metatable 上的长度、拼接和比较元方法可以通过真实 chunk 被调用
- 立即数比较和寄存器比较都能在必要时落到元方法
- 现有数值和字符串快速路径不回退

## 下一步

接下来继续往下补：

- `UNM` / `BNOT` 的一元元方法
- userdata 路径
- 更一般的元方法调度
