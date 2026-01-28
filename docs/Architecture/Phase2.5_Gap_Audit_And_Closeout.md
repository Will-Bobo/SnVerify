# Phase 2.5 缺口盘点与收口说明

> 生成时间：Phase 2.5 四个阶段（A/B/C/D）执行完成后  
> 目的：明确"阶段计划已执行但 UI/VM 仍残留 Batch 语义"的缺口，并记录收口方案与完成度

---

## 一、阶段计划执行情况（对照 `Phase2.5_Stage_Plan.md`）

### 阶段 1(A)：模型与概念重构 ✅ 已完成

- **Order/TestSession/TestRecord 模型**：已落地（`Domain/Models/Order.cs`、`TestSession.cs`、`TestRecord.cs`）
- **SessionId 规则**：`SessionIdGenerator.Format(orderId, timestamp)` 已实现
- **按 Session 导出**：`IStorageService.ExportBySessionAsync` 已实现（PASS 不去重、FAIL 按 (StickerSN, DeviceSN) 去重）
- **命名校验**：`IOrderNameValidator` 已实现

### 阶段 2(B)：流程编排重构 ⚠️ 部分完成

- **Session 生命周期服务**：`ISessionLifecycleService` / `SessionLifecycleService` 已实现
- **Start/End 防抖**：End 前检查 TestRecord 的逻辑已实现（但仍在 `EndBatchAsync` 中，未切到 Session 语义）
- **自检与主流程互斥**：已实现（自检期间禁用 Start/End/扫码）
- **导出聚合服务**：`IExportAggregationService` 已实现（按 OrderId/ProjectId 聚合 Session 并导出）
- **MES 调用点预留**：`ProcessCoordinator` 内 Pre-Gate/Post-Report 调用点已预留

**缺口**：
- Start/End 按钮逻辑仍通过 `IBatchManager` / `BatchSnapshot` 挂接，未切到 `ISessionLifecycleService` / `SessionSnapshot`
- `VerificationFlowServiceFactory.Create(batchId)` 仍以 batchId 为参数，未改为 sessionId

### 阶段 3(C)：UI 行为与布局 ⚠️ 部分完成

- **PASS/FAIL 拟物化**：已实现
- **日志折叠**：已实现（默认折叠，最近 3000 条）
- **状态栏语义**：已更新为「当前订单」「处理中」「最近结果」
- **导出流程**：已实现「选维度→选对象→执行」+ 覆盖确认
- **校验弹窗**：已挂接 `IOrderNameValidator`
- **自检期间禁用**：已实现（扫码框、Start/End/Export 按钮禁用）

**缺口**：
- 顶部控制区仍为可编辑的"本次测试/Batch"输入框（`BatchNameInput`），**缺 ProjectId/OrderId 输入**
- 状态栏「当前订单」仍绑定 `CurrentBatchId`（来自 `BatchSnapshot.BatchId`），未绑定 `SessionSnapshot.OrderId`
- 无"本次测试标识"只读展示（SessionId 的时间段部分）

### 阶段 4(D)：MES 抽象与 Gate ✅ 已完成

- **MES 抽象接口**：`IMesPreCheck` / `IMesResultReporter` / `MesCapabilities` / `MesMode` 已实现
- **Pre-Gate/Post-Report 调用点**：已在 `ProcessCoordinator` 内实现
- **Post-Report 失败事件**：已实现（`MesEventOccurred` → ViewModel → `StatusBarMessage` 弱提示）
- **MesMode 收口**：已收口为 Disabled/Enabled（移除 Strict）

---

## 二、关键缺口汇总（需收口）

### 缺口 #1：UI 输入仍为 Batch，缺 Project/Order 输入

- **现状**：`MainWindow.xaml` 顶部控制区有可编辑的 `BatchNameInput` TextBox
- **应改为**：ProjectId 输入框 + OrderId 输入框 + 只读"本次测试标识"展示
- **影响文件**：`SnVerify/MainWindow.xaml`、`SnVerify/ViewModels/MainViewModel.cs`

### 缺口 #2：ViewModel 仍依赖 BatchManager/BatchSnapshot

- **现状**：`MainViewModel` 构造函数接受 `IBatchManager`，内部使用 `BatchSnapshot`
- **应改为**：接受 `ISessionLifecycleService`，内部使用 `SessionSnapshot`
- **影响文件**：`SnVerify/ViewModels/MainViewModel.cs`、`SnVerify/Infrastructure/ServiceFactory.cs`

### 缺口 #3：VerificationFlowServiceFactory 仍以 batchId 为参数

- **现状**：`IVerificationFlowServiceFactory.Create(string batchId)` 仍以 batchId 为参数
- **应改为**：`Create(string sessionId, string orderId = null)` 或类似语义
- **影响文件**：`SnVerify/Services/Coordination/IVerificationFlowServiceFactory.cs`、`SnVerify/Infrastructure/VerificationFlowServiceFactory.cs`

### 缺口 #4：日志服务仍使用 StartBatch/EndBatch 命名

- **现状**：`ILoggingService.StartBatch(string batchId)` / `EndBatch()` 仍使用 Batch 命名
- **处理策略**：最小改动方案（保留接口，但传入 sessionId）；彻底对齐方案（重命名为 StartRun/EndRun，后续重构）
- **影响文件**：`SnVerify/Services/Logging/ILoggingService.cs`、`SnVerify/Services/Logging/LoggingService.cs`（本次采用最小改动方案）

---

## 三、收口方案（已拍板）

### UI 输入改造

- **移除**：可编辑的"本次测试/Batch"输入框
- **新增**：
  - ProjectId 输入框（必填，按用户选择）
  - OrderId 输入框（必填）
  - 只读展示：本次测试标识（从 SessionId 中提取时间段，如 `yyyyMMdd_HHmmss`，不显示 Session 字样）

### ViewModel/Service 语义替换

- **Start 流程**：
  1. 校验 ProjectId/OrderId（复用 `IOrderNameValidator`）
  2. 调用 `ISessionLifecycleService.CreateAndStartSession(orderId, orderName, projectId)`，拿到 SessionId
  3. 调用 `IVerificationFlowServiceFactory.Create(sessionId, orderId)` 创建流程服务
  4. 调用 `ILoggingService.StartBatch(sessionId)`（最小改动方案：保留接口名，但传入 sessionId）

- **End 流程**：
  1. 检查当前 Session 是否有 TestRecord（无则忽略 + 状态栏提示）
  2. 调用 `ISessionLifecycleService.EndSession()`
  3. 调用 `ILoggingService.EndBatch()`

### 状态栏绑定

- **当前订单**：从 `CurrentBatchId`（`BatchSnapshot.BatchId`）改为 `CurrentOrderId`（`SessionSnapshot.OrderId`）

---

## 四、收口完成度追踪

| 缺口 | 状态 | 说明 |
|------|------|------|
| #1 UI 输入改造 | ✅ 已完成 | 已实现 ProjectId/OrderId 输入 + 只读测试标识（`CurrentTestIdentifier`） |
| #2 ViewModel 切 Session | ✅ 已完成 | 已将 `IBatchManager` 替换为 `ISessionLifecycleService`，`BatchSnapshot` 替换为 `SessionSnapshot`（保留向后兼容别名） |
| #3 FlowFactory 切 Session | ✅ 已完成 | 已将 `Create(batchId)` 改为 `Create(sessionId, orderId)` |
| #4 日志命名对齐 | ✅ 最小改动 | 保留 `StartBatch/EndBatch` 接口名，但传入 sessionId |

### 收口完成时间
- **完成时间**：Phase 2.5 收口阶段
- **主要变更文件**：
  - `SnVerify/ViewModels/MainViewModel.cs`：从 `IBatchManager` 切换到 `ISessionLifecycleService`
  - `SnVerify/MainWindow.xaml`：UI 输入改为 ProjectId/OrderId + 只读测试标识
  - `SnVerify/Infrastructure/ServiceFactory.cs`：创建 `SessionLifecycleService` 而非 `BatchManager`
  - `SnVerify/Services/Coordination/IVerificationFlowServiceFactory.cs`：接口改为 `Create(sessionId, orderId)`
  - `SnVerify/Infrastructure/VerificationFlowServiceFactory.cs`：实现改为接受 sessionId/orderId
  - `SnVerify.Tests/ViewModels/MainViewModelTests.cs`：测试用例更新为使用 `ISessionLifecycleService` 和 `SessionSnapshot`

### 向后兼容性
- **保留的别名**（Phase 2.5 过渡期）：
  - `BatchSnapshot` 作为 `SessionSnapshot` 的别名（标记为 `[Obsolete]`）
  - `CurrentBatchId` 作为 `CurrentOrderId` 的别名
  - `IsBatchActive` 作为 `IsSessionActive` 的别名
- **目的**：确保 XAML 绑定和现有代码在过渡期仍能工作，逐步迁移

---

## 五、后续建议（非本次收口范围）

- **日志接口重命名**：后续重构时将 `ILoggingService.StartBatch/EndBatch` 重命名为 `StartRun/EndRun` 或 `StartTest/EndTest`，彻底消除代码层面的 Batch 语义
- **BatchManager/BatchSnapshot 废弃**：待收口完成后，可考虑标记 `IBatchManager` / `BatchSnapshot` 为 `[Obsolete]`，逐步移除

---

**文档版本**  
*依据：Phase2.5_Stage_Plan.md、Phase2.5_Stage1/2/3/4_Execution_Result.md*
