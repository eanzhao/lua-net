# 第 7 步：基础库——tonumber / tostring

## 状态

已完成当前这一轮。

这一轮继续沿着基础库主线补最常用的转换函数，把 `tonumber` 和 `tostring` 接到了当前运行时主线上。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lbaselib.c`
- `references/lua-5.5.0/src/lauxlib.c`
- `references/lua-5.5.0/src/ltm.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮关注的核心点是：

- `luaB_tonumber`
- `luaB_tostring`
- `luaL_tolstring`
- `__tostring`
- `__name`

## 本步骤范围

本轮先落这几件事：

- 在 `_ENV` 中预置 `tonumber`
- 在 `_ENV` 中预置 `tostring`
- 让 `tonumber` 支持一参标准转换和二参进制转换
- 让 `tostring` 支持 `__tostring` 元方法
- 让 `tostring` 支持 table / userdata metatable 上的 `__name`
- 用真实 Lua 5.5 chunk 验证这组函数的 Lua 层行为

## 设计原则

### 1. `tonumber` 先对齐 Lua 层最常用的几条路径

这一轮没有只做“十进制字符串转数字”这种最窄实现，而是把 Lua 层最常见的几条路径一起补了：

- 已经是 number 的值原样返回
- 十进制整数和浮点字符串
- 十六进制整数与十六进制浮点字符串
- `tonumber(s, base)` 的进制整数转换

这样后面即使还没开始做更大的标准库，这组最常见的数值入口也已经能直接用了。

### 2. `tostring` 不走“只看 `LuaValue.ToString()`”的偷懒路径

Lua 里的 `tostring` 不只是简单打印类型名。

这一轮把最关键的两条语义补上了：

- 有 `__tostring` 时，先调用元方法
- 没有 `__tostring` 时，再走默认格式化

默认格式化也补上了 `__name`，所以 table / userdata 现在可以打印出更接近 Lua 的 `name: 0x...` 形式。

### 3. 转换函数继续复用当前运行时入口

这一轮没有让 `tostring` 单独绕开运行时，而是继续复用当前统一 callable 入口去调用 `__tostring`。

这样 table / userdata 上的 `__tostring` 和前面已经接好的最小 callable 路径是同一条链，后面继续补元方法时不用拆第二套逻辑。

## 当前支持范围

这一轮新增支持：

- `_ENV.tonumber`
- `_ENV.tostring`
- `tonumber(number)` 原样返回
- `tonumber(string)` 的十进制整数 / 浮点转换
- `tonumber(string)` 的十六进制整数 / 十六进制浮点转换
- `tonumber(string, base)` 的 2 到 36 进制整数转换
- `tostring` 的基础值格式化
- `tostring` 的 `__tostring` 元方法路径
- `tostring` 的 `__name` 类型名路径

当前这一轮还没有展开的是：

- 更完整的 `tostring` 地址格式细节
- 更完整的 traceback 与调试信息
- `next` / `pairs` / `ipairs`
- 更系统的 `Lua.StandardLib` 模块拆分

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/tonumber_chunk.lua`
- `test/fixtures/lua55/chunks/tonumber_chunk.luac`
- `test/fixtures/lua55/source/tostring_chunk.lua`
- `test/fixtures/lua55/chunks/tostring_chunk.luac`

它们分别覆盖：

- 十进制、十六进制、十六进制浮点和进制整数转换
- 转换失败时返回 `nil`
- `tostring(3.0)` 的浮点格式
- `__tostring` 返回自定义字符串
- `__name` 影响默认对象显示名
- 函数对象的默认格式化

## 实现清单

- [x] 编写本轮文档
- [x] 在 `_ENV` 中注册 `tonumber`
- [x] 在 `_ENV` 中注册 `tostring`
- [x] 为 `tonumber` 增加一参标准转换
- [x] 为 `tonumber` 增加二参进制转换
- [x] 为 `tostring` 增加 `__tostring` 元方法路径
- [x] 为 `tostring` 增加 `__name` 显示名路径
- [x] 新增对应运行时测试
- [x] 新增对应真实 fixture
- [x] 新增对应 VM 测试

## 完成标准

本轮完成后，应满足：

- Lua 层可以直接调用 `tonumber` / `tostring`
- 真实 Lua 5.5 chunk 可以覆盖常见转换路径
- `tostring` 已经能和当前元方法路径协作
- 十进制、十六进制和进制整数转换都有测试钉住

## 下一步

接下来继续往下补：

- `next`
- `pairs`
- `ipairs`
- 更完整的错误处理与 traceback
- 更系统的标准库模块拆分
