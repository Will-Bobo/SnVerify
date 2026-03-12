# Phase 3 架构梳理总结（供评审）

> 本文档基于 Phase 3 一轮架构重整后的代码与文档整理，用于**技术评审**与后续需求分析参考。  
> 与 `07_Technical_Architecture_and_Dev_Guide.md` 互补：07 为长期约束与数据/流程说明，本文为 Phase 3 新增/变更的架构要点汇总。

---

## 一、Phase 3 架构重整目标与结果

### 1.1 目标（来自 Phase3 扩展规格）

- 在 SN 校验基础上增加：**ChipId / WifiMac / BoardVersion / ChargeBoardVersion / AndroidVersion** 的读取与校验。
- 校验规则：SN 匹配、**SN 唯一（Order 内）**、ChipId 格式与唯一（Order 内）、版本校验。
- 设备信息读取与规则执行**可配置、可扩展**，与 ADB 协议解耦。

### 1.2 本轮重整完成内容

- **产品配置统一**：废弃 `ProjectProfile` / `IProductProfileFactory`，产品维度唯一事实源为 **ProductProfile**（`Domain.Product`），设备读取配置由 **AdbConfig**（`DeviceAdbConfig`）承载。
- **DeviceAccess 子系统**：新增设备访问抽象与实现，规则链通过 **IDeviceAccessService** 按 `ProductProfile.AdbConfig` 读取设备信息，与 ADB 具体命令解耦。
- **规则链外置**：**IRulePipelineExecutor** 负责“按固定顺序执行规则、Fail Fast”，**ProcessCoordinator** 仅做流程编排与落库，不再内嵌决策树。
- **版本参数与三版本校验**：**IParameterService** 提供项目级版本目标（VerificationParameter），**IVersionVerificationService** 提供三版本强校验逻辑，供规则链调用。
- **产品注册表抽象**：**IProductRegistry** 供 UI 与流程按产品代码获取 **ProductProfile**，实现可 Mock，默认实现为 **ProductRegistryAdapter** 包装静态 **ProductRegistry**。

---

## 二、分层与职责（Phase 3 现状）

### 2.1 总体分层（不变）

```text
View (WPF)  →  ViewModel  →  Service  →  Infrastructure / Domain
```

- **View**：仅布局与绑定，无业务逻辑。
- **ViewModel**：UI 状态、命令、轮询 Snapshot；不直接 IO，不依赖 WPF 类型。
- **Service**：业务流程、规则执行、Session 生命周期、导出、MES Gate。
- **Infrastructure**：ADB 实现、SQLite、设备访问实现、产品注册表实现、工厂与组合根。

### 2.2 新增/强化的命名空间与职责

| 层次 | 命名空间 / 目录 | 职责概要 |
|------|------------------|----------|
| **Domain** | `Domain.Product` | **ProductProfile**、**VerificationMode**、**DeviceInfoCommandSet** 等产品与校验模式定义。 |
| **Domain** | `Domain.DeviceAccess` | **DeviceAdbConfig**、**DeviceInfoCommand** / **AggregateDeviceInfoCommand**、**IDeviceInfoParser** / **IAggregateDeviceInfoParser**、**IParserFactory**、**BootstrapTimeoutBehavior** 等设备访问配置与解析契约。 |
| **Domain** | `Domain.Models` | **DeviceInfo**（Phase3 设备信息模型）、**VerificationParameter**（项目级版本参数）；原有 Product/Order/TestSession/TestRecord 不变。 |
| **Services** | `Services.Parameter` | **IParameterService** / **ParameterService**：项目级版本参数的读取与持久化、缓存。 |
| **Services** | `Services.Verification` | **IVersionVerificationService** / **VersionVerificationService**：三版本（Android/Board/ChargeBoard）强校验逻辑。 |
| **Services** | `Services.Rules` | **IRulePipelineExecutor** / **RulePipelineExecutor**：按 **ProductProfile** 执行规则链（参数检查 → 设备信息读取 → SN 匹配 → SN 唯一 → ChipId 格式/唯一 → 版本校验），Fail Fast，不落库。 |
| **Services** | `Services.DeviceAccess` | **IDeviceAccessService**：按 **ProductProfile.AdbConfig** 读取 **DeviceInfo**，与 ADB 协议解耦。 |
| **Infrastructure** | `Infrastructure.Product` | **IProductRegistry** / **ProductRegistryAdapter**、**ProductRegistry**：产品 Profile 只读注册表，唯一规则入口。 |
| **Infrastructure** | `Infrastructure.DeviceAccess.*` | **DeviceSessionManager**（Session/Bootstrap）、**DeviceCommandExecutor**、**Parser**（如 TrimParser）、**ParserFactory**、**AdbDeviceService**（IDeviceAccessService 实现）。 |

---

## 三、核心数据流（Phase 3 主流程）

### 3.1 入口与配置来源

- **UI**：用户选择产品（ProductCode）、订单，开始 Session 后扫码。
- **MainViewModel**：根据 **SelectedProductCode** 从 **IProductRegistry** 取 **ProductProfile**，驱动 **Legacy / Phase3** 模式与 UI 展示；Phase3 时调用 **IVerificationFlowService.StartPhase3VerificationAsync(sn, projectId)**（或等价入口）。
- **VerificationFlowService**：转发 **IProcessCoordinator.ProcessScanAsync(sn, projectId)**。
- **ProcessCoordinator**：
  - 用 **projectId** 从 **IProductRegistry.GetProductProfile(projectId)** 取 **ProductProfile**；取不到则直接返回“未找到产品 Profile”。
  - 从 **IParameterService** 取 **VerificationParameter**（projectId）；规则链需要且为空时返回“参数未配置”。
  - 将 **ProductProfile**、**VerificationParameter**、**stickerSn**、**orderId** 交给 **IRulePipelineExecutor.ExecuteAsync**；可选传入预读的 **DeviceInfo**（当前多为 null，由执行器内部读）。

### 3.2 规则链内部（RulePipelineExecutor）

1. **参数检查**：`parameter == null` → 立即返回 **PARAMETER_NOT_CONFIGURED**。
2. **设备信息读取**：若未传入 `deviceInfo`，则 **IDeviceAccessService.ReadDeviceInfoAsync(profile)**；依赖 **profile.AdbConfig**；未配置或解析异常 → 返回“ADB 命令为空”或“ADB_READ_FAIL”。
3. **SN 匹配**：StickerSN 与 DeviceSN 字符串比较，不匹配 → Fail Fast。
4. **SN 唯一（Order 内）**：通过 **IStorageService** 在**当前 Order** 内查 PASS 历史，重复 → Fail Fast。
5. **ChipId 格式 / 唯一（Order 内）**：按 **ProductProfile** 开关执行；格式（如 F50 开头）与 Order 内唯一性由规则链调用 Storage 完成。
6. **版本校验**：调用 **IVersionVerificationService**，用 **VerificationParameter** 的期望值与 **DeviceInfo** 的实际值做三版本比较。
7. 全部通过 → 返回成功结果；**不执行任何落库**。

### 3.3 落库与状态（ProcessCoordinator）

- **ProcessCoordinator** 根据 **RuleExecutionResult** 决定 PASS/FAIL 及 FailReason。
- 落库统一由 **ProcessCoordinator** 调用 **IStorageService** 写入 **TestRecord**（含 Phase3 字段：WifiMac、ChipId、BoardVersion、ChargeBoardVersion、ExpectedVersion/ActualVersion 等）。
- 更新 **VerificationSnapshot**，触发 **SnapshotChanged**；MES 仍为 Pre-Gate / Post-Report 挂载点（可选）。

---

## 四、关键抽象与依赖关系

### 4.1 ProcessCoordinator 依赖（构造）

| 依赖 | 用途 |
|------|------|
| IStorageService | 历史查询（SN/ChipId 唯一等）、TestRecord 读写。 |
| IAdbAccessService | Legacy 路径读设备 SN 等（Phase3 主路径走 IDeviceAccessService）。 |
| ILoggingService | 日志。 |
| IMesPreCheck / IMesResultReporter / MesMode | MES Gate（可选）。 |
| IParameterService | 项目级版本参数。 |
| IVersionVerificationService | 三版本校验。 |
| IProductRegistry | 按 projectId 取 ProductProfile。 |
| IDeviceAccessService | 可选；与 IRulePipelineExecutor 配合，用于构建默认规则执行器。 |
| IRulePipelineExecutor | 规则链执行；由工厂注入，内部已含 IDeviceAccessService。 |

### 4.2 VerificationFlowServiceFactory 依赖

- **IStorageService**、**IAdbAccessService**、**ILoggingService**、**IParameterService**、**IVersionVerificationService**、**IProductRegistry**、**IRulePipelineExecutor**。
- **Create(sessionId, orderId)** 只创建 **ProcessCoordinator** + **VerificationFlowService**，不传 **IDeviceAccessService**（已包含在 **IRulePipelineExecutor** 内部）。

### 4.3 ServiceFactory（组合根）组装顺序（要点）

1. **StorageService** → InitializeAsync。
2. **LoggingService**、**SessionLifecycleService**。
3. **AdbAccessService**（Legacy）、**ProcessRunner**。
4. **DeviceAccess 子系统**：**DeviceSessionManager**、**DeviceCommandExecutor**、**TrimParser**、**ParserFactory**、**AdbDeviceService**（**IDeviceAccessService**）。
5. **ParameterService**、**VersionVerificationService**、**ProductRegistryAdapter**（**IProductRegistry**）、**RulePipelineExecutor**（Storage + **IDeviceAccessService** + VersionVerificationService）。
6. **VerificationFlowServiceFactory**（Storage、Adb、Logging、Parameter、VersionVerification、ProductRegistry、**RulePipelineExecutor**）。
7. **VersionVerificationFlowService**（Legacy 版本校验）、**ExportAggregationService**、**OrderNameValidator**、**WpfUserDialogService**。
8. **MainViewModel**（上述所有需要的接口 + **productRegistry**、**parameterService**）。

---

## 五、数据与存储（Phase 3 相关）

### 5.1 表与模型

- **Product / Order / TestSession**：与 Phase 2.5 一致。
- **TestRecord**：在原有字段基础上增加 Phase3 字段（如 **WifiMac**、**ChipId**、**BoardVersion**、**ChargeBoardVersion**；ExpectedVersion/ActualVersion 已有）。
- **VerificationParameter**：项目级版本目标（ExpectedAndroidVersion、ExpectedBoardVersion、ExpectedChargeBoardVersion），由 **ParameterService** 读写，带缓存。

### 5.2 唯一性范围（Phase3 规格）

- **SN 唯一**：**Order 内**（与 Phase 2.5 的“全局历史”不同，若已切到 Phase3 规格则按 Order 内实现）。
- **ChipId 唯一**：**Order 内**。
- 索引：如 **idx_order_sn(OrderId, StickerSN)**、**idx_order_chip(OrderId, ChipId)** 等，以支持 Order 内唯一查询。

（具体索引与表结构以 **StorageService** 与迁移脚本为准。）

---

## 六、DeviceAccess 子系统要点

- **Domain.DeviceAccess**：定义 **DeviceAdbConfig**（Bootstrap、聚合命令或按字段命令）、**DeviceInfoCommand** / **AggregateDeviceInfoCommand**、解析器接口 **IDeviceInfoParser** / **IAggregateDeviceInfoParser**、**IParserFactory**。
- **Infrastructure.DeviceAccess**：
  - **Session**：**DeviceSessionManager** 负责 ADB 连接与 Bootstrap，Bootstrap 仅在首次读取时执行并标记 session ready。
  - **Command**：**DeviceCommandExecutor** 执行单条 ADB 命令并返回输出。
  - **Parser**：**TrimParser**（Key 为 ParserKeys.Field.Trim）、**ParserFactory** 按 Key 返回解析器；配置中只存 ParserKey，须使用 ParserKeys 常量，不持解析器实例。
  - **Service**：**AdbDeviceService** 实现 **IDeviceAccessService**；**AdbConfig** 为 null 或未配置有效命令时抛“ADB 命令未配置”；禁止 Aggregate 与 Commands 混用。
- **设备信息 UI**：Phase3 完成后 **VerificationSnapshot** 可携带 **DeviceInfo**（Snapshot 内拷贝保证不可变）；**MainViewModel.CurrentDeviceInfo** 为 get-only，由 Snapshot 推导（Snapshot.DeviceInfo → 或 DeviceSN fallback → 或空对象）；界面绑定 `CurrentDeviceInfo.*`，详见 docs/phase3/Phase3_DeviceInfo_UI_Refresh_Proposal.md。
- **规则链**：仅依赖 **IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)**，与 ADB 协议解耦。

---

## 七、与 07 文档、Phase 2.5 的对照

- **07_Technical_Architecture_and_Dev_Guide.md**：仍为长期架构约束（MVVM、Snapshot、状态/事件、命令规则、禁止项等）；数据模型与流程描述可在此基础上扩展“Phase3 规则链、DeviceAccess、Parameter、ProductRegistry”。
- **Phase 2.5**：保留 Product/Order/TestSession/TestRecord 四表、Session 生命周期、MES Gate、Legacy SN 决策树与版本校验流程；Phase 3 在此基础上增加“规则链 + DeviceAccess + 参数 + 产品 Profile”，并支持 **Legacy / Phase3 双模式**（由 ProductProfile.Mode 与 UI 选择决定）。

---

## 八、评审检查清单（建议）

- [ ] **分层**：ViewModel 无 IO、无 WPF 类型；规则逻辑在 RulePipelineExecutor，落库仅在 ProcessCoordinator。
- [ ] **单一事实源**：产品配置仅来自 **IProductRegistry** + **ProductProfile**；设备读取仅通过 **IDeviceAccessService** + **AdbConfig**。
- [ ] **可测性**：IRulePipelineExecutor、IDeviceAccessService、IParameterService、IProductRegistry 均可 Mock，ProcessCoordinator 与 MainViewModel 单元测试可覆盖 Phase3 分支。
- [ ] **扩展点**：新增产品或新规则步骤可在 ProductProfile 与规则链顺序中扩展；新设备字段可扩展 DeviceInfo 与 Parser。
- [ ] **文档与代码一致**：07 文档中 Phase 3.0 描述、数据库唯一性范围（Order 内）、索引与 TestRecord 字段与当前实现一致。
- [ ] **Legacy 路径**：Legacy 产品仍走原有 AdbAccessService + 原决策树，不受 DeviceAccess 与规则链影响。

---

## 九、参考文档

- `Phase3_SN_Verify_Extension_Spec.md`：Phase3 功能与数据规格。
- `07_Technical_Architecture_and_Dev_Guide.md`：长期架构与开发约束。
- `DeviceAccess_Implementation_Change_Summary.md`：DeviceAccess 与 Profile 合并实施总结。
- `ProjectProfile_ProductProfile_Merge_Review.md`：ProjectProfile 与 ProductProfile 合并评审与方案。
