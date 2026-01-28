# Phase 2.5 收口总结

> 生成时间：Phase 2.5 四个阶段（A/B/C/D）执行完成后的收口阶段  
> 目的：记录 Phase 2.5 最终完成度与 Batch → Session 语义切换的落地情况

---

## 一、收口前状态（缺口）

### UI 层面
- **现象**：UI 仍有可编辑的"本次测试/Batch"输入框，看不到 Project/Order 输入
- **根因**：`MainWindow.xaml` 仍绑定 `BatchNameInput`，`MainViewModel` 仍依赖 `IBatchManager/BatchSnapshot`

### 服务层面
- Start/End 按钮逻辑仍通过 `IBatchManager` 挂接，未切到 `ISessionLifecycleService`
- `VerificationFlowServiceFactory.Create(batchId)` 仍以 batchId 为参数

---

## 二、收口完成情况

### ✅ UI 输入改造（缺口 #1）

**变更**：
- **移除**：可编辑的"本次测试/Batch"输入框（`BatchNameInput`）
- **新增**：
  - ProjectId 输入框（`ProjectIdInput`）
  - OrderId 输入框（`OrderIdInput`，必填）
  - 只读展示：本次测试标识（`CurrentTestIdentifier`，从 SessionId 提取时间段，如 `yyyyMMdd_HHmmss`）

**文件**：
- `SnVerify/MainWindow.xaml`：顶部控制区 UI 布局
- `SnVerify/ViewModels/MainViewModel.cs`：新增 `ProjectIdInput`、`OrderIdInput`、`CurrentTestIdentifier` 属性

### ✅ ViewModel/Service 语义替换（缺口 #2）

**变更**：
- `MainViewModel` 构造函数：从 `IBatchManager` 改为 `ISessionLifecycleService`
- 内部状态：从 `BatchSnapshot` 改为 `SessionSnapshot`
- Start 流程：
  1. 校验 ProjectId/OrderId（复用 `IOrderNameValidator`）
  2. 调用 `ISessionLifecycleService.CreateAndStartSession(orderId, orderName, projectId)`，拿到 SessionId
  3. 调用 `IVerificationFlowServiceFactory.Create(sessionId, orderId)` 创建流程服务
  4. 调用 `ILoggingService.StartBatch(sessionId)`（最小改动方案：保留接口名，但传入 sessionId）
- End 流程：
  1. 检查当前 Session 是否有 TestRecord（无则忽略 + 状态栏提示「本次操作无效/已忽略」）
  2. 调用 `ISessionLifecycleService.EndSession()`
  3. 调用 `ILoggingService.EndBatch()`

**文件**：
- `SnVerify/ViewModels/MainViewModel.cs`：字段、属性、Start/End 方法重构
- `SnVerify/Infrastructure/ServiceFactory.cs`：创建 `SessionLifecycleService` 而非 `BatchManager`

### ✅ VerificationFlowServiceFactory 切 Session（缺口 #3）

**变更**：
- `IVerificationFlowServiceFactory.Create(string batchId)` → `Create(string sessionId, string orderId = null)`
- `VerificationFlowServiceFactory` 实现：使用 `sessionId` 和 `orderId` 构造 `ProcessCoordinator`

**文件**：
- `SnVerify/Services/Coordination/IVerificationFlowServiceFactory.cs`
- `SnVerify/Infrastructure/VerificationFlowServiceFactory.cs`

### ✅ 日志命名对齐（缺口 #4）

**策略**：最小改动方案
- 保留 `ILoggingService.StartBatch/EndBatch` 接口名
- 但传入 `sessionId`（而非 batchId）
- 彻底对齐方案（重命名为 `StartRun/EndRun`）留待后续重构

---

## 三、向后兼容性（过渡期）

为减少迁移风险，保留以下别名（标记为 `[Obsolete]`）：

- `BatchSnapshot` → `SessionSnapshot` 的别名（通过属性转换）
- `CurrentBatchId` → `CurrentOrderId` 的别名
- `IsBatchActive` → `IsSessionActive` 的别名

**目的**：确保 XAML 绑定和现有代码在过渡期仍能工作，逐步迁移。

---

## 四、单元测试更新

**变更**：
- `SnVerify.Tests/ViewModels/MainViewModelTests.cs`：
  - Mock 从 `IBatchManager` 改为 `ISessionLifecycleService`
  - 测试用例更新为使用 `SessionSnapshot` 而非 `BatchSnapshot`
  - 新增测试：`StartBatchCommand_ShouldCreateSession_WhenProjectIdAndOrderIdProvided`
  - 更新测试：`EndBatchCommand_ShouldBeIgnored_WhenNoTestRecordGenerated_AndShowStatusBarMessage`（使用 SessionId）
  - 更新测试：`Commands_ShouldBeDisabled_WhenSessionIsActive`

---

## 五、状态栏绑定更新

**变更**：
- 状态栏「当前订单」从 `CurrentBatchId`（`BatchSnapshot.BatchId`）改为 `CurrentOrderId`（`SessionSnapshot.OrderId`）

**文件**：
- `SnVerify/MainWindow.xaml`：StatusBar 绑定

---

## 六、最终完成度

| 阶段 | 计划完成度 | 实际完成度 | 说明 |
|------|------------|------------|------|
| **阶段 1(A)** | ✅ 100% | ✅ 100% | Order/TestSession/TestRecord 模型、按 Session 导出、命名校验 |
| **阶段 2(B)** | ⚠️ 80% | ✅ 100% | Session 生命周期、Start/End 防抖、自检互斥、导出聚合、MES 调用点预留 → **收口后：Start/End 已切到 SessionLifecycleService** |
| **阶段 3(C)** | ⚠️ 90% | ✅ 100% | UI 行为与布局、导出流程、校验弹窗、自检禁用 → **收口后：UI 输入已切到 ProjectId/OrderId** |
| **阶段 4(D)** | ✅ 100% | ✅ 100% | MES 抽象接口、Pre-Gate/Post-Report、Post-Report 失败事件 |

---

## 七、后续建议（非本次收口范围）

1. **日志接口重命名**：后续重构时将 `ILoggingService.StartBatch/EndBatch` 重命名为 `StartRun/EndRun` 或 `StartTest/EndTest`，彻底消除代码层面的 Batch 语义
2. **BatchManager/BatchSnapshot 废弃**：待收口完成后，可考虑标记 `IBatchManager` / `BatchSnapshot` 为 `[Obsolete]`，逐步移除
3. **移除向后兼容别名**：待所有 XAML 绑定和代码迁移完成后，移除 `BatchSnapshot`、`CurrentBatchId`、`IsBatchActive` 等别名

---

**文档版本**  
*依据：Phase2.5_Stage_Plan.md、Phase2.5_Gap_Audit_And_Closeout.md、Phase2.5_Stage1/2/3/4_Execution_Result.md*
