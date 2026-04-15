# 第 5 步：SELF 与对象方法调用

## 状态

已完成当前这一轮。

这一轮继续停留在 VM 主线里，目标是把 Lua 里最常见的一条对象调用路径接起来，也就是 `obj:method(...)` 这一层。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lvm.c`
- `references/lua-5.5.0/src/lopcodes.h`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这轮关注的核心指令是：

- `OP_SELF`
- `OP_CALL`
- `OP_TAILCALL`

其中 `OP_SELF` 的官方语义很明确：

- `R[A+1] := R[B]`
- `R[A] := R[B][K[C]:shortstring]`

也就是它一边取方法，一边把接收者对象放到下一格寄存器里，方便后续按“隐式 `self`”的方式发起调用。

## 本步骤范围

本轮先落这几件事：

- 在 VM 中支持 `SELF`
- 用真实 Lua 5.5 chunk 验证 `:` 语法生成的方法调用路径
- 把 `SELF + TAILCALL` 这条最小链路跑通
- 清理仓库里无用的根目录 `luac.out`

## 设计原则

### 1. 先支持方法调用快速路径

这一轮不去碰更复杂的对象语义，只先支持最常用的那条路径：

- 从表里按短字符串键取方法
- 把接收者对象放进 `A + 1`
- 让后续 `CALL` / `TAILCALL` 按当前固定参数协议继续执行

### 2. 先对齐编译器真实产物

这一轮不是手写一条假的 `SELF` 指令来测，而是直接使用真实 Lua 5.5 编译出的 chunk。

这样能同时验证：

- `SELF` 自己的寄存器行为
- 嵌套闭包与方法体的连接是否正确
- 当前调用协议是否足以承接对象方法调用

## 当前支持范围

这一轮新增支持：

- `SELF`
- 表方法的最小快速路径
- `SELF` 后续 `TAILCALL` 的最小执行闭环

当前还没有进入这些内容：

- `GETTABUP`
- `SETTABUP`
- 依赖 `_ENV` 的表访问路径
- 元方法参与的方法解析

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/self_chunk.lua`
- `test/fixtures/lua55/chunks/self_chunk.luac`

这份脚本覆盖的路径是：

- 创建表
- 把闭包挂到字段上
- 通过 `:` 语法发起方法调用
- 在方法体内部读取 `self.name`

## 实现清单

- [x] 编写本轮文档
- [x] 支持 `SELF`
- [x] 新增真实 `self_chunk.luac`
- [x] 新增 `SELF` 对应 VM 测试
- [x] 删除仓库根目录无用的 `luac.out`
- [x] 把 `luac.out` 加入忽略规则

## 完成标准

本轮完成后，应满足：

- `:` 语法生成的最小方法调用路径可执行
- 接收者对象可以正确落到参数寄存器里
- 方法体可以通过 `self` 读取字段
- 真实 fixture 可以跑出正确结果

## 下一步

接下来继续往下补：

- `_ENV` 与全局表访问路径已经拆到 `docs/009-step-05-global-environment.md`
- 更完整的调用协议
- 上值捕获
- 元方法调度
