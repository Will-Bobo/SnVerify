# Phase 2.5 – Step 1 Cursor Agent Self-Check Prompt

> 目标：在 Cursor Agent 执行 Phase 2.5 Step 1（模型 & 数据结构 & 单元测试）时，自行校验生成内容是否符合冻结约束。

---

## 自检规则（10 条）

### 1️⃣ 概念冻结遵守

* 确认代码中 **完全不存在** Batch / DefaultBatch / AutoBatch。
* 所有新模型仅使用：Project / Order / Session / TestRecord。

### 2️⃣ SessionId 生成集中且安全

* 确保 SessionId **只在一个地方生成**，禁止外部手写或拼接。
* SessionId 的生成函数是统一可控的。

### 3️⃣ Session 不可变约束

* Session 创建后 ProjectName / OrderName **不可修改**。
* 禁止出现 UpdateProject / UpdateOrder 等修改方法。

### 4️⃣ TestRecord 无业务决策权

* TestRecord 不包含 Project / Order / MES / Gate 状态。
* 所有业务判断都在 Domain Service 或 Gate 内完成。

### 5️⃣ Gate 是唯一外部流程影响入口

* MES 逻辑只通过 Gate 调用，禁止绕过。
* 禁止 UI 或 VerificationFlow 直接访问 MES 状态。

### 6️⃣ Post-Report 失败不影响结果

* MES Report 失败只产生事件或日志，不改 PASS / FAIL。
* 禁止修改 TestRecord 的结果。

### 7️⃣ 去重规则只在视图层或导出逻辑实现

* FAIL Sheet 去重仅在导出逻辑，不影响原始 TestRecord。
* PASS 不去重。

### 8️⃣ 防抖 / 重复点击判断完善

* Start / End 重复点击判断 **需结合时间窗口 + 是否生成 TestRecord**。
* 记录日志解释被忽略的原因。

### 9️⃣ 单元测试覆盖关键约束

* 测试覆盖 Session 不可变、Gate 阻断/放行、MES 上报失败、重复操作防护。
* 不仅覆盖 Happy Path，也覆盖异常/边界情况。

### 🔟 Step 1 无越权行为

* 禁止生成 UI / ViewModel / 文件 IO / 真正的 MES 实现。
* 如果留接口或 TODO，必须明确标记用途。

---

### 使用方法

1. Cursor Agent 在生成代码后，自动检查每条规则是否被满足。
2. 如果某条规则无法通过，应在代码中注释 TODO 或标记问题，不得私自绕过。
3. 所有规则通过后，Step 1 才算完成，可以进入 Step 2。
