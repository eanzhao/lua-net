# 第 9 步：完整 `string` 库与模式匹配

## 状态

已完成当前这一轮。

这一轮把 Step 09 路线图里约定的 `string` 库主路径补齐了，包括：

- 基础字符串函数
- Lua 模式匹配主路径
- `string.format`
- `string.pack` / `string.packsize` / `string.unpack`

做完这一轮之后，真实 Lua 5.5 chunk 已经可以直接通过标准库跑通常见字符串处理、模式搜索替换和二进制打包解包路径。

## 背景与参考

这一轮主要参考这些官方文件：

- `references/lua-5.5.0/src/lstrlib.c`
- Lua 5.5 手册：<https://www.lua.org/manual/5.5/>

这一轮重点对齐的点是：

- `str_byte` / `str_char` / `str_rep` / `str_reverse` / `str_sub`
- `str_find` / `str_match` / `gmatch` / `str_gsub`
- `str_format`
- `str_pack` / `str_packsize` / `str_unpack`

## 本步骤范围

本轮落地这些能力：

- 实现 `string.byte` / `string.char` / `string.rep` / `string.reverse` / `string.sub`
- 补齐 `string.find` / `string.match` / `string.gmatch` / `string.gsub`
- 支持 Lua 模式里的字符类、锚点、捕获、位置捕获、平衡匹配和贪婪/非贪婪重复
- 实现 `string.format`
- 实现 `string.pack` / `string.packsize` / `string.unpack`
- 用运行时测试和真实 Lua 5.5 fixture 同时验证字符串库

## 设计原则

### 1. 在不重写 `LuaValue` 的前提下补一层 raw byte 语义

当前 runtime 的字符串载体仍然是 C# `string`，而 Lua 字符串本质上是字节串。

这一轮没有直接把 `LuaValueKind.String` 重构成新的底层表示，而是增加了一层 raw byte 映射：

- 来自源码和普通文本路径的字符串，默认继续按 UTF-8 取字节视图
- 来自 `string.char` / `string.pack` / 字节级切片与拼接路径的新字符串，会附带原始字节映射
- `string.byte` / `string.sub` / `string.unpack` / VM `LEN` / `CONCAT` / `rawlen` 都统一走这层字节视图

这样可以在不掀翻现有值模型的前提下，把 Step 09 需要的 byte-level 语义补到可用。

### 2. 模式匹配引擎按 Lua byte pattern 实现，不复用 .NET 正则

Lua 模式和 .NET 正则语义差异很大，尤其是：

- 位置捕获
- 平衡匹配 `%bxy`
- Lua 风格的最小匹配 `-`

这一轮直接实现了最小 Lua pattern 引擎，输入和输出都按字节位置工作，避免语义漂移。

### 3. `string.format` 与 `pack/unpack` 都直接输出 Lua 字节串

这一轮的 `string.format` 没有直接依赖 `string.Format` 拼接最终结果，而是统一往字节缓冲区写：

- `%s` 会保留原始字节串内容
- `%c` 可以直接产出单字节
- `%q` 会输出可回读的 Lua 字面量

`pack/unpack` 同样直接围绕字节缓冲区实现。

### 4. native size 先按当前 64 位宿主布局对齐

`string.pack` / `unpack` 里的 native 选项依赖宿主 ABI。

当前实现按项目当前运行环境采用这组布局：

- `short = 2`
- `int = 4`
- `long = 8`
- `size_t = 8`
- `lua_Integer = 8`
- `lua_Number = 8`

这和当前 macOS arm64 / 64 位开发环境一致，也足够覆盖项目当前 fixture。

## 当前支持范围

这一轮新增支持：

- `string.byte`
- `string.char`
- `string.find`
- `string.match`
- `string.gmatch`
- `string.gsub`
- `string.format`
- `string.pack`
- `string.packsize`
- `string.rep`
- `string.reverse`
- `string.sub`
- `string.unpack`
- `rawlen` 对 raw byte string 的正确长度语义
- VM `LEN` / `CONCAT` 对 raw byte string 的正确长度与拼接语义

模式匹配路径当前覆盖：

- 字面量与 `.`
- `%a` / `%d` / `%w` / `%s` / `%p` / `%x` / `%z` 等字符类及其大写反类
- `[]` / `[^]` 集合
- `*` / `+` / `?` / `-`
- `^` / `$`
- `()` 捕获
- `()` 位置捕获
- `%bxy`
- `%1` 到 `%9` 捕获引用

## 真实 fixture

这一轮新增 fixture：

- `test/fixtures/lua55/source/string_library_chunk.lua`
- `test/fixtures/lua55/chunks/string_library_chunk.luac`

它覆盖：

- `byte` / `char` / `rep` / `reverse` / `sub`
- `find` / `match` / `gmatch` / `gsub`
- 位置捕获与平衡匹配
- `format`
- `pack` / `packsize` / `unpack`

## 实现清单

- [x] 编写本轮文档
- [x] 为 runtime 增加 raw byte string 辅助层
- [x] 为 `string` 表注册完整 Step 09 主路径函数
- [x] 实现 Lua pattern 主引擎
- [x] 实现 `string.format`
- [x] 实现 `string.pack` / `string.packsize` / `string.unpack`
- [x] 修正 `rawlen` / VM `LEN` / `CONCAT` 的字节语义
- [x] 新增运行时测试
- [x] 新增真实 fixture
- [x] 新增 VM fixture 测试

## 完成标准

本轮完成后，应满足：

- 真实 Lua 5.5 chunk 可以直接使用 `string` 库主路径
- 模式匹配的主路径已经可用
- 二进制 pack/unpack 已有最小可用实现
- byte-level 字符串语义已经贯通 runtime 和 VM

## 下一步

Step 09 到这里收口。

接下来进入：

- Step 10：`coroutine` 库
