# ProjectProfile 与 ProductProfile 合并评估方案

**文档结构**：第一节至第七节为合并评估与原始合并方案；附录为扩展评审与最新方案；**第六节为评审结果与 4 项修订说明，第七节为修订后最终方案。修订后方案须待新方案评审通过后再执行，禁止未审核就执行。**

---

## 一、两者在系统中的使用流程与作用

### 1.1 ProjectProfile 的使用流程与作用

**定义位置**：`Domain.Models.ProjectProfile`，仅两个属性：`ProjectId`、`AggregateDeviceInfoCommand`。

**数据来源**：
- **IProductProfileFactory.Create(productId)**：按产品 ID 构造一个 `ProjectProfile`，当前实现中 `AggregateDeviceInfoCommand` 恒为 `null`。该工厂被注入到 `ProcessCoordinator` 的构造函数中，但在 Phase3 流程里**从未被调用**（ProcessCoordinator 只通过 `_productRegistry.GetProductProfile(projectId)` 取配置）。
- **RulePipelineExecutor 内部手建**：执行规则链时，若需要由执行器自己调 ADB 读设备信息（即外部未传入 `deviceInfo`），会从已有的 `ProductProfile profile` 拼出一个 `ProjectProfile`：`new ProjectProfile { ProjectId = profile.ProductCode, AggregateDeviceInfoCommand = null }`，再传给 `AdbAccessService.ReadDeviceInfoAsync(projectProfile)`。

**调用链（Phase3 实际路径）**：
1. **UI/ViewModel** 调用 `IVerificationFlowService.StartPhase3VerificationAsync(sn, projectId)`（仅传 `sn` 与 `projectId`，不传任何 Profile）。
2. **VerificationFlowService** 调用 `IProcessCoordinator.ProcessScanAsync(sn, projectId)`，**未传第三参数** `ProjectProfile`。
3. **ProcessCoordinator.ProcessScanAsync** 忽略第三参数，用 `projectId` 从 **ProductRegistry** 取 `ProductProfile`（`_productRegistry.GetProductProfile(projectId)`），取不到则直接失败返回；取到则把该 **ProductProfile** 传给 **RulePipelineExecutor.ExecuteAsync(productProfile, ...)**。
4. **RulePipelineExecutor.ExecuteAsync** 收到 **ProductProfile**，在需要读设备信息且未传入 `deviceInfo` 时，**临时构造** `ProjectProfile { ProjectId = profile.ProductCode, AggregateDeviceInfoCommand = null }`，再调用 **AdbAccessService.ReadDeviceInfoAsync(projectProfile)**。
5. **AdbAccessService.ReadDeviceInfoAsync(ProjectProfile profile)**：若 `profile != null` 且 `AggregateDeviceInfoCommand` 为空，则**直接抛出** `InvalidOperationException("ADB 命令未配置")`，不再执行后续 Step 2/3；若提供了聚合命令则执行该命令并解析（当前解析未实现）。因此当前 Phase3 下，由于构造的 `ProjectProfile` 恒为“聚合命令为空”，**一定会触发“ADB 命令未配置”**，规则链返回“adb 命令为空”。

**作用小结**：  
ProjectProfile 在设计上用于“向 ADB 层提供项目级配置”，目前**仅 ADB 读取设备信息**这一处使用；实际数据流中，它**只来自 RulePipelineExecutor 的临时构造**，且聚合命令恒为空，导致 ADB 层必然报“未配置”。`IProductProfileFactory` 虽返回 `ProjectProfile`，在 Phase3 流程中**未被使用**。

---

### 1.2 ProductProfile 的使用流程与作用

**定义位置**：`Domain.Product.ProductProfile`，属性包括：`ProductCode`、`ProductDisplayName`、`Mode`、`AdbCommands`（DeviceInfoCommandSet）、`EnableChipIdCheck`、`EnableWifiMacCheck`、`EnableBoardVersionCheck`、`EnableChargeBoardVersionCheck` 等。

**数据来源**：**唯一来源**为 **ProductRegistry**（`ProductRegistry.Get(productCode)` / `GetProductProfile(productCode)`），内置静态字典，按产品代码返回对应 ProductProfile（如 KM001、默认 Legacy 等）。

**使用流程与作用**：

| 环节 | 流程与作用 |
|------|------------|
| **UI 产品选择与展示** | MainViewModel 中，用户选择产品（`SelectedProductCode`）后触发 `UpdateCurrentProductProfile()`，通过 `_productRegistry.Get(SelectedProductCode)` 得到 **ProductProfile**，赋给 `_currentProductProfile`；用其 **ProductCode**、**Mode**（Legacy / Phase3）驱动界面文案（如 “KM001 [Phase3模式]”）以及 **IsLegacyProduct** / **IsPhase3Product**，从而决定走 Legacy 校验还是 Phase3 校验、以及 UI 展示与按钮可用性。 |
| **Phase3 流程编排** | ProcessCoordinator.ProcessScanAsync 用入参 **projectId**（即产品代码）从 **ProductRegistry.GetProductProfile(projectId)** 取 **ProductProfile**；取不到则直接返回“未找到产品 Profile”；取到后作为**规则与 ADB 的同一配置源**，传给 RulePipelineExecutor.ExecuteAsync(productProfile, ...)。 |
| **规则链执行** | RulePipelineExecutor.ExecuteAsync 接收 **ProductProfile**，用于：① 规则与开关（如各 EnableXxxCheck）；② 当需要读设备信息时，用其 **ProductCode** 拼出 **ProjectProfile** 再调 AdbAccessService（见上文）。即：**ProductProfile 是规则链的唯一输入配置，ProjectProfile 只是为满足 ADB 接口而临时派生的 DTO**。 |

**作用小结**：  
ProductProfile 是**产品维度的唯一事实源**：来自 ProductRegistry，驱动 UI 展示与模式判断、流程编排中的“是否有该产品配置”、以及规则链的完整配置（含后续可用的 AdbCommands 等）。当前 ADB 层并不直接使用 ProductProfile，而是由 RulePipelineExecutor 从 ProductProfile 转成 ProjectProfile 再调 ADB，导致**同一产品配置存在两处形状、且聚合命令无法从 ProductRegistry 传到 ADB**。

---

### 1.3 两类型在流程中的关系（简要）

- **Phase3 实际数据流**：`projectId`（产品代码）→ **ProductRegistry** → **ProductProfile** → ProcessCoordinator → RulePipelineExecutor；RulePipelineExecutor 再根据 ProductProfile **手建 ProjectProfile**（仅填 ProductCode→ProjectId、AggregateDeviceInfoCommand=null）→ AdbAccessService。
- **矛盾点**：ProductProfile 已包含产品标识（ProductCode）和后续可扩展的 AdbCommands，但“聚合命令”只存在于 ProjectProfile 且当前构造时恒为空，ProductRegistry 侧无法为某产品配置聚合命令，导致 ADB 层必然报“未配置”。合并后，聚合命令可放在 ProductProfile 上，由 ProductRegistry 统一配置，ADB 直接使用 ProductProfile，不再需要中间 ProjectProfile。

---

## 二、现状对比（表）

| 维度 | ProjectProfile (Domain.Models) | ProductProfile (Domain.Product) |
|------|--------------------------------|----------------------------------|
| **用途** | ADB 读取设备信息时的“项目配置”（最小 DTO） | 产品级规则唯一事实源（ProductRegistry 返回） |
| **字段** | `ProjectId`, `AggregateDeviceInfoCommand` | `ProductCode`, `ProductName`, `Mode`, `AdbCommands`, `EnableChipIdCheck` 等 |
| **来源** | 由 `IProductProfileFactory.Create(productId)` 或 RulePipelineExecutor 手建 `new ProjectProfile { ProjectId = profile.ProductCode, AggregateDeviceInfoCommand = null }` | 仅由 `ProductRegistry.Get(productCode)` 返回 |
| **消费者** | `AdbAccessService.ReadDeviceInfoAsync(ProjectProfile)`；`IProcessCoordinator.ProcessScanAsync(..., ProjectProfile = null)` 可选参数 | `RulePipelineExecutor.ExecuteAsync(ProductProfile, ...)`；MainViewModel 展示与模式判断；ProcessCoordinator 取规则 |

**结论**：两者都表示“按产品/项目的配置”，但分层不同——ProjectProfile 偏向“ADB 用到的少量字段”，ProductProfile 是“产品维度的完整配置”。当前 Phase3 流程中，RulePipelineExecutor 已持有 **ProductProfile**，却要再拼一个 **ProjectProfile**（且 `AggregateDeviceInfoCommand` 恒为 null）去调 ADB，存在重复与割裂。

---

## 三、是否重复、能否合并

- **概念上**：都是“按产品/项目的配置”，ProjectId 与 ProductCode 语义一致，**可以视为同一概念的不同形状**。
- **实现上**：ProjectProfile 仅有 2 个字段且只被 ADB 与少量可选参数使用；ProductProfile 已是 ProductRegistry 的唯一出口并被规则链、UI 广泛使用。**以 ProductProfile 为唯一类型，把 ADB 所需字段并入，即可消除 ProjectProfile，避免两套类型。**

合并后收益：
- 规则与 ADB 共用同一 Profile 类型，不再在 RulePipelineExecutor 里从 ProductProfile 转成 ProjectProfile。
- 产品配置只来自 ProductRegistry，单一事实源更清晰。
- 后续为某产品配置“聚合 ADB 命令”时，直接在 ProductRegistry 的 ProductProfile 上设字段即可。

---

## 四、合并方案（推荐）

### 4.1 保留 ProductProfile，废弃 ProjectProfile

- **保留并扩展**：`Domain.Product.ProductProfile`。
- **新增字段**：在 ProductProfile 上增加 **`string AggregateDeviceInfoCommand`**（可选），语义与现 ProjectProfile 中的一致，用于“一次性读取设备信息的 ADB 命令”。
- **删除**：`Domain.Models.ProjectProfile` 整个类型。

### 4.2 调用方调整

| 位置 | 现行为 | 调整后 |
|------|--------|--------|
| **AdbAccessService** | `ReadDeviceInfoAsync(ProjectProfile profile)`，使用 `profile.ProjectId`、`profile.AggregateDeviceInfoCommand` | 改为 `ReadDeviceInfoAsync(ProductProfile profile)`，使用 `profile.ProductCode`、`profile.AggregateDeviceInfoCommand`；空命令时逻辑不变（抛“ADB 命令未配置”）。 |
| **RulePipelineExecutor** | 从 `ProductProfile profile` 构造 `new ProjectProfile { ProjectId = profile.ProductCode, AggregateDeviceInfoCommand = null }` 再调 ADB | 直接传入 `profile` 给 `ReadDeviceInfoAsync(profile)`，不再构造 ProjectProfile。 |
| **IProcessCoordinator / ProcessCoordinator** | `ProcessScanAsync(string sn, string projectId, ProjectProfile projectProfile = null)` | 改为 `ProcessScanAsync(string sn, string projectId, ProductProfile productProfile = null)`，或直接去掉第三参数（当前调用处均未传，Profile 由 projectId 从 Registry 取）。建议**去掉第三参数**，保持入口简洁。 |
| **IProductProfileFactory / ProductProfileFactory** | 返回 `ProjectProfile`，仅包含 ProjectId + AggregateDeviceInfoCommand | **删除** IProductProfileFactory 与 ProductProfileFactory（ProcessCoordinator 已用 ProductRegistry 取 ProductProfile，工厂未被实际用于 Phase3）。若别处无引用，可一并移除；若有兼容调用，可改为委托给 ProductRegistry.Get(productId) 并返回 ProductProfile。 |

### 4.3 ProductRegistry 与 Adb 命令

- 当前 KM001 的 `AdbCommands` 为空的 `DeviceInfoCommandSet()`，且无 `AggregateDeviceInfoCommand`，因此会触发“adb 命令为空”。
- 合并后：在 ProductProfile 上为需要 ADB 读取的产品（如 KM001）配置 **`AggregateDeviceInfoCommand`**（或后续实现按 `AdbCommands` 分字段读取）。Phase3 若暂不实现聚合命令，可先保留“未配置则报错”的语义，由产品侧后续在 Registry 中补全命令。

### 4.4 类型与命名空间

- **Domain.Models**：删除 `ProjectProfile.cs`。
- **Domain.Product**：`ProductProfile` 增加 `AggregateDeviceInfoCommand`；保留 `DeviceInfoCommandSet`（与“聚合命令”二选一或并存，由后续实现决定）。
- **Services**：`IAdbAccessService.ReadDeviceInfoAsync` 改为接受 `ProductProfile`（可放在 `Domain.Product` 或通过 using 引用）。若希望 ADB 层不依赖 Product 命名空间，可保留一个仅含 `ProjectId`/`ProductCode` + `AggregateDeviceInfoCommand` 的接口/DTO，由调用方从 ProductProfile 填充；但这样仍保留“两套形状”，推荐直接使用 ProductProfile 以彻底合并。

---

## 五、实施步骤建议

1. 在 **ProductProfile** 上增加 **`AggregateDeviceInfoCommand`**（可选 string）。
2. **AdbAccessService**：`ReadDeviceInfoAsync` 改为接收 **ProductProfile**，内部使用 `profile.ProductCode`、`profile.AggregateDeviceInfoCommand`（空则仍抛“ADB 命令未配置”）。
3. **RulePipelineExecutor**：删除 `new ProjectProfile { ... }`，直接 `ReadDeviceInfoAsync(profile)`。
4. **IProcessCoordinator / ProcessCoordinator**：`ProcessScanAsync` 去掉第三参数 `ProjectProfile projectProfile`（或改为 ProductProfile 且不传，由内部按 projectId 取 Profile）。
5. 删除 **ProjectProfile.cs**；移除或替换对 **IProductProfileFactory** 的依赖（若已无调用可删工厂接口与实现）。
6. **ProductRegistry**：为 KM001（及需要 ADB 的产品）设置 **AggregateDeviceInfoCommand**（或占位），避免一律“命令为空”；若暂无具体命令，可先保持当前“未配置即失败”的行为。
7. 全量编译与单测（含 Adb、RulePipeline、ProcessCoordinator、MainViewModel 相关用例）通过。

---

## 六、风险与注意点

- **AdbAccessService 依赖 Domain.Product**：若项目约定“服务层不引用 Product 命名空间”，可保留一个仅含 `ProductCode` + `AggregateDeviceInfoCommand` 的只读接口（如 `IAdbProfile`），由 Domain.Product 或应用层实现，AdbAccessService 依赖接口即可；类型仍唯一（ProductProfile 实现该接口），仅多一层抽象。
- **ProcessScanAsync 第三参数**：当前调用链未传该参数，删除后行为不变；若有测试或将来扩展通过该参数注入 Profile，需改为传入 ProductProfile 或从 Registry 按 projectId 解析。

---

## 七、结论

- **可以合并**：以 **ProductProfile** 为唯一产品/项目配置类型，增加 **AggregateDeviceInfoCommand**，删除 **ProjectProfile**，并据此调整 Adb、RulePipeline、ProcessCoordinator 与工厂，能消除重复、统一事实源，且不影响现有“未配置则报错”的语义。
- 建议按上述步骤实施，并在合并后通过编译与单测验证；若采纳“ADB 层不直接依赖 Product”的约束，再增加一层 `IAdbProfile` 薄接口即可。

审核通过后可按本方案执行代码修改。

---

# 附录：基于 DeviceAccess 子系统的扩展评审与最新方案（待审核后执行）

以下为对「ADB 升级为 DeviceAccess 子系统」评估的**评审意见**，以及与原 Profile 合并方案整合后的**最新方案**。审核通过后再执行实施。

---

## 一、对本次评估的评审意见

### 1.1 一致性与认可点

| 评估结论 | 评审意见 |
|----------|----------|
| 删除 ProjectProfile、IProductProfileFactory | **同意**。与原合并方案一致，且 Phase3 流程中工厂未被使用。 |
| 设备访问与业务规则解耦 | **同意**。RulePipeline 只依赖「读设备信息」的抽象接口，不关心 ADB 命令与解析细节，符合分层目标。 |
| 引入 DeviceAdbConfig 替代 DeviceInfoCommandSet | **同意**。现 DeviceInfoCommandSet 仅描述字段命令字符串，无法表达 Bootstrap、聚合命令、解析器；新模型可覆盖多产品、聚合/字段、前置命令、不同解析等需求。 |
| 支持 Bootstrap / Aggregate / Field Command | **同意**。与需求表中的「命令前置」「聚合命令」「字段命令」一一对应，便于扩展。 |
| 引入 Parser 体系 | **同意**。不同产品输出格式不同，由 Parser 封装解析逻辑，新增产品 = 新配置 + 新 Parser，无需改规则链。 |
| 业务层接口 ReadDeviceInfoAsync(ProductProfile) | **同意**。Profile 合并后仅保留 ProductProfile，设备访问服务直接接收 ProductProfile，从 profile.AdbConfig 取配置，与合并方案一致。 |

### 1.2 建议补充与约定

| 项 | 说明 |
|----|------|
| **DeviceInfo 类型** | 保持现有 `Domain.Models.DeviceInfo` 不变；`IAggregateDeviceInfoParser.Parse(string)` 及字段解析汇总结果均返回该类型，规则链与 ProcessCoordinator 继续使用 DeviceInfo。 |
| **与现有 IAdbAccessService 的边界** | 当前 `IAdbAccessService` 还提供：`ReadDeviceSnAsync(CT)`、`ReadDeviceInfoAsync(CT)`（调试）、`GetDeviceSNAsync`、`CheckMultipleDevices`、`Snapshot`。建议 **Phase3 路径** 仅使用新接口 **IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)**；Legacy / 调试路径继续使用 **IAdbAccessService**。即：RulePipelineExecutor 注入 **IDeviceAccessService**，其他调用方（MainViewModel 调试按钮、VersionVerificationFlowService、ProcessCoordinator 若需 Legacy 等）仍使用 **IAdbAccessService**。迁移期内可保留两个接口与两套实现（或由同一实现类同时实现两个接口），避免一次替换所有调用点。 |
| **AdbConfig 为空或未配置** | ProductProfile.AdbConfig 为 null 或未配置有效命令时，行为与原方案一致：视为「ADB 未配置」，IDeviceAccessService.ReadDeviceInfoAsync 应抛异常或返回明确失败，RulePipelineExecutor 捕获后返回「adb 命令为空」等，不执行后续规则。 |
| **目录与命名空间** | 设备访问子系统放在 **Infrastructure/DeviceAccess**（Configuration、Session、Command、Parser、Service）；Legacy 用的 **IAdbAccessService** 继续放在 **Services/Adb**。**推荐**：**DeviceAdbConfig**、**DeviceInfoCommand**、**AggregateDeviceInfoCommand**、**DeviceInfoField**、**IDeviceInfoParser**、**IAggregateDeviceInfoParser** 置于 **Infrastructure/DeviceAccess** 下相应子目录；**ProductProfile.AdbConfig** 类型为 **DeviceAdbConfig**，Domain 层（ProductProfile）引用该类型（Domain 依赖 Infrastructure 的 DeviceAccess.Configuration 等命名空间）。若要求 Domain 零依赖 Infrastructure，则可将上述配置 DTO 与解析接口放在 **Domain.DeviceAccess**，实现类仍放在 Infrastructure/DeviceAccess。 |

### 1.3 风险与实施注意

- **迁移顺序**：先删除 ProjectProfile / IProductProfileFactory 并让 RulePipelineExecutor 改为依赖 IDeviceAccessService + ProductProfile，再引入 DeviceAdbConfig 与 AdbDeviceService 实现，可避免中途双类型并存过久。
- **单元测试**：AdbAccessService / RulePipelineExecutor / ProcessCoordinator 现有单测需随接口与依赖调整；新 AdbDeviceService、Parser 需补充单测（含 Mock 配置与解析器）。
- **ProductRegistry 数据**：KM001/SOLTAG25 等需从现有 DeviceInfoCommandSet 迁移为 DeviceAdbConfig（BootstrapCommands、Commands 或 AggregateCommand、对应 Parser），过渡期可为 KM001 先配字段命令 + 简单 Parser，聚合命令与 Soltag Parser 后续补齐。

---

## 二、最新方案（合并 + DeviceAccess 子系统）

以下为 **Profile 合并** 与 **ADB 升级为 DeviceAccess 子系统** 的整合方案，供审核通过后执行。

### 2.1 目标架构（高层）

```
UI
 ↓
VerificationFlowService
 ↓
ProcessCoordinator
 ↓
RulePipelineExecutor
 ↓
IDeviceAccessService（设备访问对业务暴露接口）
 ↓
DeviceAccessSubsystem（Infrastructure/DeviceAccess）
 │   Configuration (DeviceAdbConfig / DeviceInfoCommand / AggregateDeviceInfoCommand)
 │   Session (DeviceSessionManager)
 │   Command (DeviceCommandExecutor)
 │   Parser (IDeviceInfoParser / IAggregateDeviceInfoParser 实现)
 │   Service (AdbDeviceService)
 ↓
ADB（进程调用）
```

- **RulePipelineExecutor** 仅依赖 **IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)**，不再依赖 IAdbAccessService 的 ReadDeviceInfoAsync(ProjectProfile)。
- **Legacy / 调试** 路径仍使用 **IAdbAccessService**（ReadDeviceSnAsync、ReadDeviceInfoAsync(CT) 等），与 IDeviceAccessService 并存，由 DI 分别注入。

### 2.2 Profile 与类型变更

| 变更 | 内容 |
|------|------|
| **删除** | `ProjectProfile`（Domain.Models）、`IProductProfileFactory`、`ProductProfileFactory`。 |
| **ProductProfile** | 保留并调整：`AdbCommands`（DeviceInfoCommandSet）改为 **`AdbConfig`**（**DeviceAdbConfig**）；保留 ProductCode、ProductDisplayName、Mode、EnableXxxCheck 等。 |
| **DeviceInfoCommandSet** | 由 **DeviceAdbConfig** 替代（见下）；旧类型可删除或标记废弃。 |
| **ProcessCoordinator.ProcessScanAsync** | 去掉第三参数 `ProjectProfile projectProfile`；内部仅通过 projectId 从 ProductRegistry 取 ProductProfile。 |

### 2.3 设备访问配置与解析模型（DeviceAccess 子系统）

- **DeviceAdbConfig**  
  - `List<string> BootstrapCommands`  
  - `AggregateDeviceInfoCommand AggregateCommand`（可选）  
  - `List<DeviceInfoCommand> Commands`（字段命令，与 AggregateCommand 二选一或组合使用，由执行逻辑约定）

- **DeviceInfoCommand**  
  - `DeviceInfoField Field`  
  - `string Command`  
  - `IDeviceInfoParser Parser`

- **AggregateDeviceInfoCommand**  
  - `string Command`  
  - `IAggregateDeviceInfoParser Parser`

- **DeviceInfoField** 枚举：DeviceSn, ChipId, WifiMac, AndroidVersion, BoardVersion, ChargeBoardVersion

- **IDeviceInfoParser**：`string Parse(string output)`  
- **IAggregateDeviceInfoParser**：`DeviceInfo Parse(string output)`（返回现有 Domain.Models.DeviceInfo）

- **DeviceInfo**：保持 **Domain.Models.DeviceInfo** 不变。

（类型放置约定见 1.2：推荐 DeviceAdbConfig / Parser 接口与枚举放 Domain.DeviceAccess 或 Infrastructure/DeviceAccess/Configuration，实现放 Infrastructure/DeviceAccess。）

### 2.4 设备访问服务接口

```csharp
public interface IDeviceAccessService
{
    Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile);
}
```

- **AdbDeviceService** 实现该接口；内部执行：EnsureSessionInitialized → Execute BootstrapCommands → 若有 AggregateCommand 则执行并解析，否则按 Commands 逐字段执行并解析，汇总为 DeviceInfo。
- **RulePipelineExecutor** 注入 **IDeviceAccessService**，在需要读设备信息时调用 **ReadDeviceInfoAsync(profile)**；profile 为当前 ProductProfile（来自 ProcessCoordinator）。当 profile.AdbConfig 为空或未配置有效命令时，行为与原「ADB 命令未配置」一致（抛异常或返回失败，由 RulePipelineExecutor 转为「adb 命令为空」等）。

### 2.5 子系统目录结构建议

```
Infrastructure
 └── DeviceAccess
      ├── Configuration
      │     DeviceAdbConfig.cs
      │     DeviceInfoCommand.cs
      │     AggregateDeviceInfoCommand.cs
      │     DeviceInfoField.cs
      ├── Parser
      │     IDeviceInfoParser.cs
      │     IAggregateDeviceInfoParser.cs
      │     （实现类如 TrimParser、Km001DeviceParser、SoltagDeviceParser 等）
      ├── Session
      │     DeviceSessionManager.cs
      ├── Command
      │     DeviceCommandExecutor.cs
      └── Service
            AdbDeviceService.cs
            IDeviceAccessService.cs（或接口放在 Services 层，实现在此）
```

（若采用「配置与解析接口在 Domain.DeviceAccess」则增加 Domain/DeviceAccess 下对应文件，Infrastructure/DeviceAccess 仅放实现与 Session/Command/Service。）

### 2.6 ProductRegistry 示例（新配置形状，修订后使用 ParserKey）

- **KM001**（字段命令示例）：  
  AdbConfig = new DeviceAdbConfig { BootstrapCommands = ["shell ylzero"], Commands = [DeviceInfoCommand(DeviceSn, "shell getprop ro.serialno", ParserKey: "Trim"), ...] }  
- **SOLTAG25**（聚合命令示例）：  
  AdbConfig = new DeviceAdbConfig { BootstrapCommands = ["shell ylzero"], AggregateCommand = new AggregateDeviceInfoCommand { Command = "shell get_device_info", ParserKey = "Soltag" } }

（配置仅存 ParserKey；Parser 由 ParserFactory 按 Key 提供。具体命令与 Key 以实际协议为准；迁移时先保证 KM001 可走字段命令路径并返回 DeviceInfo。）

### 2.7 迁移步骤（从当前代码，审核通过后执行）

| 步骤 | 内容 |
|------|------|
| **Step 1** | 删除 **ProjectProfile**（Domain.Models）；删除 **IProductProfileFactory**、**ProductProfileFactory**；ProcessCoordinator 去掉对工厂的注入与第三参数 projectProfile。 |
| **Step 2** | 新增 **DeviceAdbConfig**、**DeviceInfoCommand**、**AggregateDeviceInfoCommand**、**DeviceInfoField**、**IDeviceInfoParser**、**IAggregateDeviceInfoParser**（按 2.5 与 1.2 约定放置）；**ProductProfile** 增加 **AdbConfig**（类型 DeviceAdbConfig），**AdbCommands**（DeviceInfoCommandSet）废弃或删除。 |
| **Step 3** | 实现 **DeviceSessionManager**、**DeviceCommandExecutor**，以及至少一种 Parser 实现（如 TrimParser / 占位聚合解析）；实现 **AdbDeviceService** 与 **IDeviceAccessService**，内部按 2.3/2.4 执行 Bootstrap → Aggregate 或 Field 命令 → 解析为 DeviceInfo。 |
| **Step 4** | **RulePipelineExecutor** 改为依赖 **IDeviceAccessService**，调用 **ReadDeviceInfoAsync(profile)**，不再构造 ProjectProfile 或调用 IAdbAccessService.ReadDeviceInfoAsync(ProjectProfile)。 |
| **Step 5** | **ProductRegistry** 中 KM001/SOLTAG25 等改为使用 **DeviceAdbConfig** 配置（Bootstrap + Commands 或 AggregateCommand + 对应 Parser）；补充/调整单测，全量编译与测试通过。 |

---

## 三、评审建议结论（与评估一致）

建议本次评审通过以下方向：

1. **删除 ProjectProfile、IProductProfileFactory。**
2. **ADB 升级为 DeviceAccess 子系统**：RulePipeline 仅依赖 IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)；Legacy/调试仍用 IAdbAccessService。
3. **引入 DeviceAdbConfig**，替代 DeviceInfoCommandSet，支持 Bootstrap / Aggregate / Field Command。
4. **引入 Parser 体系**（IDeviceInfoParser、IAggregateDeviceInfoParser 及具体实现）。
5. **ProductProfile** 仅保留一种配置形状，使用 **AdbConfig**（DeviceAdbConfig）。

这样 **新增产品 = 新配置 + 新 Parser**，无需改规则链与业务流程代码。  

**该版本已被后续评审修订；执行请以「七、修订后最终方案」为准，待新方案评审通过后再执行。**

---

## 四、是否需要反问或反驳

经审阅，**无重大需反驳点**；方案与现有代码、评估结论一致，风险已在 1.3 中说明。以下两处建议在最终方案中**明确为约定**，避免实施时歧义：

| 类型 | 说明 |
|------|------|
| **澄清** | **Domain 是否可依赖 Infrastructure**：若接受 Domain 引用 `Infrastructure.DeviceAccess.Configuration` 中的 DeviceAdbConfig 等类型，则 ProductProfile.AdbConfig 直接使用该类型；若要求 Domain 零依赖 Infrastructure，则需在 Domain 下新增 `Domain.DeviceAccess`，将 DeviceAdbConfig、Parser 接口与 DeviceInfoField 枚举置于此处，实现类仍在 Infrastructure。**最终方案默认采用「Domain 可引用 Infrastructure/DeviceAccess 配置类型」**，以减少重复 DTO；若评审方要求零依赖，则改为 Domain.DeviceAccess 方案。 |
| **约定** | **Bootstrap 命令失败**：建议实现约定为「任一条 Bootstrap 命令执行失败（或超时），则本次 ReadDeviceInfo 视为失败」，与现有「ADB 未配置」区分开（未配置 = 抛异常/返回失败；Bootstrap 失败 = 执行阶段失败，可由 RulePipelineExecutor 统一为 ADB 读取失败类原因）。 |

无其他反问或反驳；以下为汇总后的**最终方案**，待评审通过后执行。

---

## 五、最终方案（待评审通过后执行）

### 5.1 方案范围与目标

- **Profile 合并**：删除 ProjectProfile、IProductProfileFactory；ProductProfile 为唯一产品配置类型，设备访问相关配置统一为 **AdbConfig**（DeviceAdbConfig）。
- **DeviceAccess 子系统**：Phase3 设备读取由 **IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)** 承担，支持 Bootstrap / 聚合命令 / 字段命令及 Parser 体系；规则链与设备协议解耦。
- **兼容**：Legacy 与调试路径继续使用 IAdbAccessService（ReadDeviceSnAsync、ReadDeviceInfoAsync(CT) 等），与 IDeviceAccessService 并存。

### 5.2 实施约定（澄清后固定）

- **DeviceInfo**：沿用 `Domain.Models.DeviceInfo`，Parser 输出与该类型一致。
- **AdbConfig 未配置**：profile.AdbConfig 为 null 或未配置有效命令时，IDeviceAccessService 抛异常或返回失败，RulePipelineExecutor 转为「adb 命令为空」等，不执行后续规则。
- **Bootstrap 失败**：任一条 Bootstrap 命令执行失败或超时，则本次 ReadDeviceInfo 视为失败（执行阶段失败，可与「未配置」区分）。
- **Domain 与 Infrastructure**：默认 ProductProfile（Domain.Product）引用 **Infrastructure/DeviceAccess** 中的 DeviceAdbConfig 等类型；若评审要求 Domain 不依赖 Infrastructure，则改为在 **Domain.DeviceAccess** 中定义配置 DTO 与解析接口，实现仍在 Infrastructure。
- **DI**：RulePipelineExecutor 注入 **IDeviceAccessService**；ProcessCoordinator 注入 **IDeviceAccessService**（用于创建或传入 RulePipelineExecutor），并视需保留 **IAdbAccessService**（Legacy/调试）；**移除 IProductProfileFactory 的注册与注入**。

### 5.3 架构与类型（与 2.1–2.3 一致）

- 调用链：UI → VerificationFlowService → ProcessCoordinator → RulePipelineExecutor → **IDeviceAccessService** → DeviceAccess 子系统（Configuration / Session / Command / Parser / Service）→ ADB。
- 删除：ProjectProfile，IProductProfileFactory，ProductProfileFactory；ProcessScanAsync 去掉第三参数。
- ProductProfile：AdbCommands 改为 **AdbConfig**（DeviceAdbConfig）；保留 ProductCode、ProductName、Mode、EnableXxxCheck 等。
- 设备访问模型：DeviceAdbConfig（BootstrapCommands、AggregateCommand、Commands）、DeviceInfoCommand、AggregateDeviceInfoCommand、DeviceInfoField、IDeviceInfoParser、IAggregateDeviceInfoParser；DeviceInfo 不变。

### 5.4 接口与实现

- **IDeviceAccessService**：`Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile)`；实现类 **AdbDeviceService**，逻辑：EnsureSessionInitialized → Execute BootstrapCommands → 若有 AggregateCommand 则执行并解析，否则按 Commands 逐字段执行并解析，汇总为 DeviceInfo。

### 5.5 目录与迁移步骤（与 2.5、2.7 一致，补 DI）

- **目录**：Infrastructure/DeviceAccess 下 Configuration、Parser、Session、Command、Service；IDeviceAccessService 与 AdbDeviceService 置于 Service；Legacy 的 IAdbAccessService 保留在 Services/Adb。
- **迁移步骤**：
  1. **Step 1**：删除 ProjectProfile、IProductProfileFactory、ProductProfileFactory；ProcessCoordinator 去掉工厂注入与 ProcessScanAsync 第三参数；**DI 中移除 IProductProfileFactory 注册**。
  2. **Step 2**：新增 DeviceAdbConfig、DeviceInfoCommand、AggregateDeviceInfoCommand、DeviceInfoField、IDeviceInfoParser、IAggregateDeviceInfoParser；ProductProfile 增加 AdbConfig，废弃或删除 AdbCommands（DeviceInfoCommandSet）。
  3. **Step 3**：实现 DeviceSessionManager、DeviceCommandExecutor、至少一种 Parser 实现、AdbDeviceService（IDeviceAccessService）；**DI 中注册 IDeviceAccessService → AdbDeviceService**。
  4. **Step 4**：RulePipelineExecutor 改为依赖 IDeviceAccessService，调用 ReadDeviceInfoAsync(profile)；**DI 中为 RulePipelineExecutor 注入 IDeviceAccessService**（ProcessCoordinator 若自行 new RulePipelineExecutor 则传入 IDeviceAccessService）。
  5. **Step 5**：ProductRegistry 中 KM001/SOLTAG25 等改为 DeviceAdbConfig；补充/调整单测；全量编译与测试通过。

### 5.6 评审结论（与第三节一致）

1. 删除 ProjectProfile、IProductProfileFactory。  
2. ADB 升级为 DeviceAccess 子系统；RulePipeline 仅依赖 IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)；Legacy/调试仍用 IAdbAccessService。  
3. 引入 DeviceAdbConfig，替代 DeviceInfoCommandSet，支持 Bootstrap / Aggregate / Field Command。  
4. 引入 Parser 体系（IDeviceInfoParser、IAggregateDeviceInfoParser 及实现）。  
5. ProductProfile 仅保留 AdbConfig（DeviceAdbConfig）。

**本最终方案已被后续评审要求修订；修订后方案见第六节、第七节。执行须待新方案评审通过后进行，禁止未审核就执行。**

---

## 六、评审结果与修订说明

### 6.1 评审结论摘要

| 项 | 结论 |
|----|------|
| 整体方向 | ✅ 正确 |
| 删除 ProjectProfile、ADB 升级子系统、DeviceAdbConfig、Parser 体系、Bootstrap、Aggregate、RulePipeline 解耦、新增产品=配置+Parser | ✅ 正确/必须 |
| 架构分层 | ⚠ 需修正（Domain 不得依赖 Infrastructure） |
| Parser 体系 | ⚠ 需调整（配置不持 Parser 实例，改为 ParserKey + ParserFactory） |
| Aggregate 策略 | ⚠ 需明确（禁止 Aggregate 与 Field 组合，二选一） |
| Bootstrap | ⚠ 需 Session 化（首次读取执行，后续不再执行） |

**最终建议**：通过架构评审，但需完成下列 **4 项修订** 后形成修订后方案，待新方案评审通过后再执行；**禁止未审核就执行**。

### 6.2 四项修订内容

| 修订项 | 原方案问题 | 修订要求 |
|--------|------------|----------|
| **1. 架构分层** | Domain.ProductProfile 引用 Infrastructure.DeviceAccess.DeviceAdbConfig，导致 Domain 依赖 Infrastructure（违反 Clean Architecture）。 | **必须**采用 **Domain.DeviceAccess**：配置 DTO 与解析**接口**放在 **Domain/DeviceAccess**（DeviceAdbConfig、DeviceInfoCommand、AggregateDeviceInfoCommand、DeviceInfoField、IDeviceInfoParser、IAggregateDeviceInfoParser）；**实现类**放在 **Infrastructure/DeviceAccess**（Parsers、DeviceCommandExecutor、DeviceSessionManager、AdbDeviceService）。依赖关系为 Infrastructure → Domain。 |
| **2. Parser 生命周期** | 配置中直接持有 Parser 实例（如 `Parser = new TrimParser()`），导致 Registry 创建 Parser、DI 无法管理、Parser 无法依赖 Logger、单测复杂。 | 配置**只存 ParserKey**（如 `ParserKey = "Trim"`）。**ParserFactory** 按 Key 提供 Parser（`ParserFactory.Get("Trim")`）。DeviceInfoCommand / AggregateDeviceInfoCommand 仅包含 `string ParserKey`，不包含 IDeviceInfoParser / IAggregateDeviceInfoParser 实例。Parser 由 DI 管理，可依赖 Logger，可测试，Registry 不创建 Parser。 |
| **3. Aggregate 与 Field 执行规则** | 文档写「二选一或组合」，组合会导致字段覆盖、重复读取等不确定行为。 | **明确执行策略**：若 `AggregateCommand != null` 则**仅**使用 Aggregate 路径；否则**仅**使用 FieldCommands 路径。**禁止** Aggregate 与 Field 同时使用。系统行为确定。 |
| **4. Bootstrap 与 Session** | Bootstrap 执行时机未明确，若每次读取都执行会变慢。 | Bootstrap 为 **Session 级别**：**DeviceSessionManager** 在**第一次**读取设备时执行 Bootstrap，并标记 session ready；**后续**同 session 内不再执行 Bootstrap。现有 `EnsureAdbShellWarmedUpAsync`、`CheckMultipleDevices` 等职责**归入 DeviceSessionManager**（ADB 连接管理、设备数量检查、Bootstrap 执行、Shell warmup）。 |

---

## 七、修订后最终方案（待新方案评审通过后再执行；禁止未审核就执行）

以下方案已按第六节 4 项修订完成矫正。**仅在新方案评审通过后执行，禁止未审核就执行。**

### 7.1 方案范围与目标（不变）

- **Profile 合并**：删除 ProjectProfile、IProductProfileFactory；ProductProfile 为唯一产品配置类型，设备访问配置统一为 **AdbConfig**（DeviceAdbConfig）。
- **DeviceAccess 子系统**：Phase3 设备读取由 **IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)** 承担，支持 Bootstrap（Session 级）/ 聚合命令 / 字段命令及 Parser 体系；规则链与设备协议解耦。
- **兼容**：Legacy 与调试路径继续使用 IAdbAccessService，与 IDeviceAccessService 并存。

### 7.2 分层与依赖（修订 1：Domain 不依赖 Infrastructure）

**正确依赖关系**：Infrastructure → Domain（Domain 不引用 Infrastructure）。

**Domain 层**：

```
Domain
 ├── Product
 │    ProductProfile（AdbConfig 类型为 Domain.DeviceAccess.DeviceAdbConfig）
 │
 └── DeviceAccess
      DeviceAdbConfig
      DeviceInfoCommand
      AggregateDeviceInfoCommand
      DeviceInfoField（枚举）
      IDeviceInfoParser
      IAggregateDeviceInfoParser
```

**Infrastructure 层**（实现与运行时组件）：

```
Infrastructure
 └── DeviceAccess
      ├── Session
      │     DeviceSessionManager
      ├── Command
      │     DeviceCommandExecutor
      ├── Parser
      │     ParserFactory
      │     TrimParser、SoltagParser 等实现
      └── Service
            AdbDeviceService
```

- **ProductProfile.AdbConfig** 类型为 **Domain.DeviceAccess.DeviceAdbConfig**，仅引用 Domain 内类型。
- **DeviceSessionManager**：负责 ADB 连接管理、设备数量检查（CheckMultipleDevices）、Bootstrap 执行（Session 级，见 7.4）、Shell warmup（EnsureAdbShellWarmedUpAsync 逻辑迁入）。

### 7.3 配置与 Parser（修订 2：ParserKey + ParserFactory）

- **配置只存 ParserKey**，不持有 Parser 实例：
  - **DeviceInfoCommand**：`DeviceInfoField Field`、`string Command`、**`string ParserKey`**（如 `"Trim"`），**不**包含 `IDeviceInfoParser Parser`。
  - **AggregateDeviceInfoCommand**：`string Command`、**`string ParserKey`**（如 `"Soltag"`），**不**包含 `IAggregateDeviceInfoParser Parser`。
- **ParserFactory**（位于 Infrastructure/DeviceAccess/Parser）：按 Key 返回 Parser 实例，如 `IDeviceInfoParser Get(string key)`、`IAggregateDeviceInfoParser GetAggregate(string key)`。Parser 实现由 DI 注册，ParserFactory 可注入容器中注册的 Parser 字典或工厂委托。
- **ProductRegistry / DeviceAdbConfig** 仅构造配置 DTO（含 ParserKey 字符串），**不**创建任何 Parser 实例。

### 7.4 Aggregate 与 Field 执行规则（修订 3：禁止组合）

**唯一执行策略**（确定、不可组合）：

```
if profile.AdbConfig?.AggregateCommand != null
    使用 Aggregate 路径：执行 AggregateCommand.Command → ParserFactory.GetAggregate(AggregateCommand.ParserKey) → Parse → DeviceInfo
else
    使用 FieldCommands 路径：对 Commands 逐条执行 → ParserFactory.Get(cmd.ParserKey) → 按字段汇总为 DeviceInfo
```

**禁止**：同时配置 AggregateCommand 与 Commands 并混合执行。若两者都配置，**约定仅按 AggregateCommand 生效**（或在校验/文档中约定只配其一）。

### 7.5 Bootstrap 与 DeviceSessionManager（修订 4：Session 级 Bootstrap）

- **Bootstrap 为 Session 级别**：
  - **第一次**对设备进行读取时：**DeviceSessionManager** 执行 **BootstrapCommands**（如 `shell ylzero`），执行成功后标记 **session ready**。
  - **后续**同 session 内再次调用 ReadDeviceInfo 时：**不再**执行 Bootstrap，直接进入 Aggregate 或 Field 命令执行。
- **DeviceSessionManager 职责**（明确）：
  - ADB 连接管理
  - 设备数量检查（对应现有 CheckMultipleDevices）
  - Bootstrap 执行（仅首次，session 内复用）
  - Shell warmup（对应现有 EnsureAdbShellWarmedUpAsync）
- 现有 **AdbAccessService** 中的 EnsureAdbShellWarmedUpAsync、CheckMultipleDevices 等逻辑迁移至 **DeviceSessionManager**，由 **AdbDeviceService** 通过 DeviceSessionManager 使用。

### 7.6 完整执行流程（修订后）

```
RulePipelineExecutor
        ↓
IDeviceAccessService.ReadDeviceInfoAsync(profile)
        ↓
AdbDeviceService
        ↓
DeviceSessionManager（若 session 未 ready）
        ↓
执行 BootstrapCommands（仅首次），标记 session ready
        ↓
若 profile.AdbConfig.AggregateCommand != null
      ↓
DeviceCommandExecutor 执行聚合命令
      ↓
ParserFactory.GetAggregate(ParserKey) → Parser.Parse(output) → DeviceInfo
否则
      ↓
DeviceCommandExecutor 按 FieldCommands 逐条执行
      ↓
ParserFactory.Get(cmd.ParserKey) → 逐字段解析 → 汇总为 DeviceInfo
```

### 7.7 其他约定（不变）

- **DeviceInfo**：沿用 `Domain.Models.DeviceInfo`；Parser 输出与该类型一致。
- **AdbConfig 未配置**：profile.AdbConfig 为 null 或未配置有效命令时，IDeviceAccessService 抛异常或返回失败，RulePipelineExecutor 转为「adb 命令为空」等。
- **Bootstrap 失败**：任一条 Bootstrap 命令失败或超时，则本次 ReadDeviceInfo 视为失败（Session 可保持未 ready，下次再试或由调用方重试）。
- **DI**：RulePipelineExecutor、ProcessCoordinator 注入 IDeviceAccessService；移除 IProductProfileFactory；Parser 实现与 ParserFactory 由 DI 注册。

### 7.8 迁移步骤（按修订后方案）

1. **Step 1**：删除 ProjectProfile、IProductProfileFactory、ProductProfileFactory；ProcessCoordinator 去掉工厂注入与 ProcessScanAsync 第三参数；DI 移除 IProductProfileFactory 注册。
2. **Step 2**：在 **Domain/DeviceAccess** 新增 DeviceAdbConfig、DeviceInfoCommand（含 ParserKey）、AggregateDeviceInfoCommand（含 ParserKey）、DeviceInfoField、IDeviceInfoParser、IAggregateDeviceInfoParser；ProductProfile 增加 AdbConfig（类型为 Domain.DeviceAccess.DeviceAdbConfig），废弃或删除 AdbCommands。
3. **Step 3**：在 **Infrastructure/DeviceAccess** 实现 DeviceSessionManager（含 Bootstrap Session 化、Shell warmup、设备数检查）、DeviceCommandExecutor、**ParserFactory**、至少一种 Parser 实现（如 TrimParser）；实现 AdbDeviceService（IDeviceAccessService），内部按 7.4、7.5、7.6 执行；DI 注册 IDeviceAccessService、ParserFactory 及 Parser 实现。
4. **Step 4**：RulePipelineExecutor 改为依赖 IDeviceAccessService，调用 ReadDeviceInfoAsync(profile)；DI 为 RulePipelineExecutor 注入 IDeviceAccessService。
5. **Step 5**：ProductRegistry 中 KM001/SOLTAG25 等改为使用 DeviceAdbConfig（仅填 ParserKey，不填 Parser 实例）；补充/调整单测；全量编译与测试通过。

### 7.9 修订后方案结论

1. 删除 ProjectProfile、IProductProfileFactory。  
2. ADB 升级为 DeviceAccess 子系统；RulePipeline 仅依赖 IDeviceAccessService；Legacy/调试仍用 IAdbAccessService。  
3. **Domain.DeviceAccess** 放置配置与解析接口；**Infrastructure.DeviceAccess** 放置实现；**禁止 Domain 依赖 Infrastructure**。  
4. 配置仅存 **ParserKey**；**ParserFactory** 提供 Parser；禁止在 Registry/配置中创建 Parser 实例。  
5. **Aggregate 与 Field 二选一**，禁止组合；执行策略见 7.4。  
6. **Bootstrap Session 化**；DeviceSessionManager 负责连接、设备数、Bootstrap（首次）、Shell warmup。

**本修订后方案待新方案评审通过后再执行；禁止未审核就执行。**
