# Phase3 按 ProductCode 导出策略 — 变更总结（供评审）

> 对应方案文档：`Phase3_ProductCode_Export_Strategy_Proposal.md`（评审通过，按该方案实施）

---

## 一、变更概述

在现有「按项目 / 按订单」导出流程不变的前提下，**按 Session 所属 Product 的 ProductCode 选择不同导出实现**：

- **SOLTAG25 / 未知或空 ProductCode**：沿用原有逻辑（Legacy），支持 VerifyType Filter，仅 PASS/FAIL 两 Sheet、8 列。
- **KM001**：新逻辑，不按 Filter 过滤、导出全量记录；生成 **Summary Sheet**（含 SessionId、SessionName、Total、Pass、Fail、PassRate、FailRate、**ExportTime**）+ PASS/FAIL 两 Sheet（**14 列**，含目标/设备主板版本、目标/设备充电板版本及 WifiMac、ChipId 等）。

实现方式：引入 **ISessionExporter** + **ExportContext** + **ISessionExporterFactory**，聚合层对每个 Session 查 ProductCode → 选 Exporter → 调用 `ExportAsync(context)`，不修改现有 `StorageService.ExportBySessionAsync` 行为。

---

## 二、方案文档变更（评审结论已写入）

- **文件**：`docs/phase3/Phase3_ProductCode_Export_Strategy_Proposal.md`
- **结论**：整体方法同意，评审通过，按本方案实施。
- **落实的评审微调**：
  - **Factory 签名**：`GetExporter(string? productCode)`，内部用 `productCode?.Trim()` + `StringComparison.OrdinalIgnoreCase` 判断 KM001，兼容 DB 中大小写/空格不一致。
  - **Summary Sheet**：增加 **ExportTime** 列（导出时刻，便于产线确认报表时间）。
  - **Legacy 空 Filter**：`context.Filter ?? ExportRecordFilter.All`，避免 NRE。
  - **未来扩展示例**：文档末新增「十五、未来扩展示例」（ProductCode → Exporter 表 + 扩展两步说明）。

---

## 三、代码变更清单

### 3.1 领域层（Domain）

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `SnVerify/Domain/Export/ExportContext.cs` | **新增** | 导出上下文 DTO：SessionId、SessionName、OutputDirectory、Filter；未来可扩展 ZipName、ProjectName、OrderId、ExportTime 等。 |

### 3.2 存储接口与实现（Services/Storage）

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `IStorageService.cs` | **修改** | 新增 `Task<string> GetProductCodeBySessionIdAsync(int sessionId)`；无匹配或 ProductCode 为空时返回 null，聚合层回退 Legacy。 |
| `StorageService.cs` | **修改** | 实现 `GetProductCodeBySessionIdAsync`（Session → Order → Product → ProductCode）；**未修改** `ExportBySessionAsync`。 |
| `ISessionExporter.cs` | **新增** | 接口：`Task ExportAsync(ExportContext context)`；Exporter 无状态。 |
| `LegacySessionExporter.cs` | **新增** | 委托 `_storage.ExportBySessionAsync(context.SessionId, context.OutputDirectory, context.Filter ?? ExportRecordFilter.All)`。 |
| `Km001SessionExporter.cs` | **新增** | 依赖 `IProductExportRegistry`、`IExportValueResolver`；用 `GetTestRecordsBySessionAsync` 取全量记录；按 ProductExportProfile 写 Summary（列头资源化）+ PASS/FAIL 两 Sheet（14 列，由 DefaultExportValueResolver 取值）；FAIL 按 (StickerSN, DeviceSN) 去重。 |
| `ISessionExporterFactory.cs` | **新增** | 接口：`ISessionExporter GetExporter(string? productCode)`。 |
| `SessionExporterFactory.cs` | **新增** | 实现：`string.Equals(productCode?.Trim(), "KM001", StringComparison.OrdinalIgnoreCase)` → KM001 Exporter，否则 Legacy。 |

### 3.3 聚合层

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `ExportAggregationService.cs` | **修改** | 构造函数增加可选参数 `ISessionExporterFactory exporterFactory = null`（null 时内部 `new SessionExporterFactory(storage)`）；按订单/按项目循环内：`GetProductCodeBySessionIdAsync(s.Id)` → `GetExporter(productCode)` → 构造 ExportContext → `exporter.ExportAsync(context)`；ZIP 与日志逻辑不变。 |

### 3.4 工程与 DI

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `SnVerify.csproj` | **修改** | 增加 Compile：`Domain\Export\*`（含 ExportFieldId、ExportColumnDefinition、ProductExportProfile）、`Infrastructure\Export\*`、`Services\Storage\Export\*`、`Km001SessionExporter.cs`、`ISessionExporterFactory.cs`、`SessionExporterFactory.cs`。 |
| `ServiceFactory.cs` | **未改** | 仍使用 `new ExportAggregationService(storageService, loggingService, loggingService)`；未显式注册 `ISessionExporterFactory`，由聚合服务内部创建默认工厂。 |

---

## 四、测试变更

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `SnVerify.Tests/Services/ExportAggregationServiceTests.cs` | **修改** | 在 4 个导出相关用例中为 `GetProductCodeBySessionIdAsync` 增加 Mock：`ReturnsAsync((string)null)`，使聚合层走 LegacyExporter、继续使用现有 `ExportBySessionAsync` Mock，保证编译与行为一致。 |

- **已补充**：ProductExportRegistryTests、DefaultExportValueResolverTests、Km001SessionExporterTests；SessionExporterFactory 通过 ExportAggregationService 集成覆盖。

---

## 五、文档变更

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `docs/07_Technical_Architecture_and_Dev_Guide.md` | **修改** | §4.4 导出与统计视图：补充「按 ProductCode 的导出策略」、ExportContext / Factory / Exporter 的职责说明。 |
| `docs/phase3/Phase3_Export_Feature_Analysis.md` | **修改** | §3.1 聚合层：改为描述 GetProductCodeBySessionIdAsync → GetExporter → ExportContext → ExportAsync；§3.2 单 Session 导出：区分 Legacy 与 Km001SessionExporter（配置化列）行为及 Legacy 仍调用 StorageService.ExportBySessionAsync。 |

---

## 六、验证结果

| 项 | 结果 |
|----|------|
| **编译** | `build\build.cmd SnVerify.Tests\SnVerify.Tests.csproj Debug` 通过。 |
| **单元测试** | `dotnet test SnVerify.Tests\SnVerify.Tests.csproj -c Debug --no-build`：**359 通过，0 失败**（含 4 个 ExportAggregationServiceTests）。 |

---

## 七、兼容性与风险（与方案一致）

- **SOLTAG25 / 历史数据**：ProductCode 为空或非 KM001 时使用 LegacySessionExporter，行为与改前一致。
- **StorageService**：`ExportBySessionAsync` 未改，仅由 LegacySessionExporter 调用。
- **UI**：导出流程（选维度 → 选 Filter → 选对象 → 选目录 → 执行）未改；Filter 对 KM001 在实现层忽略。
- **扩展**：新增产品（如 KM002）只需新增 Exporter 实现并在 SessionExporterFactory 中注册，无需改聚合层或 Storage。

---

**变更总结完成，可供评审与归档。**
