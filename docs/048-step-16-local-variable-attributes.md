# Step 16：local variable attributes 与 `locals.lua` 兼容推进

## 状态

这一轮把 `locals.lua` 最前面的 local variable attributes 缺口补上了：

- 源码编译器现在支持局部变量 `<const>` / `<close>`
- `for` 循环控制变量按只读局部变量处理
- 块作用域结束和 `break` 离开循环时，会对源码里的 to-be-closed 局部变量发出正确的 `CLOSE`

兼容性状态也因此继续前推：

- `bwcoercion.lua`：通过
- `bitwise.lua`：通过
- `locals.lua`：继续推进，当前新的阻塞点是 `goto` / `label`

当前 `locals.lua` 的真实报错已经从：

- `locals.lua:187:10: variable attributes are not supported yet`

推进到：

- `locals.lua:1214:26: goto and labels are not supported yet`

## 背景与参考

这一轮主要参考：

- `test/fixtures/lua55/official/lua-5.5.0-tests/locals.lua`
- `src/Lua.Syntax/Parsing/LuaParser.cs`
- `src/Lua.Compiler/LuaCompiler.cs`
- `src/Lua.VM/LuaVirtualMachine.cs`
- `src/Lua.VM/LuaVirtualMachine.Helpers.cs`
- `references/lua-5.5.0/src/lparser.c`
- `references/lua-5.5.0/src/lcode.c`

Parser 其实早就已经支持 attribute 语法，并且会拦住：

- `local <close> a, b`
- `local a<close>, b<close>`

这类“同一声明里多个 to-be-closed 变量”的非法写法。

真正缺的是后半段：

- 编译器没有把 `<const>` 变成只读局部变量
- 编译器没有为 `<close>` 发出 `TBC`
- 作用域退出时没有按源码级块边界补 `CLOSE`
- `break` 离开循环时也没有源码级关闭路径

所以 `locals.lua` 在语法过了以后，立刻会卡在编译阶段。

## 本步骤范围

这一轮落地这些内容：

- 局部变量 `<const>` 编译支持
- 局部变量 `<close>` 编译支持
- 捕获 const 上值后的只读赋值检查
- 作用域结束时的源码级 `CLOSE` 发射
- `break` 离开循环时的源码级 `CLOSE` 发射
- 数值 / 泛型 `for` 控制变量只读化
- `locals.lua` 诊断推进到下一个真实缺口

这一轮明确不做：

- `goto` / `label`
- `locals.lua` 全量转绿
- 非 closable 值的官方错误文案完全对齐
- `all.lua` 全量兼容率统计

## 设计

### 1. 局部变量元数据要沿词法解析一路带下去

之前 `TryResolveLexicalName` 只返回：

- 它是 local 还是 upvalue
- 它对应哪个索引

这对普通赋值够用，但对 `<const>` 不够，因为：

- 直接给局部变量赋值要拦
- 给捕获到的 const 上值赋值也要拦
- `function foo() end` 这种语法糖最终也是一次赋值，同样要拦

所以这一轮把只读信息并入了词法引用元数据：

- `LocalInfo` 记录局部变量是否只读、是否 to-be-closed
- `UpvalueInfo` 保留被捕获名字的只读属性
- `Reference` / `CaptureSource` 也带上 `IsReadOnly`

这样 `CreateNameAssignmentTarget` 在命中 local/upvalue 后，就能统一报出：

- `attempt to assign to const variable 'name'`

而不需要区分“当前作用域局部变量”和“父作用域捕获变量”。

### 2. `<close>` 的关键不是语法，而是指令落点

对源码局部变量来说，`<close>` 真正要做的是两件事：

1. 变量初始化完成后发出 `TBC`
2. 变量离开源码作用域时发出 `CLOSE`

这两个时点都不能错。

因此这一轮的局部声明编译逻辑改成：

- 先算右值
- 再把值写入局部寄存器
- 最后只对带 `<close>` 的那个局部发出 `TBC`

这样可以保证：

- `local x <close> = nil`
- `local x <close> = false`

这种情况仍然合法，而运行时会沿用现有 `Tbc` 的 nil/false 快速路径，直接跳过登记。

### 3. 作用域退出要按“本作用域最早 closable 寄存器”发 `CLOSE`

Lua 5.5 的 `CLOSE A` 不是“关闭一个变量”，而是：

- 关闭所有寄存器索引 `>= A` 的 to-be-closed 资源
- 同时关闭对应范围内的 open upvalue

所以源码级块作用域退出时，不需要为每个变量都单独发一条 `CLOSE`。

这一轮给编译器增加了 scope 元数据：

- 当前作用域的 local 起始位置
- 当前作用域的 global declaration 起始位置
- 当前作用域里最早的 closable 局部寄存器

离开块作用域时，如果这个寄存器存在，就发出：

- `CLOSE firstClosableRegister`

这样既能覆盖一个作用域里的多个 `<close>` 声明，也能和运行时现有的降序关闭逻辑自然对齐。

### 4. `break` 不能只跳转，必须先补源码级关闭

`break` 的问题在于：

- 它会直接跳出当前循环
- 被跳过的那些块尾 `CLOSE` 根本不会执行

如果循环体或其内层块里有 `<close>` 局部变量，直接 patch 一个 `JMP` 会泄漏资源。

这轮做法是：

- loop context 记录“循环体作用域”在作用域栈里的位置
- 编译 `break` 时，扫描从当前作用域到循环体作用域之间所有 scope
- 找到这些 scope 里最早的 closable 寄存器
- 先发 `CLOSE`
- 再发跳转占位，最后 patch 到循环出口

这样：

- `while` / `repeat` / 数值 `for` 的源码局部资源
- 泛型 `for` 循环体里的源码局部资源

都能在 `break` 路径上被正确关闭。

泛型 `for` 自己的隐藏 close state 仍然走已有的 loop 尾部 `CLOSE iteratorRegister`，两条路径互不冲突。

### 5. `for` 控制变量本身也要按只读局部变量处理

既然这轮已经把只读局部变量元数据打通，就顺手把 `for` 控制变量也改成了只读绑定：

- 数值 `for` 的循环变量
- 泛型 `for` 的每个可见控制变量

这样后续官方脚本里对：

- `i = ...`
- `v = ...`

这类写法的只读检查，也能直接复用同一条编译期规则。

## 当前结果

compatibility harness 当前状态：

- `bwcoercion.lua`：通过
- `bitwise.lua`：通过
- `locals.lua`：失败，当前错误是 `goto and labels are not supported yet`

这说明 local variable attributes 这一层已经被真正越过去了，当前新缺口是后半段控制流特性，而不是 attribute 本身。

## 测试

这一轮新增或更新了这些覆盖：

- `Lua.Compiler.Tests`
  - 拒绝给 const local 赋值
  - 拒绝在嵌套函数里给捕获的 const local 赋值
  - `for` 控制变量是只读的
  - 块作用域结束会关闭 `<close>` 局部变量
  - `break` 离开循环时会关闭 `<close>` 局部变量
  - `repeat ... until` 条件可以读取块内局部变量
  - `repeat ... until` 继续下一轮时会关闭 `<close>` 局部变量
- `Lua.Compatibility.Tests`
  - `locals.lua` 诊断失败点从 variable attributes 推进到 `goto` / `label`

## 实现清单

- [x] 局部变量 attribute 元数据建模
- [x] const local / const upvalue 编译期赋值检查
- [x] `<close>` 局部变量的 `TBC` 发射
- [x] 作用域退出时的 `CLOSE` 发射
- [x] `break` 的源码级关闭路径
- [x] `for` 控制变量只读化
- [x] `locals.lua` 缺口推进到下一阶段

## 下一步

下一步应该直接补：

- `goto`
- `label`
- 以及它们和 `<close>` / `CLOSE` / 作用域边界之间的交互规则

因为 `locals.lua` 现在已经不再被 attribute 挡住，而是明确进入了控制流兼容性的下一层。 
