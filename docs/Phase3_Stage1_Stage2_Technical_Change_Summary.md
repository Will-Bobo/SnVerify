# Phase3 Stage1 & Stage2 技术代码变更总结（评审用）

本文档汇总 Phase3 Stage1、Stage2 在代码层面的所有变更，便于评审与回溯。

---

## 一、Stage1 变更概览

Stage1 目标：建立 Phase3 校验系统基础执行结构，不改变原有 Phase2.5 流程，仅扩展 Domain、Storage、ADB、参数服务与 Coordinator 的 Phase3 入口。

### 1.1 Domain 层

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Domain/Models/DeviceInfo.cs` | **新增** | 设备信息数据模型。字段：`DeviceSn`、`WifiMac`、`ChipId`、`BoardVersion`、`ChargeBoardVersion`、`AndroidVersion`。仅数据属性，无业务方法，支持 Snapshot 绑定。 |
| `Domain/Models/VerificationParameter.cs` | **新增** | 项目级版本校验参数。字段：`ProjectId`、`ExpectedAndroidVersion`、`ExpectedBoardVersion`、`ExpectedChargeBoardVersion`。支持持久化与 UI 配置读取。 |
| `Domain/Models/ProjectProfile.cs` | **新增** | 项目配置概要。字段：`ProjectId`、`AggregateDeviceInfoCommand`（可选）。用于 ADB 读取命令配置，Phase3 最小落地。 |
| `Domain/Models/TestRecord.cs` | **扩展** | 新增属性：`WifiMac`、`ChipId`、`BoardVersion`、`ChargeBoardVersion`；保留/明确 `ExpectedVersion`、`ActualVersion`。注释标明 Phase3 扩展用途。 |

### 1.2 StorageService

| 变更项 | 说明 |
|--------|------|
| **新接口** | `Task<bool> IsStickerSnPassedInOrderAsync(string orderId, string sn)`：订单维度内是否存在该 StickerSN 的 PASS 记录。 |
| **新接口** | `Task<bool> IsChipIdPassedInOrderAsync(string orderId, string chipId)`：订单维度内是否存在该 ChipId 的 PASS 记录。 |
| **新接口** | `Task<VerificationParameter> GetVerificationParameterAsync(string projectId)`、`Task SaveVerificationParameterAsync(VerificationParameter parameter)`：版本参数持久化。 |
| **表结构** | 新增表 `VerificationParameter`；`TestRecord` 建表 SQL 增加列 `WifiMac`、`ChipId`、`BoardVersion`、`ChargeBoardVersion` 等；新增索引 `idx_testrecord_chipid_result`。 |
| **迁移** | `MigrateTestRecordAddColumnsAsync`：对已有库执行 `ALTER TABLE TestRecord ADD COLUMN ...`，补充 Phase3 列。 |
| **顺序修正** | `CreateTablesAsync` 中执行顺序调整为：建表 → 执行迁移（ADD COLUMN）→ 建索引，避免在旧库上先建索引再迁移导致 “no such column: ChipId” 等错误。 |
| **CRUD** | `SaveTestRecordAsync`、`GetTestRecordsBySessionAsync`、`GetTestRecordBySessionAndStickerSnAsync`、`UpdateTestRecordAsync` 的 SQL 与映射已包含新列。 |

查询实现均基于 `TestRecord` 联表 `TestSession`、`Order`，按 `OrderName`（业务 orderId）过滤，且仅统计 `Result = 'PASS'`。

### 1.3 AdbAccessService

| 变更项 | 说明 |
|--------|------|
| **新接口** | `Task<DeviceInfo> ReadDeviceInfoAsync(ProjectProfile profile)`。按项目 Profile 读取设备信息，支持聚合命令或回退到分字段读取（SN + `getprop ro.build.display.id` 等），超时与重试策略与既有逻辑一致，不操作 UI 线程。 |

### 1.4 ParameterService（新增服务）

| 文件 | 说明 |
|------|------|
| `Services/Parameter/IParameterService.cs` | 接口：`GetParameterAsync(projectId)`、`SaveParameterAsync(parameter)`。 |
| `Services/Parameter/ParameterService.cs` | 实现：基于 `IStorageService` 持久化，内存缓存（`ConcurrentDictionary`）按 projectId 缓存，避免重复读库。 |

### 1.5 ProcessCoordinator（Phase3 入口与 Parameter 判定）

| 变更项 | 说明 |
|--------|------|
| **构造函数** | 新增可选参数：`IParameterService parameterService`。 |
| **新方法** | `ProcessScanAsync(string sn, string projectId, ProjectProfile projectProfile = null)`：Phase3 校验入口。 |
| **流程顺序（未改）** | 1）取项目参数 → 2）ADB 读取设备信息 → 3）SN 匹配 → 4）ChipId 格式（F50 开头）→ 5）ChipId 订单内唯一（PASS 记录）→ 6）版本匹配 → 7）保存 TestRecord。任一步失败即终止并写 FailReason。 |
| **Parameter 判定修正** | 原逻辑：`parameter == null` 或三 Expected 全为空则 `PARAMETER_NOT_CONFIGURED`。现逻辑：**仅当 `parameter == null` 时** 判定为 `PARAMETER_NOT_CONFIGURED`；Parameter 存在即允许继续，版本校验仅对“已配置的 Expected”做强校验。 |
| **落库** | 通过私有方法 `SavePhase3ResultAsync` 写入 TestRecord（含 DeviceInfo、VerificationParameter 相关字段）并触发 MES Post-Report（若有）。 |

`StartVerificationAsync`（Phase2.5）未改动，Phase3 与 Phase2.5 双轨并存。

### 1.6 VersionVerificationFlowService（Stage1 三版本强校验）

| 变更项 | 说明 |
|--------|------|
| **新方法** | `(bool isPass, string failReason) VerifyVersion(DeviceInfo deviceInfo, VerificationParameter parameter)`：在 FlowService 内执行 Android / Board / ChargeBoard 三版本强校验；`parameter == null` 返回 `PARAMETER_NOT_CONFIGURED`；对非空 Expected 做忽略大小写比较，任一不匹配即返回对应 FailReason。 |

此处为“在 FlowService 内集中版本逻辑”，Stage2 再抽成独立 `IVersionVerificationService` 并由 Coordinator 调用。

### 1.7 Stage1 单元测试

| 测试文件 | 覆盖内容 |
|----------|----------|
| `ProcessCoordinatorPhase3Tests.cs` | `ProcessScanAsync`：SN 匹配成功/失败、ChipId 格式非法、ChipId 订单内重复、Android 版本不匹配、ADB 读取失败、Parameter 未配置、Parameter 存在但三 Expected 全空仍 PASS、三 Expected 均配置且匹配时 PASS。 |
| `StorageServiceOrderScopeTests.cs` | `IsStickerSnPassedInOrderAsync`、`IsChipIdPassedInOrderAsync` 在订单维度内 PASS/非 PASS 及跨订单行为。 |
| `StorageServiceMigrationTests.cs` | 旧库（无 Phase3 列）经 `InitializeAsync` 迁移后，新列与索引存在且无 “no such column” 类错误。 |
| `ParameterServiceTests.cs` | 参数获取、缓存与持久化。 |
| `VersionVerificationFlowServiceTests.cs` | `VerifyVersion`：三版本全匹配、Android/Board/ChargeBoard 不匹配、Parameter 未配置。 |

---

## 二、Stage2 变更概览

Stage2 目标：构建工业级规则引擎抽象——**三版本强校验独立服务** + **Product Profile 唯一规则来源**，Coordinator 只做调度与状态，不写版本/ChipId/SN 规则逻辑。

### 2.1 VersionVerificationService（新增服务）

| 文件 | 说明 |
|------|------|
| `Services/Verification/IVersionVerificationService.cs` | 接口：`Task<(bool success, string failReason)> VerifyAsync(DeviceInfo deviceInfo, VerificationParameter parameter, CancellationToken cancellationToken = default)`。 |
| `Services/Verification/VersionVerificationService.cs` | 实现：仅做三版本强校验。顺序 Android → Board → ChargeBoard；`parameter == null` 返回 `PARAMETER_NOT_CONFIGURED`；非空 Expected 与 actual 忽略大小写比较，任一不匹配即返回对应 FailReason（与 Stage1 语义一致）。无流程控制、无持久化、无 ADB。 |

### 2.2 ProductProfileFactory（新增服务）

| 文件 | 说明 |
|------|------|
| `Services/Product/IProductProfileFactory.cs` | 接口：`ProjectProfile Create(string productId)`。 |
| `Services/Product/ProductProfileFactory.cs` | 实现：校验 `productId` 非空，返回 `new ProjectProfile { ProjectId = productId, AggregateDeviceInfoCommand = null }`。Phase3 允许硬编码最小 Profile；注释预留 JSON/DB 扩展。 |

规则链路：**ProductId → ProductProfileFactory → Profile 对象 → ADB/校验服务使用**；禁止 UI/Coordinator/Domain 直接拼规则。

### 2.3 ProcessCoordinator（Stage2 改造）

| 变更项 | 说明 |
|--------|------|
| **构造函数** | 新增可选参数：`IVersionVerificationService versionVerificationService = null`、`IProductProfileFactory productProfileFactory = null`。保留对现有调用方的兼容（不传则使用回退行为）。 |
| **Profile 来源** | 在 `ProcessScanAsync` 中：若 `_productProfileFactory != null`，则 `effectiveProfile = _productProfileFactory.Create(projectId)`；否则使用调用方传入的 `projectProfile`。ADB 统一使用 `effectiveProfile`。 |
| **版本校验委托** | 删除 Coordinator 内所有“直接比较 Expected* 与 deviceInfo.*”的代码。改为：`var versionService = _versionVerificationService ?? new VersionVerificationService();`，`var (versionOk, versionFailReason) = await versionService.VerifyAsync(deviceInfo, parameter)`；若 `!versionOk` 则按 `versionFailReason` 落库并更新 Snapshot。 |
| **流程顺序** | 未改变：PreGate（若有）→ 取参 → ADB 读取 → SN 匹配 → ChipId 格式 → ChipId 订单唯一 → **VersionVerificationService.VerifyAsync** → 结果存储。 |

Coordinator 不再包含任何版本比较或 Profile 构造逻辑，仅做调度与 Snapshot 管理。

### 2.4 Stage2 单元测试

| 测试文件 | 覆盖内容 |
|----------|----------|
| `VersionVerificationServiceTests.cs` | `VerifyAsync`：三版本全部匹配 → success=true；Android/Board/ChargeBoard 任一项不匹配 → 对应 FailReason；`parameter == null` → `PARAMETER_NOT_CONFIGURED`。 |

既有 `ProcessCoordinatorPhase3Tests` 未改构造方式（未注入 VersionVerificationService/ProductProfileFactory），依赖 Coordinator 内 `_versionVerificationService ?? new VersionVerificationService()` 的默认实例，行为与 Stage1 一致，故全部通过。

### 2.5 工程与依赖

| 变更项 | 说明 |
|--------|------|
| `SnVerify.csproj` | 新增编译项：`Services/Verification/IVersionVerificationService.cs`、`VersionVerificationService.cs`；`Services/Product/IProductProfileFactory.cs`、`ProductProfileFactory.cs`。 |
| **命名空间** | `SnVerify.Services.Verification`、`SnVerify.Services.Product`。注意：`IStorageService`/`StorageService` 等处使用的 `Product` 类型为 `Domain.Models.Product`，与 `Services.Product` 命名空间并存，在正常编译（如测试项目、主项目非动态 WPF 临时项目）下无冲突。 |

---

## 三、架构约束遵守情况

| 约束 | Stage1/Stage2 实现情况 |
|------|-------------------------|
| 不在 UI 中写校验规则 | 未在 View/ViewModel 中增加规则判断；校验均在 Service/Coordinator 调度内完成。 |
| Coordinator 不写版本/ChipId/SN 规则 | Stage2 后版本比较全部移至 `IVersionVerificationService`；SN/ChipId 仍为 Coordinator 内“调用 Storage/ADB 并依据返回值决定 FailReason”的流程步骤，未引入独立规则引擎，符合“仅调度”的阶段性要求。 |
| Domain 不依赖数据库/ADB | Domain 仅包含模型与枚举，无 Storage/ADB 依赖。 |
| Profile 为规则唯一入口 | Stage2 通过 `IProductProfileFactory.Create(projectId)` 得到 Profile，再交给 ADB 与参数服务使用；优先使用工厂，无工厂时回退到调用方传入 Profile。 |
| 三版本强校验工业化封装 | 由 `IVersionVerificationService.VerifyAsync` 统一执行，顺序与 FailReason 与文档一致，便于后续扩展（如更多版本字段或策略）。 |
| Pipeline 顺序不变 | Phase3 流程顺序自 Stage1 起即为：取参 → ADB → SN → ChipId 格式 → ChipId 唯一 → 版本 → 落库；Stage2 仅将“版本”步骤改为调用 VersionVerificationService，顺序未变。 |
| Snapshot 只读、UI 绑定 | VerificationSnapshot 仍由 Coordinator 更新，仅暴露只读属性，无业务计算在 Snapshot 类型内。 |

---

## 四、测试与构建状态

- **单元测试**：在 `d:\wpf_workspace\SnVerify` 下执行 `dotnet test SnVerify.Tests\SnVerify.Tests.csproj --no-build`，**306 个测试通过**（含 Stage1/Stage2 新增用例）。  
- **主工程构建**：使用 `build\build.cmd`（或 `CursorAgentBuild.cmd`）时，若脚本生成动态 WPF 临时项目，可能出现 `Product` 命名空间与类型解析冲突的环境级问题；主库与测试库在常规 MSBuild/dotnet build 下编译通过，无新增业务逻辑错误。

---

## 五、文件清单（新增/显著修改）

### 新增文件

- `Domain/Models/DeviceInfo.cs`
- `Domain/Models/VerificationParameter.cs`
- `Domain/Models/ProjectProfile.cs`
- `Services/Parameter/IParameterService.cs`
- `Services/Parameter/ParameterService.cs`
- `Services/Verification/IVersionVerificationService.cs`
- `Services/Verification/VersionVerificationService.cs`
- `Services/Product/IProductProfileFactory.cs`
- `Services/Product/ProductProfileFactory.cs`
- `SnVerify.Tests/Services/ProcessCoordinatorPhase3Tests.cs`
- `SnVerify.Tests/Services/StorageServiceOrderScopeTests.cs`
- `SnVerify.Tests/Services/StorageServiceMigrationTests.cs`
- `SnVerify.Tests/Services/ParameterServiceTests.cs`
- `SnVerify.Tests/Services/VersionVerificationServiceTests.cs`

### 显著修改文件

- `Domain/Models/TestRecord.cs`（新增 Phase3 字段）
- `Services/Storage/IStorageService.cs`（订单维度查询与 VerificationParameter CRUD）
- `Services/Storage/StorageService.cs`（实现、表结构、迁移顺序、CRUD 新列）
- `Services/Adb/IAdbAccessService.cs`、`AdbAccessService.cs`（ReadDeviceInfoAsync(ProjectProfile)）
- `Services/Coordination/ProcessCoordinator.cs`（ProcessScanAsync、Parameter 判定、Stage2 注入与版本委托、Profile 工厂）
- `Services/Coordination/VersionVerificationFlowService.cs`（VerifyVersion 三版本强校验）
- `SnVerify.Tests/Services/VersionVerificationFlowServiceTests.cs`（VerifyVersion 及三版本/PARAMETER_NOT_CONFIGURED 用例）
- `SnVerify.csproj`（新服务与 Domain 的编译项）

---

**文档版本**：v1.0  
**适用阶段**：Phase3 Stage1 + Stage2 代码评审。

---

## 六、KM001 聚合命令增补（2026-03）

> 本增补用于记录 KM001 在 Phase3 下的设备信息读取方式收敛，不改变既有流程骨架。

- KM001 `AdbConfig` 已切换为 **aggregate-only**：
  - `AggregateCommand.Command = "shell dumpsys window getmcuversion"`
  - `AggregateCommand.ParserKey = ParserKeys.Aggregate.Km001McuVersion`
  - `BootstrapCommandSpecs = null`
  - `Commands = null`
- 新增 `Km001McuVersionAggregateParser`，协议映射为：
  - 第2行第1列 -> `ChargeBoardVersion`
  - 第2行第2列 -> `BoardVersion`
  - 第2行第3列 -> `ChipId`
  - 第2行第4列 -> `AndroidVersion`
  - 第2行第5列 -> `DeviceSn`
  - 第2行第6列 -> `WifiMac`
- 协议规则：
  - 先做换行标准化（`\r\n` -> `\n`）再分行；
  - 少于两行或第二行列数小于6时，视为协议错误；
  - 列数大于6时仅使用前6列。
- 失败语义区分：
  - 无输出/未读到 SN -> `ADB_READ_FAIL`
  - 协议不符（解析异常）-> `ADB_PROTOCOL_INVALID`
- SN 比对保持 `StringComparison.Ordinal`，严格大小写一致。
