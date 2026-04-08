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
- 完成运行时值、栈、调用帧、状态对象的第一版骨架
- 建立 `Lua.Runtime.Tests`

当前主线测试结果：

- `dotnet test lua-net.sln`
- 13 个测试通过

## 仓库结构

当前仓库主要目录如下：

- `docs/`
  阶段规划和设计文档
- `references/lua-5.5.0/`
  官方 Lua 5.5.0 源码参考
- `src/Lua.Runtime/`
  当前主线运行时实现
- `test/Lua.Runtime.Tests/`
  当前主线运行时测试

后续会逐步扩展到这些模块：

- `Lua.Bytecode`
- `Lua.Syntax`
- `Lua.Compiler`
- `Lua.StandardLib`
- `Lua.Cli`

## 文档索引

当前已经落地的文档：

- [docs/001-roadmap.md](/Users/zhaoyiqi/Code/lua-net/docs/001-roadmap.md)
- [docs/002-step-01-foundation.md](/Users/zhaoyiqi/Code/lua-net/docs/002-step-01-foundation.md)
- [docs/003-step-01-source-reference.md](/Users/zhaoyiqi/Code/lua-net/docs/003-step-01-source-reference.md)
- [docs/004-step-02-runtime-model.md](/Users/zhaoyiqi/Code/lua-net/docs/004-step-02-runtime-model.md)

这些文档对应的是：

- 总路线图
- 第 1 步基础基线
- 官方源码参考策略
- 第 2 步运行时模型

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
- `LuaThread`
- `LuaUserData`
- `LuaStack`
- `CallFrame`
- `LuaState`

这些类型的目标不是一次做满，而是先为后续 VM、字节码、闭包、表和标准库提供统一的运行时承载结构。

## 下一步

下一步会进入第 3 步：

- 编写 `docs/005-step-03-bytecode-loader.md`
- 对齐 Lua 5.5 二进制块格式
- 读取 `proto`
- 建立指令元数据表
- 实现反汇编输出

## 参考资料

- [Lua 5.5 手册](https://www.lua.org/manual/5.5/)
- [Lua 5.5 发布说明](https://www.lua.org/manual/5.5/readme.html)
- [Lua 官方测试集](https://www.lua.org/tests/)
- [Lua 5.5 官方源码索引](https://www.lua.org/source/5.5/)
