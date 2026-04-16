# Step 16：源码 `for` 编译与 `bitwise.lua` 兼容推进

## 状态

这一轮把 Step 16 里两条卡点一起往前推了：

- 源码编译器现在支持数值 `for` 和泛型 `for`
- 官方 `bitwise.lua` 从“编译不过”推进到“整段跑通”

中间还顺手修掉了三个被官方脚本逼出来的真实语义问题：

- `>>` 右移必须是逻辑右移，不是算术右移
- 文本编译路径里的二元算术/位运算，在没有 `MMBIN*` 跟随指令时也要能回退到元方法
- `0xffffffffffffffff.0` 这类超范围十六进制字符串不能被错误地当成 `-1`

这一轮结束后，compatibility harness 的状态变成：

- `bwcoercion.lua`：通过
- `bitwise.lua`：通过
- `locals.lua`：失败，当前阻塞点是 local variable attributes

## 背景与参考

这一轮主要参考：

- `test/fixtures/lua55/source/for_integer_chunk.lua`
- `test/fixtures/lua55/source/for_float_chunk.lua`
- `test/fixtures/lua55/source/for_generic_chunk.lua`
- `test/fixtures/lua55/chunks/for_integer_chunk.luac`
- `test/fixtures/lua55/chunks/for_generic_chunk.luac`
- `test/fixtures/lua55/official/lua-5.5.0-tests/bitwise.lua`
- `test/fixtures/lua55/official/lua-5.5.0-tests/locals.lua`
- `references/lua-5.5.0/src/lparser.c`
- `references/lua-5.5.0/src/lvm.c`

这轮最大的价值不是“补了一个 `for` 语法”，而是把源码编译路径和官方脚本真实行为重新拉近了：

- `bitwise.lua` 一开始卡在 vararg
- 补完 vararg 后卡在 `for`
- 补完 `for` 后继续暴露出右移语义、元方法回退、超范围十六进制字符串
- 修完这些以后，`bitwise.lua` 才真正转绿

这正是 Step 16 想要的工作方式：不是凭感觉补 feature，而是让官方测试把真实缺口一层层顶出来。

## 本步骤范围

这一轮落地这些内容：

- 数值 `for` 的源码编译
- 泛型 `for` 的源码编译
- `break` 在泛型 `for` 中跳到 `CLOSE`
- 逻辑右移语义修正
- 文本编译路径二元算术/位运算的元方法回退补齐
- 十六进制 `.0` 字符串的超范围解析修正
- `bitwise.lua` 转绿
- 新增 `locals.lua` 诊断测试

这一轮明确不做：

- local variable attributes 的完整实现
- `goto` / `label`
- 官方 `locals.lua` 转绿
- `all.lua` 全量通过率统计

## 设计

### 1. 数值 `for` 直接对齐现有 `FORPREP` / `FORLOOP`

先看了官方 fixture 的真实反汇编，再决定怎么降级。

`for_integer_chunk.luac` 很清楚地说明了寄存器布局：

- `A`：循环内部状态
- `A+1`：循环内部状态
- `A+2`：源码可见的循环变量

因此源码编译器不需要额外插入 `MOVE`，只需要：

- 先把初值、上界、步长装进连续三个持久寄存器
- 发出 `FORPREP`
- 把循环变量名绑定到 `A+2`
- 编译循环体
- 在末尾发出 `FORLOOP`

这样生成的执行形态和 VM 已有实现是对齐的。

### 2. 泛型 `for` 按官方字节码形态编译

官方 `for_generic_chunk.luac` 暴露了一个关键事实：

- 可见循环变量不是 `A..A+2`
- 而是 `TFORPREP` 之后的 `A+3...`

这一轮按这个布局实现：

- `A`：iterator
- `A+1`：state
- `A+2`：control / close state
- `A+3...`：源码可见变量

代码形态也照官方走：

- `TFORPREP`
- loop body
- `TFORCALL`
- `TFORLOOP`
- `CLOSE`

这样 `for _, v in pairs(t) do` 这种“单个调用表达式返回多值”的情况也能自然工作。

### 3. 泛型 `for` 的 `break` 不能直接跳出

泛型 `for` 在 VM 里会通过 `TFORPREP` 把某个寄存器登记成 to-be-closed 资源位。

所以这轮处理 `break` 时，不能像 `while` 那样直接 patch 到循环末尾，而是要 patch 到：

- `CLOSE A`

之后再自然落到循环外。

这一步不是可选优化，而是泛型 `for` 正确清理路径的一部分。

### 4. 逻辑右移必须按无符号位模式做

官方 `bitwise.lua` 在很前面就会检查：

- `a >> 4 == ~a`

其中 `a = 0xF0F0F0F0F0F0F0F0`

如果右移按 C# `long >>` 的算术右移做，这条立即失败。

所以这轮把左右移统一改成：

- 先按 `ulong` 解释位模式
- 再做移位
- 最后再转回 `long`

同时把 `math.mininteger` 这类极端负位移也单独收口成 `0`，避免左右移互相递归导致栈溢出。

### 5. 文本编译路径不能依赖 `MMBIN*` 才能调用元方法

官方字节码会在很多二元算术/位运算后面跟 `MMBIN` / `MMBINI` / `MMBINK`。

但我们自己的源码编译器目前还没有发这些指令。

这会导致一个问题：

- 官方 `bwcoercion.lua` 给字符串挂了 `__band` / `__bor` / `__shr` 等元方法
- `bitwise.lua` 在文本编译路径里直接写 `"0xAA.0" & "0xF0.0"`
- 如果 VM 只认 `MMBIN*` 路径，文本编译出来的 chunk 就会错误地失败

这一轮的做法是：

- 继续保留官方字节码路径对 `MMBIN*` 的支持
- 但当普通二元算术/位运算失败、并且后面没有 `MMBIN*` 时，直接按对应事件回退到元方法

这样能把文本编译路径补齐，而不破坏现有官方字节码执行。

### 6. 十六进制 `.0` 字符串要区分“可落进整数”和“只能当浮点”

之前为了支持 `0xF0.0`，实现里把“有小数点但分数部分全是 0 的十六进制串”直接降成整数。

这对：

- `0xF0.0`

是对的，但对：

- `0xffffffffffffffff.0`

会出错，因为它超出了 `int64` 可表示范围，却被错误地回绕成了 `-1`。

这一轮改成：

- 如果 `.0` 形式对应的整数值能精确落进 `int64`，返回整数
- 否则返回浮点

这样：

- `tonumber("0xF0.0")` 仍然是 `240`
- `tonumber("0xffffffffffffffff.0")` 是一个大于 `math.maxinteger` 的浮点
- `math.tointeger("0xffffffffffffffff.0")` 会正确返回 `nil`

## 当前结果

compatibility harness 当前状态：

- `bwcoercion.lua`：通过
- `bitwise.lua`：通过
- `locals.lua`：失败，当前错误是 `variable attributes are not supported yet`

这里新的失败点是明确而稳定的：

- `locals.lua:187:10: variable attributes are not supported yet`

它对应的正是 Step 16 还没补完的 local variable attributes 能力。

## 测试

这一轮新增或更新了这些覆盖：

- `Lua.Compiler.Tests`
  - 数值 `for` 默认步长
  - 数值 `for` + `break`
  - 浮点 `for`
  - 泛型 `for`（显式 iterator/state/control）
  - 泛型 `for`（`pairs(...)` 单调用多返回）
  - 逻辑右移语义
  - 字符串位运算元方法回退
- `Lua.Runtime.Tests`
  - `tonumber("0xffffffffffffffff.0")` 返回超范围浮点
  - `math.tointeger("0xffffffffffffffff.0")` 返回 `nil`
- `Lua.Compatibility.Tests`
  - `bitwise.lua` 绿测
  - `locals.lua` 诊断测试

## 实现清单

- [x] 支持数值 `for` 源码编译
- [x] 支持泛型 `for` 源码编译
- [x] 让泛型 `for` 的 `break` 落到 `CLOSE`
- [x] 修正逻辑右移语义
- [x] 修正极端负位移的栈溢出
- [x] 为文本编译路径补齐二元算术/位运算元方法回退
- [x] 修正超范围十六进制 `.0` 字符串解析
- [x] 把 `bitwise.lua` 从诊断测试转成绿测
- [x] 新增 `locals.lua` 诊断测试
- [x] 更新 README / 文档索引

## 完成标准

本轮完成后，应满足：

- 源码 `for i = ... do` 可以编译执行
- 源码 `for k, v in ... do` 可以编译执行
- `bitwise.lua` 可以整段跑通
- 当前下一条官方失败脚本已被重新量化

## 延期内容

这一轮之后，Step 16 的下一优先级建议是：

- local variable attributes（先把 `locals.lua` 顶过去）
- `goto` / `label`
- 更完整的 traceback / error level
- 弱表与 `__gc`
