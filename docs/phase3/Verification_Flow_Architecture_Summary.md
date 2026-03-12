# 检验流程架构与数据流说明（人工审核用）

> 本文档对当前 SnVerify 的**检验流程**做集中说明，包括架构分层、调用链、数据流方向与关键决策点，便于人工审核与理解。  
> 与 `07_Technical_Architecture_and_Dev_Guide.md`、`DeviceAccess_Subsystem_Architecture_v1.md` 等配合使用。

---

## 1. 总体架构（检验相关）

### 1.1 分层与职责

```
┌─────────────────────────────────────────────────────────────────┐
│  View (MainWindow / Dialogs)                                     │
│  仅布局、绑定、事件转发，无业务逻辑                                 │
└────────────────────────────┬────────────────────────────────────┘
                             │ 绑定 / Command
┌────────────────────────────▼────────────────────────────────────┐
│  ViewModel (MainViewModel)                                       │
│  路由：根据产品模式选 Legacy / Phase3 入口；轮询 Snapshot 更新 UI   │
└────────────────────────────┬────────────────────────────────────┘
                             │ 调用 Service 接口
┌────────────────────────────▼────────────────────────────────────┐
│  Service 层（协调 + 规则 + 设备）                                 │
│  • IVerificationFlowServiceFactory → 按 Session 创建流程服务       │
│  • IVerificationFlowService → 门面，委托 ProcessCoordinator       │
│  • IProcessCoordinator → 流程编排、落库、Snapshot、MES 挂载         │
│  • IRulePipelineExecutor → 规则链（仅 Phase3）、Fail Fast          │
│  • IDeviceAccessService → 设备信息读取（Phase3，配置驱动）          │
│  • IAdbAccessService → Legacy 设备 SN 读取                        │
│  • IStorageService / IParameterService / IVersionVerificationService │
└────────────────────────────┬────────────────────────────────────┘
                             │ 实现与基础设施
┌────────────────────────────▼────────────────────────────────────┐
│  Infrastructure                                                │
│  • VerificationFlowServiceFactory → 创建 ProcessCoordinator +   │
│    VerificationFlowService（注入 RulePipelineExecutor 等）        │
│  • DeviceAccess：DeviceSessionManager / AdbDeviceService /       │
│    DeviceCommandExecutor / ParserFactory                        │
│  • AdbAccessService / StorageService / ProductRegistry          │
└─────────────────────────────────────────────────────────────────┘
```

- **业务规则与设备协议解耦**：规则链（RulePipelineExecutor）只依赖 `IDeviceAccessService` 与 `DeviceInfo`，不关心 ADB 细节；设备行为由 `ProductProfile.AdbConfig` 配置驱动。
- **单一事实源**：产品配置唯一来自 `ProductProfile`（ProductRegistry）；检验结果唯一落库到 `TestRecord`（StorageService）。

### 1.2 双路径：Legacy 与 Phase3

| 维度       | Legacy（如 SOLTAG25）           | Phase3（如 KM001）                          |
|------------|----------------------------------|---------------------------------------------|
| 触发条件   | 非 Phase3 产品 或 未选产品码     | `IsPhase3Product && SelectedProductCode`   |
| 入口       | `StartVerificationAsync(sn)`     | `StartPhase3VerificationAsync(sn, projectId)` |
| 设备读取   | `IAdbAccessService`（ylzero + getprop） | `IDeviceAccessService.ReadDeviceInfoAsync(profile)` |
| 规则       | ProcessCoordinator 内决策树       | `IRulePipelineExecutor.ExecuteAsync`       |
| 落库与 MES | 均由 ProcessCoordinator 统一调度 | 同上                                        |

---

## 2. 调用流程（按时间顺序）

### 2.1 批次开始（开始测试）

1. 用户输入项目 ID、订单 ID，点击「开始测试」。
2. **MainViewModel** 调用 `_sessionLifecycleService.CreateAndStartSession(orderId, orderId, projectId, productCode)`  
   → 创建/复用 Order、TestSession，返回 `sessionId`。
3. **MainViewModel** 调用 `_flowServiceFactory.Create(sessionId, orderId)`  
   → **VerificationFlowServiceFactory** 创建新的 **ProcessCoordinator**（注入 sessionId、orderId、Storage、Adb、RulePipelineExecutor、ProductRegistry 等）和 **VerificationFlowService**（包装该 Coordinator）。
4. ViewModel 持有 `_verificationFlowService`，订阅 MES 事件；此后该 Session 下所有扫码都走这一个 Coordinator。

**数据流**：UI 输入（项目/订单）→ SessionLifecycleService（持久化 Session）→ Factory 产出 ProcessCoordinator + VerificationFlowService → ViewModel 持有。

### 2.2 单次扫码（Phase3 路径）

1. 用户扫码，**MainViewModel** 收到输入（如带 `\r` 的一行 SN），校验 `IsSessionActive`、非 `IsProcessing` 后调用 `HandleScanInputAsync(sn)`。
2. ViewModel 根据 `IsPhase3Product && SelectedProductCode` 调用  
   `_verificationFlowService.StartPhase3VerificationAsync(trimmedSn, SelectedProductCode)`。
3. **VerificationFlowService** 委托 **ProcessCoordinator.ProcessScanAsync(sn, projectId)**。
4. **ProcessCoordinator**：
   - 加锁，若未在处理则置 `Snapshot = Processing(sn)`；
   - 按 `_sessionId` 取 Session → Order → Product 名，再 **IParameterService.GetParameterAsync(productName)** 得到版本参数 `parameter`；
   - **IProductRegistry.GetProductProfile(projectId)** 得到 **ProductProfile**；
   - 调用 **IRulePipelineExecutor.ExecuteAsync(productProfile, deviceInfo: null, parameter, stickerSn: sn, orderId: _orderId)**（设备信息由执行器内部读取）；
   - 根据返回的 **RuleExecutionResult** 调用 **SavePhase3ResultAsync** 落库，并 **UpdateSnapshot(Completed(...))**。
5. **RulePipelineExecutor.ExecuteAsync** 内部：
   - ① Parameter 空 → 直接返回 `Fail("PARAMETER_NOT_CONFIGURED")`；
   - ② **deviceInfo 为 null** → 调用 **IDeviceAccessService.ReadDeviceInfoAsync(profile)** 读取设备信息；
   - ③ 后续按顺序：DeviceSn 非空、StickerSN 与 DeviceSN 物理匹配、SN 订单内不重复、ChipId 格式（F50 开头）、ChipId 订单内唯一、**IVersionVerificationService.VerifyAsync** 三版本校验；任一步 Fail 即返回，否则 **Pass(di)**。
6. **IDeviceAccessService** 实现为 **AdbDeviceService**：
   - 调用 **DeviceSessionManager.EnsureSessionReadyAsync(profile.AdbConfig)**：  
     - 若未 Warmup 则执行一次 Shell warmup（`shell exit`）；  
     - **每次**都执行 **BootstrapCommandSpecs**（如 `shell ylzero`，可配置 AcceptableExitCodes、TimeoutBehavior）；
   - 再按 **AdbConfig** 执行 Aggregate 或 Field 命令（如 `getprop ro.serialno` 等），经 **ParserFactory** 解析得到 **DeviceInfo**（DeviceSn、ChipId、AndroidVersion 等）。
7. **ProcessCoordinator** 将 RulePipelineExecutor 返回的 Result / FailReason / DeviceInfo 写入 **TestRecord**（SavePhase3ResultAsync），并可选执行 MES Post-Report；最后更新 **VerificationSnapshot**。
8. **MainViewModel** 轮询 `_verificationFlowService.Snapshot` 直至 `!IsProcessing`，更新 UI 绑定并清空扫码框。

**数据流（Phase3）**：  
扫码 SN + 产品码 → ProcessCoordinator → ProductProfile + Parameter → RulePipelineExecutor → IDeviceAccessService.ReadDeviceInfoAsync(profile) → DeviceSessionManager（Warmup 一次 + Bootstrap 每批）+ Field 命令 → DeviceInfo → 规则链（匹配/唯一/版本）→ RuleExecutionResult → ProcessCoordinator 落库 + Snapshot。

### 2.3 单次扫码（Legacy 路径）

1. ViewModel 调用 `_verificationFlowService.StartVerificationAsync(trimmedSn)`。
2. **VerificationFlowService** 委托 **ProcessCoordinator.StartVerificationAsync(sn)**。
3. **ProcessCoordinator** 内部使用 **IAdbAccessService** 读取设备 SN（含 ylzero 与 getprop 等 Legacy 逻辑），再按 Phase 2.5 决策树（StickerSN/DeviceSN 匹配、历史 PASS 绑定、包装不一致等）判断，**SaveOrUpdateFailResultAsync / SaveResultAsync** 落库，**UpdateSnapshot**。
4. 不经过 RulePipelineExecutor 与 IDeviceAccessService；设备读取与规则均在同一 Coordinator 内完成。

**数据流（Legacy）**：  
扫码 SN → ProcessCoordinator → IAdbAccessService（ADB）→ 设备 SN → 决策树 → 落库 + Snapshot。

### 2.4 版本校验（VersionMatch）

- 独立于 SN 扫码流程：用户点击「版本校验」等入口后，**MainViewModel** 调用 **IVersionVerificationFlowService.ExecuteVersionCheckAsync(session, ...)**。
- **VersionVerificationFlowService** 使用 **IAdbAccessService.ReadDeviceInfoAsync** 读取设备信息，与当前 Session 的期望版本比较，结果写 **TestRecord**（StickerSN = "-"），并更新自身 Snapshot。
- 数据流：Session + 期望版本 → AdbAccessService → DeviceInfo → 版本比较 → TestRecord + Snapshot。

---

## 3. 设备访问子系统（Phase3）数据流

```
ProductProfile (ProductRegistry.Get(projectId))
    │
    └── AdbConfig
            ├── BootstrapCommandSpecs [ 每检测批次执行 ]
            │     └── Command, AcceptableExitCodes, TimeoutBehavior
            ├── AggregateCommand 或
            └── Commands (FieldCommands)
                  └── Field + Command + ParserKey

RulePipelineExecutor.ExecuteAsync(profile, deviceInfo: null, ...)
    │
    └── IDeviceAccessService.ReadDeviceInfoAsync(profile)
            │
            └── AdbDeviceService
                    │
                    ├── DeviceSessionManager.EnsureSessionReadyAsync(config)
                    │     ├── 若 !_warmupDone → EnsureShellWarmedUpAsync() [ 进程内一次 ]
                    │     └── BootstrapCommandSpecs [ 每次调用都执行 ]
                    │
                    └── ExecuteFieldCommandsAsync(config.Commands) 或 ExecuteAggregateAsync(...)
                          │
                          └── DeviceCommandExecutor.ExecuteAsync(cmd) → ParserFactory → DeviceInfo
```

- **配置驱动**：同一套 RulePipelineExecutor 不关心具体 ADB 命令，只消费 `DeviceInfo`；不同产品通过不同 **ProductProfile.AdbConfig** 得到不同命令与 Parser。
- **Session 模型**：Environment Session（上位机环境）。Warmup 仅表示 Shell 通道已建立，不绑定设备身份；Bootstrap 每检测批次执行，不依赖设备探测与额外 IO。

---

## 4. 关键数据流方向汇总

| 阶段           | 数据方向 |
|----------------|----------|
| 批次开始       | UI → SessionLifecycleService（创建 Session）→ Factory → 新建 ProcessCoordinator + VerificationFlowService |
| 产品/参数      | ProductRegistry（Profile）、ParameterService（版本期望）→ ProcessCoordinator → RulePipelineExecutor |
| 设备信息       | Profile.AdbConfig → IDeviceAccessService → DeviceSessionManager + DeviceCommandExecutor → ADB → DeviceInfo → RulePipelineExecutor |
| 规则结果       | RulePipelineExecutor → RuleExecutionResult（Pass/Fail + FailReason + DeviceInfo）→ ProcessCoordinator |
| 持久化         | ProcessCoordinator → IStorageService（TestRecord）；不由 RulePipelineExecutor 直接写库 |
| 状态到 UI      | ProcessCoordinator.Snapshot → VerificationFlowService.Snapshot → ViewModel 轮询 → 绑定到 View |

---

## 5. 与架构文档的对应关系

- **07_Technical_Architecture_and_Dev_Guide.md**：MVVM、Snapshot、Service 分层、MES Gate、落库规则、Legacy/Phase3 执行模型。
- **DeviceAccess_Subsystem_Architecture_v1.md**：DeviceAccess 分层、BootstrapCommandSpecs、Environment Session、Warmup 一次 / Bootstrap 每批、禁止设备绑定与额外 IO。
- **DeviceAccess_SessionReady_PerDevice_Proposal.md**：SessionReady 仅屏蔽 Warmup 成本；Bootstrap 每检测批次执行。
- **Bootstrap_Tolerant_ExitCodes_Proposal.md**：BootstrapCommandSpec 的 AcceptableExitCodes 与 TimeoutBehavior（Fail / Ignore / Retry）。

---

## 6. 小结（便于人工核对）

- **架构**：View → ViewModel → Service（协调 + 规则 + 设备）→ Infrastructure；设备与规则解耦，配置驱动。
- **双路径**：Legacy 用 AdbAccessService + 内置决策树；Phase3 用 ProductRegistry + RulePipelineExecutor + IDeviceAccessService，规则与设备完全由配置与执行器负责。
- **调用链（Phase3 单次扫码）**：MainViewModel → VerificationFlowService.StartPhase3VerificationAsync → ProcessCoordinator.ProcessScanAsync → ProductProfile + Parameter → RulePipelineExecutor.ExecuteAsync → IDeviceAccessService.ReadDeviceInfoAsync → DeviceSessionManager（Warmup 一次 + Bootstrap 每批）+ Field 命令 → DeviceInfo → 规则链 → Result → ProcessCoordinator 落库与 Snapshot。
- **数据流**：配置与参数自上而下注入；设备信息自 ADB 经 DeviceAccess 以 DeviceInfo 形式进入规则链；规则结果自 RulePipelineExecutor 回到 ProcessCoordinator 落库并驱动 Snapshot；Snapshot 自 Service 到 ViewModel 再到 View 绑定。
