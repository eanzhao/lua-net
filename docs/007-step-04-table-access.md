# 第 4 步补充：表访问与对象基础路径

## 状态

已完成当前这一轮。

这一轮继续停留在 VM 主线里，但聚焦点从数值和字符串原语，转到了最小可用的表构造与原始表访问。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/ltable.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮特别关注的是 `OP_NEWTABLE`、`OP_GETTABLE`、`OP_GETI`、`OP_GETFIELD`、`OP_SETTABLE`、`OP_SETI`、`OP_SETFIELD` 这一组指令在官方 VM 里的快速路径。

## 本步骤范围

本轮先落这几件事：

- 把 `LuaTable` 从占位对象补成最小可存取模型
- 支持字符串键、整数键和寄存器键三种原始访问路径
- 支持 `NEWTABLE` 和它后面的 `EXTRAARG`
- 让 `LEN` 能处理最小表长度路径
- 用真实 Lua 5.5 chunk 验证表构造和表访问结果
- 用官方 Lua 5.5.0 `luac` 统一重编仓库里的 fixture

## 设计原则

### 1. 先做原始访问，不先做元方法

Lua 表相关语义很大一块都围绕元表和元方法展开。

但在当前阶段，更重要的是先把：

- 表对象能存值
- VM 能走通原始访问
- 真实 chunk 能把值放进去再读出来

这条路径钉住。

所以这一轮只做最小快速路径，元方法调度继续留给后面。

### 2. 先统一键规范，再谈更复杂布局

Lua 里的数值键有一个很关键的细节：

- `1`
- `1.0`

在表访问里应当落到同一个键上。

所以 `LuaTable` 这一轮先做键规范化，把“可精确表示为整数的浮点键”归一到整数键上。这样 `GETI`、`SETI` 和通用 `GETTABLE`、`SETTABLE` 才能在最小模型里对齐起来。

### 3. 先保证真实产物一致

这轮不只补运行时对象和 opcode，还顺手把所有 `test/fixtures/lua55/source/*.lua` 用本地编译出的官方 `luac 5.5.0` 重新生成了一遍，避免把系统自带的 Lua 5.4 产物混进来。

## 当前支持范围

这一轮新增支持这些路径：

- `NEWTABLE`
- `GETTABLE`
- `GETI`
- `GETFIELD`
- `SETTABLE`
- `SETI`
- `SETFIELD`
- `NEWTABLE` 后续 `EXTRAARG` 的最小处理

运行时表对象当前提供这些最小能力：

- 原始键值读写
- `nil` 赋值删除键
- 整数键与整数值浮点键归一
- 连续整数序列长度计算

当前还没有进入这些内容：

- 数组区和哈希区的完整分离实现
- 元表查找
- `__index` / `__newindex`
- 表相关垃圾回收细节
- 更复杂的长度边界规则

## 实现清单

- [x] 编写本轮文档
- [x] 扩展 `LuaTable` 的最小存取模型
- [x] 支持 `NEWTABLE`
- [x] 支持 `GETTABLE` / `GETI` / `GETFIELD`
- [x] 支持 `SETTABLE` / `SETI` / `SETFIELD`
- [x] 处理 `NEWTABLE` 后续的 `EXTRAARG`
- [x] 为表对象补运行时测试
- [x] 新增真实 `table_chunk.luac`
- [x] 新增真实 `table_dynamic_chunk.luac`
- [x] 用官方 Lua 5.5.0 `luac` 重编全部 fixture

## 完成标准

本轮完成后，应满足：

- 表对象可以承载最小原始键值读写
- VM 可以执行最小表构造与读写路径
- 真实字段键、整数键、寄存器键 fixture 可以跑出正确结果
- 表长度至少能覆盖当前连续整数键的快速路径

## 下一步

接下来继续往下补：

- `SELF`、`GETTABUP`、`SETTABUP` 等剩余对象访问路径
- `LOADF`、`LOADKX`、`EXTRAARG` 的其余使用场景
- 更完整的调用协议
- 上值捕获
- 元方法调度
