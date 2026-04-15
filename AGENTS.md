# AGENTS.md

## 项目约定

- 所有文档使用中文。
- 所有代码注释使用英文。

## 代码组织约定

- `LuaVirtualMachine` 按职责拆分为 partial class，新增 opcode 或功能时放到对应的文件里：
  - `LuaVirtualMachine.cs` — 核心执行循环和公共 API
  - `LuaVirtualMachine.Arithmetic.cs` — 算术、位运算、拼接、长度
  - `LuaVirtualMachine.Comparison.cs` — 相等性、有序比较、条件跳转
  - `LuaVirtualMachine.TableAccess.cs` — 表读写、SETLIST、NEWTABLE、SELF
  - `LuaVirtualMachine.Metamethods.cs` — 元方法解析与分发
  - `LuaVirtualMachine.ControlFlow.cs` — 循环、vararg、泛型 for、跳转
  - `LuaVirtualMachine.Helpers.cs` — 寄存器、上值、常量、资源清理

- 跨模块共享的值操作逻辑放 `LuaValueHelper`，不要在 `LuaState` 和 `LuaVirtualMachine` 里重复实现。

- 元表相关的对象类型实现 `IMetatableOwner` 接口，通过 `MetatableOwnerExtensions.TryGetMetamethod` 统一查找。

- `LuaState` 里的 native 函数遵循 `LuaNativeFunction` 委托签名，不需要 `_ = state; _ = closure;` 丢弃模式。
