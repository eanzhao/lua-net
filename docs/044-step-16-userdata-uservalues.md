# Step 16：userdata 多关联值

## 状态

Step 16 先从一个边界清晰、对兼容性有直接收益的点切入：

- `debug.getuservalue`
- `debug.setuservalue`
- `LuaUserData` 的多槽位关联值模型

这一轮不宣称 Step 16 整体完成，只是把“多 userdata 关联值”这条子线真正落地。

## 背景与参考

这一轮主要参考这些官方资料：

- `references/lua-5.5.0/doc/manual.html`
- `references/lua-5.5.0/src/ldblib.c`
- `references/lua-5.5.0/src/lapi.c`

重点对齐的语义是：

- `debug.getuservalue(u, n)` 读取第 `n` 个关联值
- `debug.setuservalue(udata, value, n)` 写入第 `n` 个关联值
- user value 槽位是否“存在”和槽位里的值是否为 `nil` 是两件事

最后一点很关键。
在 Lua 5.5 里：

- 槽位存在但值是 `nil`，`debug.getuservalue` 应返回 `nil, true`
- 槽位不存在，才返回 `fail`

如果实现里只存一个“当前值”，就无法区分这两种情况。

## 本步骤范围

这一轮落地这些能力：

- `LuaUserData` 支持固定数量的 user value 槽位
- 槽位默认初始化为 `nil`
- `debug.getuservalue` 支持可选 `n` 参数
- `debug.setuservalue` 支持可选 `n` 参数
- 越界槽位返回 `fail`
- 新增单元测试覆盖默认槽位、多槽位和失败路径

这一轮明确**不做**：

- userdata 的 `__gc` 终结器
- 弱表与弱引用回收语义
- 更完整的 error level / traceback 行号信息
- 官方测试集接入
- 性能基线与 benchmark

## 设计

### 1. `LuaUserData` 改为“值 + 固定槽位数组”

之前的 `LuaUserData` 只有：

- `Value`
- `Metatable`

这不足以表达 Lua 5.5 里的多 user value 语义。

这一轮把它扩展成：

- `Value`：宿主对象本体
- `Metatable`：userdata 元表
- `UserValueCount`：固定槽位数量
- `TryGetUserValue(slot, out value)`：按 Lua 的 1-based 槽位访问
- `TrySetUserValue(slot, value)`：只允许写入已有槽位

这里故意没有做成“按需自动扩容”。

原因是官方语义里，userdata 的 user value 数量在创建时就已经固定；
访问不存在的槽位应该失败，而不是偷偷创建新槽位。

### 2. 槽位存在但值为 `nil` 仍然算“成功”

Lua 的 `debug.getuservalue` 不是“看值真不真”，而是“看槽位存不存在”。

所以这一轮的判断规则是：

- 槽位编号在范围内：返回该值和 `true`
- 槽位编号超界：返回 `fail`

这让下面两种情况可以被区分：

- `slot 1 = nil` -> `nil, true`
- `slot 2` 不存在 -> `fail`

### 3. `debug.setuservalue` 保持“写已有槽位，否则 fail”

官方 `lua_setiuservalue` 在槽位不存在时会返回失败。

因此这一轮 `debug.setuservalue` 的策略是：

- 第 1 个参数不是 userdata：报类型错误
- 第 2 个参数缺失：报缺参错误
- `n` 不是整数：报类型错误
- `n` 越界：返回 `fail`
- 写入成功：返回原 userdata

### 4. 内部辅助 userdata 显式使用 0 个 user value

运行时里有少量内部包装用的 userdata，例如 `io` 里挂在文件句柄表上的 reader/writer 包装。

这些对象不是给 Lua 层暴露 user value 语义的，因此这一轮把它们显式建成 `0` 槽位，
避免默认行为让内部对象看起来像是“天然自带一个 user value”。

## 测试

这一轮新增和补强的测试覆盖：

- `LuaUserData` 槽位默认值与越界访问
- `LuaUserData` 只允许写已有槽位
- `debug.getuservalue` 默认槽位返回 `nil, true`
- `debug.setuservalue` 写第 2 槽位
- 不存在的槽位返回 `fail`
- 非 userdata 输入返回 `fail`

## 实现清单

- [x] 扩展 `LuaUserData` 的 user value 存储模型
- [x] 修正 `debug.getuservalue`
- [x] 修正 `debug.setuservalue`
- [x] 调整内部 `io` 包装 userdata 的槽位数量
- [x] 新增运行时单元测试
- [x] 更新 README 文档索引与阶段状态

## 完成标准

本轮完成后，应满足：

- `LuaUserData` 可以表示多个固定 user value 槽位
- `debug.getuservalue` 能区分“槽位存在但值为 nil”和“槽位不存在”
- `debug.setuservalue` 不会隐式创建新槽位
- 自动化测试覆盖成功与失败路径

## 延期内容

Step 16 后续仍需继续补：

- 弱表（`__mode = "k"` / `"v"` / `"kv"`）
- `__gc` 终结器
- 更完整的 traceback / error level
- 官方测试集接入与差距量化
- 性能基线测量
