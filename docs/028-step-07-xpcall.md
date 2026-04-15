# 第 7 步：基础库——xpcall

## 状态

已完成当前这一轮。

这一轮继续沿着基础库主线补受保护调用，把 `xpcall` 接到了当前运行时和 VM 主线上。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/ldo.c`
- `references/lua-5.5.0/src/lvm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaB_xpcall`
- `finishpcall`
- message handler 的错误收敛

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `xpcall`
- 让 `xpcall` 复用当前统一 callable 调用入口
- 让 `xpcall` 支持额外参数透传
- 让 `xpcall` 在失败时通过 message handler 转换错误对象
- 让 message handler 自己报错时返回 `"error in error handling"`
- 用真实 Lua 5.5 chunk 验证 `xpcall` 的 Lua 层行为

## 设计原则

### 1. `xpcall` 和 `pcall` 共用同一套受保护调用骨架

这一轮没有再单独复制一套 `xpcall` 调用逻辑，而是把它和 `pcall` 一起收进统一辅助逻辑里。

这样做有两个好处：

- 成功路径和失败路径的返回协议不会分叉
- 后面继续补 `xpcall` 相关细节时，不需要改两套代码

### 2. message handler 只接收一个错误对象

Lua 里 `xpcall` 的 message handler 不是普通的“失败回调”，而是拿一个错误对象，返回一个新的错误对象。

这一轮先把最关键的 Lua 层可见语义钉住：

- 成功时返回 `true, ...`
- 失败时返回 `false, handledError`
- handler 返回多个值时，只取第一个

### 3. handler 自己再报错时收成固定结果

这一轮专门把一个容易写偏的地方钉住了：

- 如果 message handler 自己再报错，最终结果不是它的新错误对象
- 当前对齐到官方 Lua 5.5 的 Lua 层结果：`false, "error in error handling"`

## 当前支持范围

这一轮新增支持：

- `_ENV.xpcall`
- `xpcall(f, msgh, ...)` 的额外参数透传
- `xpcall` 成功时的多返回值透传
- `xpcall` 失败时的 message handler 转换
- message handler 自己报错时的固定错误结果
- `xpcall` 复用当前统一 callable 解析路径

当前这一轮还没有展开的是：

- `tonumber` / `tostring`（已拆到 `docs/029-step-07-number-string-conversion.md`）
- `next` / `pairs` / `ipairs`（已拆到 `docs/030-step-07-table-iteration-functions.md`）
- 更完整的 traceback 与调试信息
- 更系统的标准库模块拆分

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/xpcall_chunk.lua`
- `test/fixtures/lua55/chunks/xpcall_chunk.luac`

它覆盖：

- `xpcall` 的成功路径
- `xpcall` 对额外参数的透传
- `xpcall` 对 userdata `__call` 路径的复用
- `xpcall` 对 `assert(false, "...")` 错误对象的转换
- message handler 自己报错时的固定结果

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `xpcall`
- [x] 把 `pcall` / `xpcall` 收到统一辅助逻辑中
- [x] 为 `xpcall` 增加 message handler 处理
- [x] 为 message handler 自身报错增加固定收敛结果
- [x] 新增对应运行时测试
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用 `xpcall`
- `xpcall` 在真实 Lua 5.5 chunk 中可用
- `xpcall` 可以复用当前普通函数和最小 `__call` 路径
- message handler 的成功与失败结果都已经被测试钉住

## 下一步

接下来继续往下补：

- `tonumber` / `tostring`
- `next` / `pairs` / `ipairs`
- 更完整的错误处理与 traceback
- 更系统的标准库模块拆分
