# Phase4 · KM008 与架构修正 — 执行技术总结（供审核）

| 项 | 内容 |
|---|------|
| 状态 | 实现已完成，待代码评审与在 **Visual Studio / MSBuild（WPF）** 下全量测试确认 |
| 依据方案 | [Phase4_KM008_ProjectCategory_Technical_Proposal.md](./Phase4_KM008_ProjectCategory_Technical_Proposal.md) |
| 日期 | 2026-04-07（执行会话） |

---

## 1. 范围对照

| 方案要求 | 执行情况 |
|----------|----------|
| 新增 `KM008`，不破坏 `KM001` / Legacy | 已增加 `ProductRegistry` 项；`KM001` 配置未改语义；Legacy 导出路径不变 |
| KM008：SN、Android、WifiMac 展示；无 ChipId/Board/Charge 校验 | `EnableChipIdCheck=false` 等；规则链按 Profile 跳过 ChipId；版本校验按 Profile 开关忽略 Board/Charge（避免切换产品后期望残留误报） |
| 独立 Parser：`android, sn, wifiMac` | `Km008AndroidVersionAggregateParser` + `ParserKeys.Aggregate.Km008AndroidVersion` |
| `RulePipelineExecutor`：`EnableChipIdCheck` 控制 ChipId，且关闭时 `ChipId=null` | 已实现 |
| 导出去硬编码 + `ExportContext.ProductCode` + 双 Registry 校验 | `Km001SessionExporter` 使用 `context.ProductCode`；`IProductRegistry` + `IProductExportRegistry` |
| UI Profile 驱动 | `ShowChipIdColumn` / `ShowBoardVersion` / `ShowChargeVersion` / `ShowWifiMac` + `MainWindow` 布局 |
| 测试：KM008 / KM001 回归 | 单元测试 + 集成测试类见下文 |

---

## 2. 新增与修改的文件清单

### 2.1 新增（源码）

| 路径 | 说明 |
|------|------|
| `SnVerify/Infrastructure/DeviceAccess/Parser/Km008AndroidVersionAggregateParser.cs` | KM008 三列聚合解析；核心字段非空抛 `AggregateProtocolException` |
| `SnVerify.Tests/Infrastructure/DeviceAccess/Km008AndroidVersionAggregateParserTests.cs` | Parser 单测 |
| `SnVerify.Tests/Services/Storage/SessionExporterFactoryTests.cs` | 工厂：Phase3 / Legacy 路由 |
| `SnVerify.Tests/Integration/Phase4Km008MainFlowIntegrationTests.cs` | Parser + 真实 `ProductRegistry` + `VersionVerificationService` + `RulePipelineExecutor` |

> **注意**：主工程为非 SDK 风格 csproj，已在 `SnVerify/SnVerify.csproj` 中 **显式** `Compile Include` 上述 Parser 文件。

### 2.2 修改（核心）

| 路径 | 要点 |
|------|------|
| `SnVerify/Domain/DeviceAccess/Parsing/ParserKeys.cs` | `Km008AndroidVersion` |
| `SnVerify/Infrastructure/Product/ProductRegistry.cs` | 注册 `KM008` |
| `SnVerify/Infrastructure/Export/ProductExportRegistry.cs` | `KM008` 导出列独立配置：移除 ChipId/Board/Charge 相关 5 列（共 9 列） |
| `SnVerify/Infrastructure/ServiceFactory.cs` | 注册 KM008 Parser |
| `SnVerify/Services/Rules/RulePipelineExecutor.cs` | ChipId 分支 + `ChipId` 清零 |
| `SnVerify/Domain/Export/ExportContext.cs` | `ProductCode` |
| `SnVerify/Services/Storage/Km001SessionExporter.cs` | 构造注入 `IProductRegistry`；导出前校验 ProductCode / Profile / ExportProfile |
| `SnVerify/Services/Storage/SessionExporterFactory.cs` | 注入 `IProductRegistry`；`Mode==Phase3` → 同一 Phase3 Exporter |
| `SnVerify/Services/Storage/ExportAggregationService.cs` | 两处 `ExportContext` 赋值 `ProductCode`；默认工厂传入 `ProductRegistryAdapter` |
| `SnVerify/Services/Storage/ISessionExporterFactory.cs` | 接口注释更新 |
| `SnVerify/ViewModels/MainViewModel.cs` | `Show*` 四个属性，在 `UpdateCurrentProductProfile` 中同步 |
| `SnVerify/MainWindow.xaml` | Phase3 SN 双布局（含/不含芯片列）；版本区 Board/Charge 行 Visibility |
| `SnVerify/Services/Verification/IVersionVerificationService.cs` | `VerifyAsync` 增加 `ProductProfile` 可选参数（Board/Charge 按开关跳过） |
| `SnVerify/Services/Verification/VersionVerificationService.cs` | 按 Profile 开关决定是否比对 Board/Charge |

### 2.3 测试与文档

| 路径 | 要点 |
|------|------|
| `SnVerify.Tests/Services/RulePipelineExecutorTests.cs` | KM008：Chip 清零、不查 Chip 重复 |
| `SnVerify.Tests/Infrastructure/ProductRegistryTests.cs` | `KM008` Profile / `GetProductCodes` |
| `SnVerify.Tests/Infrastructure/ProductExportRegistryTests.cs` | `KM008`：列裁剪（9 列）且不包含 ChipId/Board/Charge |
| `SnVerify.Tests/Services/Storage/Km001SessionExporterTests.cs` | `ProductCode`、缺省异常、KM008 导出 9 列 |
| `SnVerify.Tests/ViewModels/ProductUIRenderingTests.cs` | KM008 `Show*` 行为 |
| `SnVerify.Tests/Services/VersionVerificationServiceTests.cs` | Board/Charge 按 Profile 开关跳过（覆盖 KM008 期望残留场景） |

---

## 3. 行为摘要（审核用）

### 3.1 ADB 与解析

- **命令**：与 `KM001` 相同 — `shell dumpsys window getmcuversion`。
- **协议**：第二行 CSV 为 **`android, sn, wifiMac`**（3 列）；与 KM001 的 6 列协议 **分离实现**。
- **WifiMac**：非空时 `ToUpperInvariant()`；允许空字符串（不强制 MAC 非空）。

### 3.2 规则链顺序（未改阶段语义，仅增加 Profile 分支）

1. Parameter 非空  
2. 读 `DeviceInfo`  
3. 若 `!EnableChipIdCheck` → **`ChipId = null`**  
4. 设备 SN 非空、贴纸与设备 SN 一致  
5. SN 批次 PASS 唯一（仍为 **`IsStickerSnPassedInBatchAsync`**，与现网一致）  
6. **仅当 `EnableChipIdCheck`**：ChipId F50 + ChipId 批次唯一  
7. `VersionVerificationService`（Android 仍按非空 Expected 比对；Board/Charge 同时受非空 Expected + Profile 开关控制）

### 3.3 导出

- Phase3 导出必须在 **`ExportContext.ProductCode`** 中提供已在 **`ProductRegistry`** 注册的代码，且 **`ProductExportRegistry`** 须有对应列配置。
- **禁止**静默回退到 KM001；缺列配置 → **`InvalidOperationException`**。
- Legacy Session：`productCode` 为空时仍走 **`LegacySessionExporter`**，不依赖 `ProductCode` 字段。
- **KM008 列裁剪**：导出为 9 列，移除 ChipId/Board/Charge 相关 5 列（与 KM008 “不测芯片/充电板版本”一致）。

### 3.4 UI

- **KM008**：无芯片列布局；无 Board/Charge 版本行；显示 Android 目标/设备行与 WifiMac（在 `EnableWifiMacCheck==true` 时）。
- **KM001**：原「含芯片 + 三行版本」保留，由 `Show*` 与 `IsPhase3Product` 共同控制。

---

## 4. 测试策略说明

- **TDD 取向**：先补充用例（Parser、规则链、导出、工厂、UI Show*、集成链），再对齐实现。
- **集成测试**：`Phase4Km008MainFlowIntegrationTests` 使用真实 `ProductRegistry.Get("KM008")` 与 **`VersionVerificationService`**，规则链其余依赖 Mock，覆盖「解析 → PASS」主链路。

---

## 5. 构建与验证说明（审核必读）

- 主项目为 **.NET Framework 4.7.2 + WPF**（非 SDK 风格），在 **仅 `dotnet test` CLI、未生成 XAML 代码隐藏类** 的环境中可能出现 `InitializeComponent` 等编译错误。
- **建议审核步骤**：在 **Visual Studio** 中打开解决方案 → **全部重新生成** → 运行 **`SnVerify.Tests`** 全套测试。
- 人工抽查建议仍按方案：**KM008** 选机、扫码、Session 参数、导出 xlsx 列结构与 Fail 路径。

---

## 6. 未纳入或后续可选项

- 未改数据库结构（与方案一致）。
- 未改 MES 行为。
- 若历史 Session 在库中 **`ProductCode` 为空** 且业务上实为 Phase3，导出仍可能走 Legacy（既有数据模型限制）；若需纠偏需单独数据修复或产品决策。

---

## 7. 修订记录

| 日期 | 说明 |
|------|------|
| 2026-04-07 | 初稿：对应 Phase4 KM008 与 Phase3 导出/规则/UI 修正落地总结 |
| 2026-04-07 | 修订：版本校验按 `ProductProfile` 开关忽略 Board/Charge；KM008 导出列独立配置为 9 列（去除 ChipId/Board/Charge） |

审核意见可记于本文末尾新增「审核结论」表格或独立 `Phase4_KM008_Review_Response.md`。
