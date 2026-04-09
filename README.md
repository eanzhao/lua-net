# lua-net

`lua-net` 是一个用 C# 从头实现 Lua 5.5 的学习型项目。

项目目标很直接：

- 对齐 Lua 5.5 语言行为
- 用清楚、可测试、可阅读的 C# 结构重新实现
- 按阶段推进，每一步先写文档，再写代码和测试

当前工具链基线：

- SDK：.NET 10
- Target Framework：`net10.0`

## 当前进度

目前已经完成的基础工作：

- 明确项目目标为 Lua 5.5.0
- 拉取官方 Lua 5.5.0 源码到 `references/lua-5.5.0/`
- 建立新的运行时主线项目 `Lua.Runtime`
- 建立字节码主线项目 `Lua.Bytecode`
- 建立虚拟机主线项目 `Lua.VM`
- 完成运行时值、栈、调用帧、状态对象的第一版骨架
- 完成 Lua 5.5 指令布局、opcode 顺序和字节码静态模型
- 完成第一版 Lua 5.5 chunk 读取器、`proto` 模型和反汇编器
- 完成第一版 VM 骨架和最小执行循环
- 补上第一批布尔加载、条件判断和跳转指令
- 补上第一批比较与短路指令
- 补上第一批算术与一元运算指令
- 补上第一批 K 变体、位运算和移位指令
- 补上第一批幂运算和字符串原语指令
- 补上第一批表构造与原始表访问指令
- 补上 `SELF` 和最小对象方法调用路径
- 补上 `_ENV` 和最小全局表访问路径
- 补上共享上值 cell 与 `GETUPVAL` / `SETUPVAL`
- 补上 `CLOSE` 和块作用域上值关闭路径
- 补上 `TBC` 的 `nil/false` 最小快速路径
- 补上最小 native closure 与 `_ENV.setmetatable`
- 补上最小 `_ENV.error` 和 Lua 运行时异常对象
- 补上表 metatable 与 `__close` 查找路径
- 补上 to-be-closed 寄存器登记与逆序关闭路径
- 补上 `__close` 的错误对象传递与继续关闭路径
- 补上 `LOADF`、`LOADKX`、`LFALSESKIP` 的最小执行路径
- 补上 `SETLIST` 和数组批量写入路径
- 补上 `VARARG`、`GETVARG` 和第一版开放结果协议
- 补上数值 `for`、泛型 `for` 和循环回跳路径
- 补上 `ERRNNIL` 与全局声明检查路径
- 补上具名 vararg 参数与 vararg table 路径
- 补上 `MMBIN` / `MMBINI` / `MMBINK` 的第一版二元元方法分发路径
- 补上 `LEN` / `CONCAT` 与比较运算的第一版元方法分发路径
- 补上 `UNM` / `BNOT` 与最小 `__call` 路径
- 建立 `Lua.Runtime.Tests`
- 建立 `Lua.Bytecode.Tests`
- 建立 `Lua.VM.Tests`
- 加入真实 Lua 5.5 chunk fixture
- 用官方 Lua 5.5.0 `luac` 统一重编当前 fixture
- 跑通真实 `nested_chunk.luac` 的执行结果
- 跑通真实 `branch_chunk.luac` 的控制流执行结果
- 跑通真实 `eqk_chunk.luac`、`lt_chunk.luac`、`testset_chunk.luac` 的比较与短路结果
- 跑通真实 `arith_chunk.luac`、`addk_chunk.luac`、`not_chunk.luac`、`floor_div_chunk.luac` 的算术与一元结果
- 跑通真实 `k_ops_chunk.luac`、`bit_chunk.luac` 的 K 变体与位运算结果
- 跑通真实 `pow_chunk.luac`、`str_chunk.luac` 的幂运算与字符串结果
- 跑通真实 `table_chunk.luac`、`table_dynamic_chunk.luac` 的表访问结果
- 跑通真实 `self_chunk.luac` 的对象方法调用结果
- 跑通真实 `global_chunk.luac` 的全局读写结果
- 跑通真实 `upvalue_chunk.luac` 的共享上值结果
- 跑通真实 `close_chunk.luac` 的块作用域关闭结果
- 跑通真实 `tbc_nil_chunk.luac`、`tbc_false_chunk.luac` 的 `TBC` 最小快速路径结果
- 跑通真实 `tbc_close_chunk.luac` 的 `__close` 与关闭顺序结果
- 跑通真实 `tbc_error_chunk.luac` 的 `__close` 错误传播与继续关闭结果
- 跑通真实 `loadf_chunk.luac`、`lfalseskip_chunk.luac` 的加载与布尔转换结果
- 跑通手工 proto 的 `LOADKX + EXTRAARG` 结果
- 跑通真实 `setlist_chunk.luac`、`setlist_extraarg_chunk.luac` 的数组批量写入结果
- 跑通真实 `vararg_fixed_chunk.luac`、`vararg_all_chunk.luac` 的 vararg 结果
- 跑通真实 `open_call_chunk.luac` 的开放调用链结果
- 跑通真实 `setlist_open_chunk.luac` 的开放 `SETLIST` 结果
- 跑通手工 proto 的 `GETVARG` 最小语义结果
- 跑通真实 `for_integer_chunk.luac`、`for_float_chunk.luac` 的数值 `for` 结果
- 跑通真实 `for_generic_chunk.luac` 的泛型 `for` 结果
- 跑通真实 `while_chunk.luac` 的循环回跳结果
- 跑通真实 `repeat_chunk.luac` 的 `repeat / until` 结果
- 跑通真实 `global_ok_chunk.luac`、`global_err_chunk.luac` 的全局声明检查结果
- 跑通真实 `vararg_table_return_chunk.luac`、`vararg_table_mix_chunk.luac`、`vararg_table_mutation_chunk.luac` 的具名 vararg 参数与 vararg table 结果
- 跑通真实 `meta_add_chunk.luac`、`meta_addi_chunk.luac`、`meta_flip_chunk.luac`、`meta_addk_chunk.luac` 的二元元方法分发结果
- 跑通真实 `meta_len_chunk.luac`、`meta_concat_chunk.luac`、`meta_eq_chunk.luac`、`meta_lt_chunk.luac`、`meta_le_chunk.luac` 的长度、拼接与比较元方法结果
- 跑通真实 `meta_lti_chunk.luac`、`meta_gti_chunk.luac`、`meta_lei_chunk.luac`、`meta_gei_chunk.luac` 的立即数比较元方法结果
- 跑通真实 `meta_unm_chunk.luac`、`meta_bnot_chunk.luac` 的一元元方法结果
- 跑通真实 `meta_call_chunk.luac`、`meta_tailcall_chunk.luac` 的 `__call` 结果

当前主线测试结果：

- `dotnet test lua-net.sln`
- 101 个测试通过

## 仓库结构

当前仓库主要目录如下：

- `docs/`
  阶段规划和设计文档
- `references/lua-5.5.0/`
  官方 Lua 5.5.0 源码参考
- `src/Lua.Runtime/`
  当前主线运行时实现
- `src/Lua.Bytecode/`
  当前主线字节码实现
- `src/Lua.VM/`
  当前主线虚拟机实现
- `test/Lua.Runtime.Tests/`
  当前主线运行时测试
- `test/Lua.Bytecode.Tests/`
  当前主线字节码测试
- `test/Lua.VM.Tests/`
  当前主线虚拟机测试

后续会逐步扩展到这些模块：

- `Lua.Bytecode`
- `Lua.Syntax`
- `Lua.Compiler`
- `Lua.StandardLib`
- `Lua.Cli`

## 文档索引

当前已经落地的文档：

- [docs/001-roadmap.md](docs/001-roadmap.md)
- [docs/002-step-01-foundation.md](docs/002-step-01-foundation.md)
- [docs/003-step-01-source-reference.md](docs/003-step-01-source-reference.md)
- [docs/004-step-02-runtime-model.md](docs/004-step-02-runtime-model.md)
- [docs/005-step-03-bytecode-loader.md](docs/005-step-03-bytecode-loader.md)
- [docs/006-step-04-vm-skeleton.md](docs/006-step-04-vm-skeleton.md)
- [docs/007-step-04-table-access.md](docs/007-step-04-table-access.md)
- [docs/008-step-04-self-call.md](docs/008-step-04-self-call.md)
- [docs/009-step-04-global-environment.md](docs/009-step-04-global-environment.md)
- [docs/010-step-05-upvalue-cells.md](docs/010-step-05-upvalue-cells.md)
- [docs/011-step-05-close.md](docs/011-step-05-close.md)
- [docs/012-step-05-tbc.md](docs/012-step-05-tbc.md)
- [docs/013-step-05-close-metamethod.md](docs/013-step-05-close-metamethod.md)
- [docs/014-step-05-close-errors.md](docs/014-step-05-close-errors.md)
- [docs/015-step-04-load-opcodes.md](docs/015-step-04-load-opcodes.md)
- [docs/016-step-04-setlist.md](docs/016-step-04-setlist.md)
- [docs/017-step-04-vararg-open-results.md](docs/017-step-04-vararg-open-results.md)
- [docs/018-step-04-loops.md](docs/018-step-04-loops.md)
- [docs/019-step-04-repeat-global-checks.md](docs/019-step-04-repeat-global-checks.md)
- [docs/020-step-04-vararg-table.md](docs/020-step-04-vararg-table.md)
- [docs/021-step-04-binary-metamethods.md](docs/021-step-04-binary-metamethods.md)
- [docs/022-step-04-length-concat-compare-metamethods.md](docs/022-step-04-length-concat-compare-metamethods.md)
- [docs/023-step-04-unary-call-metamethods.md](docs/023-step-04-unary-call-metamethods.md)

这些文档对应的是：

- 总路线图
- 第 1 步基础基线
- 官方源码参考策略
- 第 2 步运行时模型
- 第 3 步字节码加载与反汇编
- 第 4 步 VM 骨架与最小执行闭环
- 第 4 步补充：表访问与对象基础路径
- 第 4 步补充：SELF 与对象方法调用
- 第 4 步补充：_ENV 与最小全局表访问
- 第 5 步：共享上值 Cell 与最小捕获语义
- 第 5 步补充：CLOSE 与块作用域上值关闭
- 第 5 步补充：TBC 的最小快速路径
- 第 5 步补充：`__close` 与 to-be-closed 生命周期第一版
- 第 5 步补充：`__close` 的错误传播与继续关闭
- 第 4 步补充：剩余加载路径
- 第 4 步补充：`SETLIST` 与数组批量写入
- 第 4 步补充：`VARARG` 与开放结果协议
- 第 4 步补充：循环执行路径
- 第 4 步补充：`repeat / until` 与全局声明检查
- 第 4 步补充：具名 vararg 参数与 vararg table
- 第 4 步补充：二元算术与位运算元方法分发
- 第 4 步补充：长度、拼接与比较元方法分发
- 第 4 步补充：一元元方法与最小 `__call`

## 开发方式

这个项目按下面的节奏推进：

1. 先明确阶段目标
2. 先在 `docs/` 里写阶段文档
3. 再写最小可用实现
4. 用测试把当前阶段钉住
5. 再进入下一步

官方资料的使用顺序：

1. 先看 Lua 5.5 手册
2. 再看官方 Lua 5.5 源码
3. 最后把行为落实到 C# 代码和测试

补充约定：

- `references/lua-5.5.0/src` 里的本地临时构建产物已经通过 `.gitignore` 忽略

## 快速开始

### 1. 还原并运行测试

```bash
dotnet test lua-net.sln
```

### 2. 查看当前主线项目

```bash
dotnet sln lua-net.sln list
```

### 3. 查看官方源码参考

```bash
ls references/lua-5.5.0/src
```

## 当前实现范围

当前 `Lua.Runtime` 已经包含这些基础类型：

- `LuaValueKind`
- `LuaValue`
- `LuaTable`
- `LuaClosure`
- `LuaNativeClosureBody`
- `LuaUpvalue`
- `LuaThread`
- `LuaUserData`
- `LuaStack`
- `CallFrame`
- `LuaRuntimeException`
- `LuaState`
- `ILuaClosureBody`

这些类型的目标不是一次做满，而是先为后续 VM、字节码、闭包、表和标准库提供统一的运行时承载结构。

当前 `Lua.Bytecode` 已经包含这些基础能力：

- Lua 5.5 chunk 头常量
- 指令格式定义
- 指令位布局定义
- opcode 枚举
- opcode 名称表
- opcode 模式表
- 原始 32 位指令解码
- `LuaChunkReader`
- `LuaChunk` / `LuaPrototype` / `LuaConstant` 模型
- 真实 Lua 5.5 chunk 的基础结构读取
- 第一版反汇编输出

当前 `Lua.VM` 已经包含这些基础能力：

- `LuaBytecodeClosureBody`
- `LuaVirtualMachine`
- 固定参数调用协议与第一版开放结果协议
- `MOVE` / `LOADFALSE` / `LOADTRUE` / `LOADNIL`
- `LOADI` / `LOADF` / `LOADK` / `LOADKX`
- `LFALSESKIP`
- `GETUPVAL`
- `GETTABUP`
- `GETTABLE` / `GETI` / `GETFIELD`
- `SETUPVAL`
- `SETTABUP`
- `SETTABLE` / `SETI` / `SETFIELD`
- `SETLIST`
- `NEWTABLE`
- `SELF`
- `ADDI` / `ADDK` / `SUBK` / `MULK` / `MODK` / `DIVK` / `IDIVK`
- `POWK`
- `ADD` / `SUB` / `MUL` / `MOD` / `POW` / `DIV` / `IDIV`
- `BANDK` / `BORK` / `BXORK`
- `BAND` / `BOR` / `BXOR`
- `SHLI` / `SHRI` / `SHL` / `SHR`
- `MMBIN` / `MMBINI` / `MMBINK`
- `UNM` / `BNOT` / `NOT`
- `LEN` / `CONCAT`
- `CLOSE`
- `TBC`
- `_ENV.setmetatable`
- `_ENV.error`
- table metatable 上的最小 `__call`
- `JMP`
- `EQ` / `LT` / `LE` / `EQK`
- `EQI` / `LTI` / `LEI` / `GTI` / `GEI`
- `TEST` / `TESTSET`
- `CALL` / `TAILCALL` / `RETURN` / `RETURN0` / `RETURN1`
- `FORLOOP` / `FORPREP`
- `TFORPREP` / `TFORCALL` / `TFORLOOP`
- `CLOSURE` / `VARARG` / `GETVARG` / `ERRNNIL` / `VARARGPREP`
- 真实 Lua 5.5 chunk 的最小执行闭环
- 真实控制流 chunk 的基础执行闭环
- 真实比较与短路 chunk 的基础执行闭环
- 真实算术与一元 chunk 的基础执行闭环
- 真实 K 变体与位运算 chunk 的基础执行闭环
- 真实幂运算与字符串 chunk 的基础执行闭环
- 真实表构造与原始表访问 chunk 的基础执行闭环
- 真实对象方法调用 chunk 的基础执行闭环
- 真实全局表访问 chunk 的基础执行闭环
- 真实共享上值 chunk 的基础执行闭环
- 真实块作用域关闭 chunk 的基础执行闭环
- 真实 `TBC` 最小快速路径 chunk 的基础执行闭环
- 真实 `__close` 与关闭顺序 chunk 的基础执行闭环
- 真实 `__close` 错误传播与继续关闭 chunk 的基础执行闭环
- 真实 `LOADF` / `LFALSESKIP` chunk 的基础执行闭环
- 手工 proto 的 `LOADKX + EXTRAARG` 执行闭环
- 真实 `SETLIST` / `EXTRAARG` chunk 的基础执行闭环
- 真实 vararg chunk 的基础执行闭环
- 真实开放调用链 chunk 的基础执行闭环
- 真实开放 `SETLIST` chunk 的基础执行闭环
- 手工 proto 的 `GETVARG` 执行闭环
- 真实数值 `for` chunk 的基础执行闭环
- 真实泛型 `for` chunk 的基础执行闭环
- 真实 backward `JMP` chunk 的基础执行闭环
- 真实 `repeat / until` chunk 的基础执行闭环
- 真实全局声明检查 chunk 的基础执行闭环
- 真实具名 vararg 参数与 vararg table chunk 的基础执行闭环
- 真实二元算术与位运算元方法 chunk 的基础执行闭环
- 真实长度、拼接与比较元方法 chunk 的基础执行闭环
- 真实一元元方法与 `__call` chunk 的基础执行闭环

## 下一步

下一步会继续沿着 VM 和运行时主线往下补：

- 补上 `__index` / `__newindex` 和更一般的表访问元方法
- 补上更多控制流与标准库配套路径
- 补上更完整的全局声明与相关语义
- 扩展 userdata 路径
- 补上更完整的 to-be-closed 生命周期和错误恢复路径
- 开始往语法分析和编译器主线推进

## 参考资料

- [Lua 5.5 手册](https://www.lua.org/manual/5.5/)
- [Lua 5.5 发布说明](https://www.lua.org/manual/5.5/readme.html)
- [Lua 官方测试集](https://www.lua.org/tests/)
- [Lua 5.5 官方源码索引](https://www.lua.org/source/5.5/)
