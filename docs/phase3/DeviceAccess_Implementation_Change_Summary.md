# DeviceAccess 子系统实施与 Profile 合并 — 变更总结（供评审）

## 一、编译与测试结果

- **编译**：使用 `build\build.cmd SnVerify\SnVerify.csproj Debug` 通过（MSBuild / Visual Studio）。
- **单元测试**：`dotnet test SnVerify.Tests\SnVerify.Tests.csproj` 共 **339** 个用例，**全部通过**。

---

## 二、变更范围概览

本次实施完成：

1. **Profile 合并**：删除 `ProjectProfile`、`IProductProfileFactory`、`ProductProfileFactory`；产品配置统一为 `ProductProfile`，设备访问配置由 `AdbConfig`（`DeviceAdbConfig`）承载。
2. **DeviceAccess 子系统**：新增 Domain.DeviceAccess（配置与解析接口）、Infrastructure.DeviceAccess（Session / Command / Parser / Service），Phase3 设备读取由 `IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)` 提供，规则链与 ADB 协议解耦。
3. **调用链与 DI**：`RulePipelineExecutor` 改为依赖 `IDeviceAccessService`；`ProcessCoordinator` 去掉第三参数与工厂，可选注入 `IDeviceAccessService`；`ServiceFactory` 组装 DeviceAccess 并注入。

---

## 三、删除与废弃

| 类型 | 说明 |
|------|------|
| **Domain/Models/ProjectProfile.cs** | 已删除。 |
| **Services/Product/IProductProfileFactory.cs** | 已删除。 |
| **Services/Product/ProductProfileFactory.cs** | 已删除。 |
| **IAdbAccessService.ReadDeviceInfoAsync(ProjectProfile)** | 已移除；Phase3 改用 `IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)`。 |
| **ProcessCoordinator** 第三参数 `ProjectProfile projectProfile` | 已移除；入口为 `ProcessScanAsync(string sn, string projectId)`。 |
| **ProcessCoordinator** 依赖 `IProductProfileFactory` | 已移除。 |

---

## 四、新增与修改

### 4.1 Domain 层

**新增目录与文件（Domain/DeviceAccess/）：**

| 文件 | 说明 |
|------|------|
| **DeviceInfoField.cs** | 枚举：DeviceSn, ChipId, WifiMac, AndroidVersion, BoardVersion, ChargeBoardVersion。 |
| **DeviceInfoCommand.cs** | 单字段命令配置：Field、Command、**ParserKey**（不存 Parser 实例）。 |
| **AggregateDeviceInfoCommand.cs** | 聚合命令配置：Command、**ParserKey**。 |
| **DeviceAdbConfig.cs** | BootstrapCommands、AggregateCommand、Commands；执行策略为 Aggregate 与 Field 二选一。 |
| **IDeviceInfoParser.cs** | `string Parse(string output)`。 |
| **IAggregateDeviceInfoParser.cs** | `DeviceInfo Parse(string output)`。 |
| **IParserFactory.cs** | `Get(string key)`、`GetAggregate(string key)`。 |

**修改：**

| 文件 | 变更 |
|------|------|
| **Domain/Product/ProductProfile.cs** | `AdbCommands`（DeviceInfoCommandSet）改为 **AdbConfig**（DeviceAdbConfig）；引用 `Domain.DeviceAccess`。 |

### 4.2 Infrastructure 层

**新增目录与文件（Infrastructure/DeviceAccess/）：**

| 路径 | 说明 |
|------|------|
| **Session/DeviceSessionManager.cs** | ADB 连接与 Shell warmup；Bootstrap 仅**首次**读取时执行并标记 session ready，后续同 session 不再执行。 |
| **Command/DeviceCommandExecutor.cs** | 执行单条 ADB 命令并返回标准输出。 |
| **Parser/TrimParser.cs** | 实现 `IDeviceInfoParser`，对输出 Trim；注册 Key 为 ParserKeys.Field.Trim。 |
| **Parser/ParserFactory.cs** | 实现 `IParserFactory`，按 Key 返回已注册的 Parser（配置仅存 ParserKey，不创建 Parser 实例）。 |
| **Service/AdbDeviceService.cs** | 实现 `IDeviceAccessService`；若 `AdbConfig == null` 或未配置有效命令则抛「ADB 命令未配置」；若同时配置 Aggregate 与 Commands 则抛异常（禁止混用）。 |

### 4.3 Service 层

| 文件 | 说明 |
|------|------|
| **Services/DeviceAccess/IDeviceAccessService.cs** | 新增接口：`Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile)`。 |

### 4.4 调用方与 DI

| 位置 | 变更 |
|------|------|
| **Services/Rules/RulePipelineExecutor.cs** | 依赖由 `IAdbAccessService` 改为 **IDeviceAccessService**；内部调用 `ReadDeviceInfoAsync(profile)`，不再构造 `ProjectProfile`；对「ADB 命令未配置」类异常捕获并返回「ADB 命令为空」。 |
| **Services/Coordination/ProcessCoordinator.cs** | 移除对 `IProductProfileFactory` 的依赖与第三参数；新增可选 **IDeviceAccessService**，在未注入 `IRulePipelineExecutor` 时用于构建默认 `RulePipelineExecutor`。 |
| **Services/Coordination/IProcessCoordinator.cs** | `ProcessScanAsync(string sn, string projectId, ProjectProfile projectProfile = null)` 改为 **ProcessScanAsync(string sn, string projectId)**。 |
| **Infrastructure/VerificationFlowServiceFactory.cs** | 创建 `ProcessCoordinator` 时不再传入 `productProfileFactory`。 |
| **Infrastructure/ServiceFactory.cs** | 创建 DeviceSessionManager、DeviceCommandExecutor、ParserFactory（注册 Trim）、**AdbDeviceService**；**RulePipelineExecutor** 改为注入 **IDeviceAccessService**。 |
| **Infrastructure/Product/ProductRegistry.cs** | SOLTAG25 使用 `AdbConfig = null`；KM001 使用 **DeviceAdbConfig**（BootstrapCommands: shell ylzero；Commands: DeviceSn + AndroidVersion，ParserKey = ParserKeys.Field.Trim），**仅存 ParserKey，须使用 ParserKeys 常量**。 |

### 4.5 工程文件

- **SnVerify.csproj**：移除对已删文件（ProjectProfile、IProductProfileFactory、ProductProfileFactory）的 Compile；新增 Domain/DeviceAccess 各文件、Infrastructure/DeviceAccess 各文件、Services/DeviceAccess/IDeviceAccessService.cs。

---

## 五、单元测试变更与新增

| 测试类/文件 | 变更内容 |
|-------------|----------|
| **ProcessCoordinatorPhase3Tests** | Mock 由 `IAdbAccessService` 改为 **IDeviceAccessService**；`ReadDeviceInfoAsync(It.IsAny<ProjectProfile>)` 改为 `ReadDeviceInfoAsync(It.IsAny<ProductProfile>)`；构造 ProcessCoordinator 时传入 **productRegistry**、**deviceAccessService**；`ProcessScanAsync(StickerSn, ProjectId)` 仅两参数；参数未配置时 Verify 改为对 **IDeviceAccessService**。 |
| **RulePipelineExecutorTests** | Mock 由 `IAdbAccessService` 改为 **IDeviceAccessService**；CreatePhase3Profile 使用 **AdbConfig = null**；所有 Setup/Verify 使用 **ProductProfile**。 |
| **VerificationFlowServiceTests** | `ProcessScanAsync(TestSn, TestProjectId, null)` 改为 **ProcessScanAsync(TestSn, TestProjectId)**。 |
| **Infrastructure/DeviceAccess/ParserFactoryTests.cs** | 新增：Get/GetAggregate 已注册与未注册、Key 空校验。 |
| **Infrastructure/DeviceAccess/TrimParserTests.cs** | 新增：Parse 对 Trim 与 null 的处理。 |
| **Infrastructure/DeviceAccess/AdbDeviceServiceTests.cs** | 新增：profile 为 null、AdbConfig 为 null 时抛异常；FieldCommands 配置下执行并返回 DeviceInfo（Mock IProcessRunner）。 |
| **Infrastructure/DeviceAccess/DeviceSessionManagerTests.cs** | 新增：EnsureSessionReadyAsync 在 config 为 null 时完成；Bootstrap 配置下仅执行一次。 |

---

## 六、架构约定遵守情况

| 约定 | 状态 |
|------|------|
| **Domain 不依赖 Infrastructure** | ✅ ProductProfile、DeviceAdbConfig 等均在 Domain；实现位于 Infrastructure。 |
| **配置仅存 ParserKey，不存 Parser 实例** | ✅ DeviceInfoCommand / AggregateDeviceInfoCommand 仅含 `string ParserKey`；ProductRegistry 仅配置 Key。 |
| **Aggregate 与 Field 二选一，禁止混用** | ✅ AdbDeviceService 在两者均配置时抛异常；执行分支仅选其一。 |
| **Bootstrap Session 级** | ✅ DeviceSessionManager 首次读取执行 Bootstrap 并标记 session ready，后续同 session 不再执行。 |
| **Legacy/调试仍用 IAdbAccessService** | ✅ ReadDeviceSnAsync、ReadDeviceInfoAsync(CT)、CheckMultipleDevices 保留；**GetDeviceSNAsync** 已标记 `[Obsolete]`，建议改用 ReadDeviceSnAsync，待 Step 2 再删除；Phase3 路径单独使用 IDeviceAccessService。ylzero 执行已统一为 RunYlzeroAsync（仅执行 `shell ylzero`，无 deviceId）+ IsYlzeroResultAcceptableForSnRead；ReadDeviceSnAsync 与 ReadDeviceInfoAsync 均使用该判定逻辑，仅在不接受时打日志或返回失败。 |

---

## 七、后续可做（未在本次实施）

- 为 SOLTAG25 或其它产品配置 **AggregateCommand** 及对应 **IAggregateDeviceInfoParser** 实现（如 SoltagDeviceParser）。
- 在 DI 或配置中注册更多 Parser（如按 Key "Soltag" 注册聚合解析器）。
- 视需要删除或标记废弃 **DeviceInfoCommandSet**（当前 ProductProfile 已不再使用，仅 ProductRegistry 未再引用其类型）。

---

## 八、评审检查清单

- [ ] 编译通过（`build\build.cmd SnVerify\SnVerify.csproj Debug`）
- [ ] 单元测试全部通过（339）
- [ ] Domain 无对 Infrastructure 的引用
- [ ] 配置中无 Parser 实例，仅 ParserKey
- [ ] ProcessScanAsync 仅两参数，无 ProjectProfile
- [ ] Phase3 设备读取路径使用 IDeviceAccessService + ProductProfile

---

**文档版本**：与 DeviceAccess 子系统实施及 Profile 合并实现一致。  
**日期**：按实施完成日。
