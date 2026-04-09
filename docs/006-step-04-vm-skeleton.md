# 第 4 步：VM 骨架与最小执行闭环

## 状态

进行中。

这一阶段的目标，是把前面已经落好的 `Runtime` 和 `Bytecode` 真正接起来，先形成一条最小但真实可跑的执行链路。

这一步不追求一次把 Lua VM 做完整，而是先完成：

- 从 `LuaChunk` 创建可执行闭包
- 建立第一版取指执行循环
- 支持一组最小 opcode
- 让真实 Lua 5.5 chunk 能跑出结果

## 背景与参考

这一阶段主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lvm.h`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lfunc.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一阶段的关注点，不是把 `lvm.c` 逐行翻成 C#，而是先把它拆成更容易读懂和逐步扩展的结构。

## 本步骤范围

本阶段先落这几件事：

- 建立 `Lua.VM` 项目
- 定义“闭包挂执行体”的最小承载方式
- 实现第一版 `LuaVirtualMachine`
- 支持固定参数、固定返回值的最小调用协议
- 支持一组能跑通真实 fixture 的基础指令
- 建立 `Lua.VM.Tests`
- 用真实 `nested_chunk.luac` 验证执行结果
- 用真实 `branch_chunk.luac` 验证控制流执行结果
- 用真实 `eqk_chunk.luac`、`lt_chunk.luac`、`testset_chunk.luac` 验证比较与短路行为
- 用真实 `arith_chunk.luac`、`addk_chunk.luac`、`not_chunk.luac`、`floor_div_chunk.luac` 验证算术与一元运算行为
- 用真实 `k_ops_chunk.luac`、`bit_chunk.luac` 验证 K 变体、位运算与移位行为
- 用真实 `pow_chunk.luac`、`str_chunk.luac` 验证幂运算与字符串原语行为
- 用真实 `self_chunk.luac` 验证对象方法调用行为
- 用真实 `global_chunk.luac` 验证 `_ENV` 全局读写行为
- 用真实 `upvalue_chunk.luac` 验证共享上值捕获行为
- 用真实 `close_chunk.luac` 验证块作用域关闭上值行为
- 用真实 `tbc_nil_chunk.luac`、`tbc_false_chunk.luac` 验证 `TBC` 的最小快速路径
- 用真实 `meta_add_chunk.luac`、`meta_addi_chunk.luac`、`meta_flip_chunk.luac`、`meta_addk_chunk.luac` 验证二元元方法分发行为
- 用真实 `meta_len_chunk.luac`、`meta_concat_chunk.luac`、`meta_eq_chunk.luac`、`meta_lt_chunk.luac`、`meta_le_chunk.luac` 验证长度、拼接与比较元方法行为
- 用真实 `meta_lti_chunk.luac`、`meta_gti_chunk.luac`、`meta_lei_chunk.luac`、`meta_gei_chunk.luac` 验证立即数比较元方法行为
- 用真实 `meta_unm_chunk.luac`、`meta_bnot_chunk.luac` 验证一元元方法行为
- 用真实 `meta_call_chunk.luac`、`meta_tailcall_chunk.luac` 验证最小 `__call` 行为
- 用真实 `meta_index_table_chunk.luac`、`meta_index_function_chunk.luac` 验证 `__index` 表访问元方法行为
- 用真实 `meta_newindex_table_chunk.luac`、`meta_newindex_function_chunk.luac`、`meta_newindex_existing_chunk.luac` 验证 `__newindex` 表访问元方法行为

## 设计原则

### 1. 先把执行路径接通

现在项目已经有：

- 运行时值模型
- 调用帧和状态容器
- chunk 与 proto 模型
- opcode 和指令解码

所以第 4 步最重要的事情，不是继续堆模型，而是把这几块接成一条真实链路。

### 2. 先从固定协议出发，再逐步接开放结果

Lua 完整调用协议里有不少复杂点：

- 变长参数
- 开放参数个数
- 开放返回值个数
- 上值捕获
- 元方法调度

这一步最开始先只支持最小固定协议：

- 固定实参数量
- 固定返回值数量
- 基础数值运算
- 基础闭包调用

这样可以先把主循环稳定下来，再逐层扩展。

到当前这一轮，已经在这个基础上继续接上了第一版动态结果路径：

- `VARARG`
- open `CALL`
- open `TAILCALL`
- open `RETURN`
- `SETLIST B == 0`

### 3. 用真实 chunk 验证，而不是只测手工数据

如果只测手工构造的 proto，很容易把实现写成“只适配测试”。

所以这一步除了手工指令测试，还要直接执行真实 Lua 5.5 chunk，确保：

- `ChunkReader` 输出的模型足够支撑执行
- `VM` 对 opcode 的理解和官方产物一致
- 前三步的拆分方式能形成闭环

## 当前支持范围

第一版 VM 先支持这些能力：

- `MOVE`
- `LOADFALSE`
- `LOADTRUE`
- `LOADNIL`
- `LOADI`
- `LOADK`
- `GETUPVAL`
- `GETTABUP`
- `GETTABLE`
- `GETI`
- `GETFIELD`
- `SETUPVAL`
- `SETTABUP`
- `SETTABLE`
- `SETI`
- `SETFIELD`
- `NEWTABLE`
- `SELF`
- `ADDI`
- `ADDK`
- `SUBK`
- `MULK`
- `MODK`
- `DIVK`
- `IDIVK`
- `POWK`
- `ADD`
- `SUB`
- `MUL`
- `MOD`
- `POW`
- `DIV`
- `IDIV`
- `BANDK`
- `BORK`
- `BXORK`
- `BAND`
- `BOR`
- `BXOR`
- `SHLI`
- `SHRI`
- `SHL`
- `SHR`
- `MMBIN`
- `MMBINI`
- `MMBINK`
- `UNM`
- `BNOT`
- `NOT`
- `LEN`
- `CONCAT`
- `CLOSE`
- `TBC`
- `JMP`
- `EQ`
- `LT`
- `LE`
- `EQK`
- `EQI`
- `LTI`
- `LEI`
- `GTI`
- `GEI`
- `TEST`
- `TESTSET`
- `CALL`
- `TAILCALL`
- `RETURN`
- `RETURN0`
- `RETURN1`
- `FORLOOP`
- `FORPREP`
- `TFORPREP`
- `TFORCALL`
- `TFORLOOP`
- `CLOSURE`
- `VARARG`
- `GETVARG`
- `ERRNNIL`
- `VARARGPREP`

当前二元算术和位运算已经补上了第一版 `MMBIN` / `MMBINI` / `MMBINK` 分发，但仍然只覆盖最小对象语义。

当前算术也只先支持最小快速路径：

- `ADDI` / `ADDK` / `SUBK` / `MULK` / `ADD` / `SUB` / `MUL` 先支持整数与浮点数
- 二元算术快速路径失败时，会继续落到 `MMBIN` / `MMBINI` / `MMBINK` 查找 `__add`、`__sub`、`__mul`、`__mod`、`__pow`、`__div`、`__idiv`
- `POWK` / `POW` 先支持数值路径，并按 Lua 的浮点结果语义返回
- `DIV` 按 Lua 规则返回浮点结果
- `DIVK` / `IDIVK` / `MODK` 和寄存器版本一起对齐 Lua 规则
- `IDIV` 和 `MOD` 先对齐 Lua 的向下取整和取模规则
- `UNM` / `NOT` 先支持基础数值和真假值语义
- `UNM` 也补上了 table metatable 上的 `__unm`

当前字符串原语也先支持最小快速路径：

- `LEN` 先支持字符串长度，以及 table metatable 上的 `__len`
- `CONCAT` 先支持字符串和数值拼接，也支持 table metatable 上的 `__concat`
- userdata 和更完整的对象语义放到后续阶段

当前表访问也先支持最小元方法路径：

- `GETTABUP` / `GETTABLE` / `GETI` / `GETFIELD` 现在在原始 miss 时，也支持 table metatable 上的 `__index`
- `SETTABUP` / `SETTABLE` / `SETI` / `SETFIELD` 现在在原始 miss 时，也支持 table metatable 上的 `__newindex`
- `__index` / `__newindex` 先支持 table fallback 和 function fallback
- 已有原始键命中时，会绕过 `__newindex`

当前位运算也先支持快速路径：

- `BANDK` / `BORK` / `BXORK` 与寄存器版本先支持整数运算
- `SHLI` / `SHRI` / `SHL` / `SHR` 先对齐 Lua 的移位规则
- 二元位运算快速路径失败时，也会通过 `MMBIN` / `MMBINI` / `MMBINK` 走 `__band`、`__bor`、`__bxor`、`__shl`、`__shr`
- `BNOT` 先支持整数路径，也补上了 table metatable 上的 `__bnot`

当前分支和比较也只先支持最小快速路径：

- `TEST` 走 Lua 的真假值规则
- `TESTSET` 走 Lua 的短路规则
- `EQ` / `EQK` 先走原始比较快速路径，并补上 table metatable 上的 `__eq`
- `LT` / `LE` 先支持数值和字符串顺序比较，并补上 table metatable 上的 `__lt` / `__le`
- `LTI` / `LEI` / `GTI` / `GEI` 先支持数值路径，也补上 table metatable 的立即数比较元方法路径
- userdata 和更复杂的比较行为放到后续阶段

当前调用协议也先支持最小可调用对象路径：

- `CALL` / `TAILCALL` 现在除函数外，也支持 table metatable 上的 `__call`
- `TFORCALL` 沿用同一套最小 callable 解析
- `__call` chain 和 userdata 可调用对象放到后续阶段

## 模型说明

### `ILuaClosureBody`

职责：

- 让 `LuaClosure` 可以挂不同类型的执行体
- 继续保持 `Lua.Runtime` 不依赖 `Lua.Bytecode` 和 `Lua.VM`

这样后续无论是：

- Lua 字节码闭包
- C# 原生函数闭包
- 标准库包装闭包

都可以挂到同一个运行时闭包容器上。

### `LuaBytecodeClosureBody`

职责：

- 挂住一个 `LuaPrototype`
- 作为第一版 VM 的可执行函数体

### `LuaVirtualMachine`

职责：

- 从 `LuaChunk` 或 `LuaClosure` 启动执行
- 管理 `LuaState`
- 建立第一版取指执行循环
- 解释当前支持的 opcode
- 返回执行结果

## 当前实现清单

- [x] 编写本阶段文档
- [x] 建立 `Lua.VM` 项目
- [x] 让 `LuaClosure` 可以挂执行体
- [x] 实现第一版 `LuaVirtualMachine`
- [x] 支持最小调用与返回协议
- [x] 支持最小布尔加载与条件跳转
- [x] 支持第一批比较与短路指令
- [x] 支持第一批算术与一元运算指令
- [x] 支持第一批 K 变体、位运算与移位指令
- [x] 支持第一批幂运算与字符串原语指令
- [x] 建立 `Lua.VM.Tests`
- [x] 用真实 `nested_chunk.luac` 验证执行结果
- [x] 用真实 `branch_chunk.luac` 验证控制流结果
- [x] 用真实 `eqk_chunk.luac`、`lt_chunk.luac`、`testset_chunk.luac` 验证比较与短路结果
- [x] 用真实 `arith_chunk.luac`、`addk_chunk.luac`、`not_chunk.luac`、`floor_div_chunk.luac` 验证算术与一元结果
- [x] 用真实 `k_ops_chunk.luac`、`bit_chunk.luac` 验证 K 变体、位运算与移位结果
- [x] 用真实 `pow_chunk.luac`、`str_chunk.luac` 验证幂运算与字符串原语结果
- [x] 用真实 `self_chunk.luac` 验证对象方法调用结果
- [x] 用真实 `global_chunk.luac` 验证 `_ENV` 全局读写结果
- [x] 用真实 `upvalue_chunk.luac` 验证共享上值捕获结果
- [x] 用真实 `close_chunk.luac` 验证块作用域关闭上值结果
- [x] 用真实 `tbc_nil_chunk.luac`、`tbc_false_chunk.luac` 验证 `TBC` 最小快速路径结果
- [x] 用真实 `tbc_close_chunk.luac` 验证 `__close`、`CLOSE` 与函数退出关闭结果
- [x] 用真实 `tbc_error_chunk.luac` 验证 `__close` 的错误传播与继续关闭结果
- [x] 用真实 `loadf_chunk.luac`、`lfalseskip_chunk.luac` 验证剩余加载路径结果
- [x] 用手工 proto 验证 `LOADKX + EXTRAARG` 执行结果
- [x] 用真实 `setlist_chunk.luac`、`setlist_extraarg_chunk.luac` 验证 `SETLIST` 与 `EXTRAARG` 结果
- [x] 用真实 `vararg_fixed_chunk.luac`、`vararg_all_chunk.luac` 验证 `VARARG` 结果
- [x] 用真实 `open_call_chunk.luac` 验证开放调用链结果
- [x] 用真实 `setlist_open_chunk.luac` 验证开放 `SETLIST` 结果
- [x] 用手工 proto 验证 `GETVARG` 最小语义结果
- [x] 用真实 `for_integer_chunk.luac`、`for_float_chunk.luac` 验证数值 `for` 结果
- [x] 用真实 `for_generic_chunk.luac` 验证泛型 `for` 结果
- [x] 用真实 `while_chunk.luac` 验证 backward `JMP` 结果
- [x] 用真实 `repeat_chunk.luac` 验证 `repeat / until` 结果
- [x] 用真实 `global_ok_chunk.luac`、`global_err_chunk.luac` 验证 `ERRNNIL` 与 `global` 声明检查结果
- [x] 用真实 `vararg_table_return_chunk.luac`、`vararg_table_mix_chunk.luac`、`vararg_table_mutation_chunk.luac` 验证具名 vararg 参数与 vararg table 结果
- [x] 用真实 `meta_add_chunk.luac`、`meta_addi_chunk.luac`、`meta_flip_chunk.luac`、`meta_addk_chunk.luac` 验证二元元方法分发结果
- [x] 用真实 `meta_len_chunk.luac`、`meta_concat_chunk.luac`、`meta_eq_chunk.luac`、`meta_lt_chunk.luac`、`meta_le_chunk.luac` 验证长度、拼接与比较元方法结果
- [x] 用真实 `meta_lti_chunk.luac`、`meta_gti_chunk.luac`、`meta_lei_chunk.luac`、`meta_gei_chunk.luac` 验证立即数比较元方法结果
- [x] 用真实 `meta_unm_chunk.luac`、`meta_bnot_chunk.luac` 验证一元元方法结果
- [x] 用真实 `meta_call_chunk.luac`、`meta_tailcall_chunk.luac` 验证最小 `__call` 结果
- [x] 用真实 `meta_index_table_chunk.luac`、`meta_index_function_chunk.luac` 验证 `__index` 结果
- [x] 用真实 `meta_newindex_table_chunk.luac`、`meta_newindex_function_chunk.luac`、`meta_newindex_existing_chunk.luac` 验证 `__newindex` 结果

## 完成标准

本阶段这一轮完成时，应满足：

- `Lua.VM` 可以独立编译
- `LuaChunk` 能进入 VM 执行
- 至少有一份真实 Lua 5.5 chunk 能跑出正确结果
- 调用帧和栈在执行前后能正确收拢
- 测试可以覆盖手工 proto 和真实 fixture 两条路径

## 下一步

这一步跑通之后，后续继续往下补：

- 表访问的最小快速路径已经拆到 `docs/007-step-04-table-access.md`
- `SELF` 与最小对象方法调用路径已经拆到 `docs/008-step-04-self-call.md`
- `_ENV` 与最小全局表访问路径已经拆到 `docs/009-step-04-global-environment.md`
- 共享上值 cell 与 `GETUPVAL` / `SETUPVAL` 已经拆到 `docs/010-step-05-upvalue-cells.md`
- `CLOSE` 与块作用域上值关闭已经拆到 `docs/011-step-05-close.md`
- `TBC` 的 `nil/false` 快速路径已经拆到 `docs/012-step-05-tbc.md`
- `__close` 与 to-be-closed 生命周期第一版已经拆到 `docs/013-step-05-close-metamethod.md`
- `__close` 的错误传播与继续关闭已经拆到 `docs/014-step-05-close-errors.md`
- 剩余加载路径已经拆到 `docs/015-step-04-load-opcodes.md`
- `SETLIST` 与数组批量写入已经拆到 `docs/016-step-04-setlist.md`
- `VARARG` 与开放结果协议已经拆到 `docs/017-step-04-vararg-open-results.md`
- 循环执行路径已经拆到 `docs/018-step-04-loops.md`
- `repeat / until` 与全局声明检查已经拆到 `docs/019-step-04-repeat-global-checks.md`
- 具名 vararg 参数与 vararg table 已经拆到 `docs/020-step-04-vararg-table.md`
- 二元算术与位运算元方法分发已经拆到 `docs/021-step-04-binary-metamethods.md`
- 长度、拼接与比较元方法分发已经拆到 `docs/022-step-04-length-concat-compare-metamethods.md`
- 一元元方法与最小 `__call` 已经拆到 `docs/023-step-04-unary-call-metamethods.md`
- 表访问元方法分发已经拆到 `docs/024-step-04-table-metamethods.md`
- 更完整的调用协议
- 更完整的跳转与条件分支
- `EQI` / `LEI` / `GEI` 之外更多比较组合
- `and` / `or` 之外更复杂的短路场景
- 更一般的元方法调度
