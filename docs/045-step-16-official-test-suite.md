# Step 16：官方测试集接入（第一轮）

## 状态

这一轮把 Step 16 里“接入官方测试集”这条主线正式落地了第一版：

- 仓库内引入官方 `lua-5.5.0-tests`
- 新增 `Lua.Compatibility.Tests`
- 跑通一条真实官方脚本
- 把下一条官方脚本的当前阻塞点固定成诊断测试

这里还不是“整套官方测试集全部跑通”，但已经从“手工临时试跑”升级成了“可回归、可量化、能持续扩展”的基础设施。

## 背景与参考

这一轮主要参考这些官方材料：

- `https://www.lua.org/tests/`
- `test/fixtures/lua55/official/lua-5.5.0-tests/`
- `references/lua-5.5.0/doc/manual.html`

重点目标不是一口气跑完所有官方脚本，而是先把下面这几个问题解决掉：

- 官方测试材料怎么进入仓库
- 如何在自动化测试里执行官方脚本
- 当前第一批真实阻塞点是什么
- 后续新增兼容性修复后，如何把官方脚本逐步转成绿测

## 本步骤范围

这一轮落地这些能力：

- 仓库内新增官方 Lua 5.5.0 测试集夹具
- 新增 `test/Lua.Compatibility.Tests`
- 通过 `Lua.Cli` 执行官方脚本
- 接入 `bwcoercion.lua` 绿测
- 接入 `bitwise.lua` 诊断测试
- 顺手补齐官方脚本暴露出的三个兼容点：
  - 首行 `#` 注释
  - `global` / `global *` / `global function`
  - `0xF0.0` 这类十六进制数值字面量

这一轮明确**不做**：

- 整套 `all.lua` 跑通
- 官方测试集全量通过统计
- `vararg` 函数定义支持
- `for` 循环、`goto` / `label`、完整 variable attribute 支持
- 弱表、`__gc`、完整 traceback

## 设计

### 1. 官方测试集直接进仓库夹具

这一轮没有把官方测试集做成“测试时在线下载”，而是直接放进：

- `test/fixtures/lua55/official/lua-5.5.0-tests/`

原因很直接：

- 自动化测试不应依赖网络
- 官方测试应当固定到一个明确版本
- 调试失败时，需要能直接打开对应脚本定位

同时保留原始压缩包：

- `test/fixtures/lua55/official/lua-5.5.0-tests.tar.gz`

这样后续如果要核对来源或重新解包，材料是自描述的。

### 2. 兼容性测试单独建项目，不和单元测试混在一起

项目最早的测试策略文档就把“兼容性测试”列成单独一层。

这一轮按那个约定补了：

- `test/Lua.Compatibility.Tests`

边界是：

- 单元测试继续测局部语义
- fixture 测试继续测本仓库已有脚本/字节码夹具
- compatibility 测试专门跑官方 Lua 材料

这样后面逐步扩张官方脚本覆盖面时，不会把现有测试层次搅乱。

### 3. 先接“能跑的官方脚本”，再把失败点转成路线图

当前兼容性 harness 不是直接跑 `all.lua`。

原因是现阶段仍然存在几个编译器缺口，`all.lua` 只会在很早的位置失败，信息密度不够高。

所以这一轮先选了两条更有代表性的脚本：

- `bwcoercion.lua`
- `bitwise.lua`

它们的价值分别是：

- `bwcoercion.lua`：能验证 `global none`、标准库、字符串位运算元方法等真实组合路径
- `bitwise.lua`：能继续向前推进到下一层编译器缺口，而不是停在词法/数值解析

### 4. 诊断测试也算兼容性资产

这一轮里，`bitwise.lua` 还没有转绿。

当前它已经从：

- `malformed number`

推进到了：

- `vararg functions are not supported yet`

这说明这轮修复已经把更靠前的兼容层问题清掉了。

因此这里把它固定成一条“当前缺口诊断测试”，是有价值的：

- 如果以后错误又退回到词法层，测试会第一时间报警
- 如果以后实现了 vararg 函数定义，只需要把这条测试从“预期失败”改成“预期成功”

## 当前接入结果

这一轮兼容性项目当前覆盖：

- `bwcoercion.lua`：通过
- `bitwise.lua`：失败，当前阻塞点是 vararg 函数定义

另外，接官方脚本时顺手暴露并修掉了这些更前置的兼容问题：

- 首行 `#` 特殊注释没有被 lexer 跳过
- 编译器直接拒绝 `global` 相关声明
- `0xF0.0` 这类官方数值字面量被当成 malformed number

## 测试

这一轮新增测试覆盖：

- `Lua.Syntax.Tests`
  - 首行 `#` 注释
  - `0xF0.0` 数值字面量
- `Lua.Runtime.Tests`
  - `tonumber("0xF0.0")`
- `Lua.Compiler.Tests`
  - `global none`
  - `global<const> *`
  - `global function`
  - 全局初始化已定义错误
- `Lua.Compatibility.Tests`
  - `bwcoercion.lua` 绿测
  - `bitwise.lua` 诊断测试

## 实现清单

- [x] 引入官方 `lua-5.5.0-tests`
- [x] 新增 `Lua.Compatibility.Tests`
- [x] 接入官方脚本执行 helper
- [x] 跑通 `bwcoercion.lua`
- [x] 固定 `bitwise.lua` 当前缺口
- [x] 补齐首行 `#` 注释兼容
- [x] 补齐 `global` 基本语义
- [x] 补齐 `0xF0.0` 数值字面量兼容
- [x] 更新 README / 文档索引

## 完成标准

本轮完成后，应满足：

- 仓库内存在固定版本的官方测试集夹具
- 自动化测试可以直接执行官方 Lua 5.5.0 脚本
- 至少一条官方脚本已经转成稳定绿测
- 当前下一条官方脚本的真实阻塞点被量化并固定

## 延期内容

下一轮继续扩官方测试覆盖时，优先级建议是：

- vararg 函数定义
- `for` 循环
- `goto` / `label`
- 更完整的 variable attribute 支持
- `all.lua` 子集编排与通过率统计
