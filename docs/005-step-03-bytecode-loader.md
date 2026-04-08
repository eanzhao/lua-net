# 第 3 步：字节码加载与反汇编

## 状态

进行中。

这一阶段的目标，是把 Lua 5.5 的 chunk 结构和指令布局先固定下来，为后续的 chunk 读取器、反汇编器和 VM 提供统一的字节码模型。

## 背景与参考

这一阶段主要参考这些官方文件：

- `references/lua-5.5.0/src/lundump.c`
- `references/lua-5.5.0/src/lundump.h`
- `references/lua-5.5.0/src/ldump.c`
- `references/lua-5.5.0/src/lopcodes.h`
- `references/lua-5.5.0/src/lopcodes.c`
- `references/lua-5.5.0/src/lopnames.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一阶段先做“模型对齐”，再做“完整读取”。

也就是说，先把下面这些内容稳定下来：

- chunk 头常量
- 指令格式
- 参数位宽与偏移
- opcode 顺序
- opcode 模式表

## 本步骤范围

本阶段会依次完成这些内容：

- 建立 `Lua.Bytecode` 项目
- 定义 Lua 5.5 chunk 头常量
- 定义指令格式枚举
- 定义指令位布局
- 定义 opcode 枚举
- 定义 opcode 名称表
- 定义 opcode 模式表
- 定义 `LuaInstruction` 解码结构
- 建立基础测试

这一阶段已经进入后半段，重点从静态模型推进到了真实 chunk 读取。

## 设计原则

### 1. 先把布局钉死

Lua 5.5 的指令字段布局、参数位宽、signed 参数偏移，都是后续字节码处理的基础。

这些内容一旦抽象稳定，后面无论是：

- 读取 chunk
- 反汇编
- 构建 proto 模型
- 驱动 VM 执行

都可以复用同一套位操作逻辑。

### 2. opcode 顺序必须严格对齐官方

Lua 的 opcode 顺序不是随便取的。

官方 `lopcodes.h`、`lopcodes.c`、`lopnames.h` 里的顺序必须保持一致，否则：

- 反汇编输出会错
- 指令模式判断会错
- 运行时调度也会错

所以这一步会先把顺序和模式表明确写死，并用测试保护。

### 3. 先做可测的静态模型

这一步先把“静态字节码知识”提取出来：

- 常量
- 表
- 解码逻辑

这样后面再写二进制读取器时，重点就只剩下 I/O 和结构还原，不会把位布局和流读取搅在一起。

## 模型说明

### `LuaChunkHeaderConstants`

职责：

- 定义 Lua 5.5 chunk 头部使用的固定字节和数值

当前重点包括：

- `LUA_SIGNATURE`
- `LUAC_DATA`
- `LUAC_VERSION`
- `LUAC_FORMAT`
- `LUAC_INT`
- `LUAC_INST`
- `LUAC_NUM`

### `LuaInstructionFormat`

职责：

- 对齐 Lua 5.5 的 6 种指令格式：
  - `iABC`
  - `ivABC`
  - `iABx`
  - `iAsBx`
  - `iAx`
  - `isJ`

### `LuaInstructionLayout`

职责：

- 定义字段位宽
- 定义字段偏移
- 提供 `MAXARG_*` 与 `OFFSET_*`
- 为指令解码提供统一位运算入口

### `LuaOpcode`

职责：

- 按官方顺序列出所有 Lua 5.5 opcode

### `LuaOpcodeTables`

职责：

- 提供 opcode 名称表
- 提供 opcode 模式表
- 提供基础元数据访问入口

### `LuaInstruction`

职责：

- 表示一条原始 32 位指令
- 提供 `Opcode`、`A`、`B`、`C`、`Bx`、`sBx`、`Ax`、`sJ` 等访问方法
- 为反汇编和后续 VM 解码提供统一入口

## 当前实现清单

- [x] 编写本阶段文档
- [x] 创建 `Lua.Bytecode` 项目
- [x] 定义 chunk 头常量
- [x] 定义指令格式与位布局
- [x] 定义 opcode 顺序、名称表和模式表
- [x] 定义基础指令解码结构
- [x] 创建 `Lua.Bytecode.Tests`
- [x] 为常量和指令解码补测试
- [x] 建立第一版 chunk 与 proto 模型
- [x] 实现第一版 chunk 读取器
- [x] 用真实 Lua 5.5 chunk 夹具验证读取结果
- [x] 输出第一版反汇编文本

## 完成标准

本阶段完成时，应满足：

- `Lua.Bytecode` 可以独立编译
- opcode 顺序与官方 Lua 5.5 一致
- 指令字段解码有测试保护
- chunk 头常量与官方 Lua 5.5 一致
- 能读取真实 Lua 5.5 chunk 的基础结构
- 能输出第一版可读反汇编结果

## 延后处理的内容

这些内容放到下一轮继续展开：

- 更完整的指令参数格式化
- 更多 chunk 夹具
- 更复杂的函数嵌套和字符串场景验证
- 让反汇编结果进一步向官方 `luac -l -l` 靠拢

## 下一步

当前 chunk 读取和第一版反汇编已经落稳，下一步继续：

- 增加更多真实 Lua 5.5 chunk fixture
- 扩展反汇编格式化规则
- 进入 VM 骨架设计与实现
