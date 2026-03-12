# Phase3 按 ProductCode 的导出策略方案（评审稿）

## 一、目标与需求

在当前导出系统中实现 **按 ProductCode 区分导出策略**，满足：

| 产品 | 导出策略 | 说明 |
|------|----------|------|
| **SOLTAG25** | 保持现有逻辑 | 支持 VerifyType Filter（SN 检验 / 版本检验 / 全部）；仅导出 PASS / FAIL 两个 Sheet。 |
| **KM001** | 新逻辑 | **不使用** VerifyType Filter（导出该 Session 下全部记录）；导出 **Summary Sheet** + PASS / FAIL Sheet；PASS/FAIL 表增加 Phase3 设备字段：WifiMac、ChipId、BoardVersion、ChargeBoardVersion。 |

实现约束：

1. **根据 ProductCode 选择不同 Exporter**（或等效策略分支）。
2. **不影响现有 SOLTAG25 逻辑**（行为与当前完全一致）。
3. **KM001 支持字段**：WifiMac、ChipId、BoardVersion、ChargeBoardVersion（TestRecord 已存在，仅需在 Excel 中写出）。

---

## 二、现状与可复用点

- **Product 表**：已有 **ProductCode** 列（迁移 `MigrateProductAddProductCodeAsync`），Session → Order → Product 可推导出 ProductCode。
- **TestRecord**：已有 WifiMac、ChipId、BoardVersion、ChargeBoardVersion、ExpectedVersion、ActualVersion 等字段。
- **当前导出**：`ExportAggregationService` 逐 Session 调用 `IStorageService.ExportBySessionAsync(sessionId, tempDir, filter)`；`StorageService` 内按 filter 过滤、写 8 列 xlsx（Id, 条形码SN, 设备SN, Result, FailReason, VerifyTime, 目标版本号, 设备版本号）+ txt。
- **UI**：选维度 → 选 VerifyType Filter → 选对象 → 选目录 → 执行；filter 透传到聚合层再透传到 Storage。

SOLTAG25 保持上述链路不变；KM001 需要在「单 Session 导出」环节换用另一套逻辑（无 filter、Summary + PASS/FAIL 且 PASS/FAIL 多 4 列）。

---

## 三、方案设计

### 3.1 总体思路

- **按 Session 决定策略**：每个 Session 通过 **Session → Order → Product → ProductCode** 得到 ProductCode；再根据 ProductCode 选择「用现有带 filter 的导出」或「用 KM001 专用导出」。
- **策略注入点**：在 **ExportAggregationService** 层，对每个 Session 先解析 ProductCode，再调用对应的「单 Session 导出」实现（当前实现保留为 Legacy；KM001 为新增实现）。
- **不改变 UI 流程**：仍为「选维度 → 选 Filter → 选对象 → 选目录 → 执行」。Filter 仅对 SOLTAG25（及未来沿用 Legacy 的产品）生效；对 KM001 在实现上忽略 Filter，UI 仍可照常弹出 Filter 对话框（简化实现），或后续可做「KM001 时跳过 Filter 选择」的体验优化（本方案不强制）。

### 3.2 需要的能力

| 能力 | 说明 |
|------|------|
| **Session → ProductCode** | 由 SessionId 解析出该 Session 所属 Product 的 ProductCode（DB 已有 Product.ProductCode）。 |
| **按 ProductCode 分支** | 聚合层根据 ProductCode 调用不同导出逻辑：SOLTAG25 → 现有 ExportBySessionAsync(sessionId, dir, filter)；KM001 → 新逻辑（无 filter，Summary + PASS/FAIL 含 4 列设备字段）。 |
| **KM001 新逻辑** | 单 Session：取该 Session 全部 TestRecord（不按 VerifyType 过滤）→ 写 xlsx：Summary Sheet + PASS Sheet + FAIL Sheet（PASS/FAIL 表头含 WifiMac、ChipId、BoardVersion、ChargeBoardVersion）；可选保留 txt。 |

### 3.3 是否新增 Exporter 类（评估结论：建议新增）

**方案 A（不新增 Exporter）**  
在 `StorageService` 内根据 `ExportBySessionAsync(sessionId, outputDir, filter, string productCode = null)` 的 productCode 分支：若 KM001 则忽略 filter、写 Summary + 12 列 PASS/FAIL；否则走现有逻辑。  
- 优点：少新类型，改动集中。  
- 缺点：Storage 层职责膨胀，违反开闭原则；后续若再增加产品线（如 KM002）需继续改 Storage，测试与维护成本高。

**方案 B（新增 Exporter 抽象）**  
引入 **ISessionExporter**（或命名为 **ISessionExportStrategy**），按 Session 导出到指定目录；**LegacySessionExporter**（现有逻辑，带 filter）、**Km001SessionExporter**（无 filter，Summary + PASS/FAIL 含 4 列）。聚合层持有 **ISessionExporterFactory**（或根据 ProductCode 选实现的简单工厂），对每个 Session 取 ProductCode → 取 Exporter → 调用 `ExportAsync(sessionId, outputDir)`。  
- 优点：SOLTAG25 逻辑封装在 Legacy 实现中，不动现有 Storage 核心；KM001 独立实现，易单测；新增产品线只需新增 Exporter 实现并注册。  
- 缺点：多 2～3 个类型与一个工厂/注册表。

**评审建议**：**采用方案 B**。新增 Exporter 类与工厂，便于「不影响 SOLTAG25」且后续扩展清晰；Storage 仅负责「取 TestRecord、按需提供数据」，不承担「按产品写不同 Excel 形状」的决策。

### 3.3.1 架构微调：ExportContext（采纳评审建议）

- **问题**：若 filter 放在 LegacySessionExporter 构造函数，则每 Session 可能需 new 一个 Exporter，且未来 Exporter 若有状态会难以复用。
- **建议**：引入 **ExportContext**，承载单次导出的 SessionId、OutputDirectory、Filter?；接口改为 `Task ExportAsync(ExportContext context)`。
- **好处**：Exporter 无状态、可注册为 Singleton；扩展时可在 context 中增加 ZipName、ProjectName、OrderId 等，无需改接口。

### 3.4 是否需要新增 Summary Sheet 生成逻辑（评估结论：需要）

- **需求明确要求**：KM001 导出 **Summary Sheet** + PASS / FAIL Sheet。
- **Summary 内容**（采纳评审建议）：  
  - 表头：SessionId、SessionName、Total、Pass、Fail、**PassRate**、**FailRate**、**ExportTime**。  
  - 数据行：一行汇总；**PassRate = Pass / Total**（如 98%）、**FailRate = Fail / Total**；**ExportTime = DateTime.Now**（导出时刻，便于产线确认「报表何时导出」）。  
  - 若需多 Session 合并汇总，可放在后续迭代；本方案仅「单 Session 一个 xlsx，内含 Summary + PASS + FAIL」。
- **实现位置**：在 **Km001SessionExporter** 内，先写 Summary Sheet，再写 PASS Sheet，再写 FAIL Sheet；PASS/FAIL 列顺序见 3.6。

结论：**需要新增 Summary Sheet 生成逻辑**，与 KM001 的 Exporter 实现放在一起。

### 3.6 KM001 Excel 列顺序（采纳评审建议）

- **PASS / FAIL Sheet 列顺序**（设备相关字段放一起，便于 Excel 可读）：  
  Id、条形码SN、**设备SN、WifiMac、ChipId、BoardVersion、ChargeBoardVersion**、Result、FailReason、VerifyTime、目标版本号、设备版本号。  
- 即：设备 5 列连续排列，再跟结果与版本列。

### 3.5 ProductCode 解析

- **数据来源**：`TestSession` → `Order` → `Product`；`Product` 表已有 `ProductCode`。
- **接口**：在 **IStorageService** 增加 `Task<string> GetProductCodeBySessionIdAsync(int sessionId)`；无匹配或 ProductCode 为空时返回 `null` 或空字符串，聚合层将此类 Session **回退为 Legacy 导出**（与 SOLTAG25 一致），保证兼容历史数据。
- **实现**：`StorageService` 内一条 SQL：  
  `SELECT p.ProductCode FROM TestSession s INNER JOIN "Order" o ON s.OrderId = o.Id INNER JOIN Product p ON o.ProductId = p.Id WHERE s.Id = @SessionId`，返回单列。
- **可选优化（按订单导出）**：同一订单下所有 Session 属于同一 Product，ProductCode 相同。可新增 `Task<string> GetProductCodeByOrderIdAsync(string orderId)`（Order → Product → ProductCode），按订单导出时只查一次 ProductCode，循环内复用，避免 N 次 SQL。**当前方案「每 Session 查一次」完全可接受**；该优化为可选，实施时可后续加入。

---

## 四、需要修改 / 新增的文件（评估清单）

| 类型 | 文件/位置 | 变更说明 |
|------|-----------|----------|
| **接口** | `IStorageService` | 新增 `Task<string> GetProductCodeBySessionIdAsync(int sessionId)`。 |
| **实现** | `StorageService` | 实现 `GetProductCodeBySessionIdAsync`；**不删不改**现有 `ExportBySessionAsync(sessionId, outputDir, filter)`，供 Legacy Exporter 调用。 |
| **新增** | Domain/Export 或 Services 层 **ExportContext** | 承载单次导出调用参数：SessionId、OutputDirectory、Filter?；未来可扩展 ZipName、ProjectName、OrderId 等。 |
| **新增** | **ISessionExporter** | 接口：`Task ExportAsync(ExportContext context)`；Exporter 无状态，可注册为 Singleton。 |
| **新增** | **LegacySessionExporter** | 依赖 `IStorageService`；`ExportAsync(context)` 内部调用 `ExportBySessionAsync(context.SessionId, context.OutputDirectory, context.Filter)`；**不**在构造函数中持有 filter。 |
| **新增** | **Km001SessionExporter** | 依赖 `IStorageService`（仅用于 `GetTestRecordsBySessionAsync`）；无 filter 取全量记录；写 xlsx：Summary（含 PassRate/FailRate）+ PASS/FAIL（12 列，设备字段连续）；可选写 txt。 |
| **新增** | **ISessionExporterFactory** | 方法：`ISessionExporter GetExporter(string productCode)`。SOLTAG25 或 null/空 → LegacySessionExporter；KM001 → Km001SessionExporter。Filter 由调用方放入 ExportContext，不参与工厂选择。 |
| **修改** | **ExportAggregationService** | 依赖 `IStorageService` + `ISessionExporterFactory`；对每个 Session：`productCode = await _storage.GetProductCodeBySessionIdAsync(s.Id)`（或按订单时可选 `GetProductCodeByOrderIdAsync` 一次）；`exporter = _factory.GetExporter(productCode)`；`context = new ExportContext { SessionId = s.Id, OutputDirectory = tempDir, Filter = filter }`；`await exporter.ExportAsync(context)`；ZIP 与日志逻辑不变。 |
| **修改** | **ServiceFactory（DI）** | 注册 `ISessionExporterFactory` 及具体 Exporter 实现；`ExportAggregationService` 构造时注入 Factory。 |
| **可选** | **MainViewModel.ExportAsync** | 当前已传 filter 到聚合层；若未来希望 KM001 时跳过「选择导出内容类型」对话框，可在此处根据所选对象维度或首 Session 的 ProductCode 分支；**本方案不做强制要求**。 |

说明：

- **不修改**：`MainWindow.xaml` 导出按钮、`ExportDimensionDialog`、`ExportRecordFilterDialog`、现有 `StorageService.ExportBySessionAsync` 签名与行为（SOLTAG25 完全走 Legacy Exporter，行为不变）。
- **Excel 列**：Legacy 仍为 8 列；KM001 的 PASS/FAIL 为 12 列，**设备字段连续**：Id、条形码SN、设备SN、WifiMac、ChipId、BoardVersion、ChargeBoardVersion、Result、FailReason、VerifyTime、目标版本号、设备版本号。

---

## 五、是否需要新增 Exporter 类（结论）

- **需要。** 建议新增：  
  - **ISessionExporter**（接口）  
  - **LegacySessionExporter**（现有逻辑封装）  
  - **Km001SessionExporter**（KM001：无 filter，Summary + PASS/FAIL 含 4 列设备字段）  
  - **ISessionExporterFactory**（按 ProductCode + filter 返回 Exporter）

---

## 六、是否需要新增 Summary Sheet 生成逻辑（结论）

- **需要。** 仅 KM001 使用；在 Km001SessionExporter 内实现，内容建议：SessionId、SessionName、总记录数、PASS 数、FAIL 数等一行汇总；具体列名与顺序可在实现时与业务确认。

---

## 七、代码复杂度评估

| 项 | 评估 | 说明 |
|------|------|------|
| **ExportContext** | 低 | 小型 DTO，约 10 行。 |
| **ProductCode 解析** | 低 | 单 SQL + 一个接口方法，约 20 行。 |
| **Exporter 接口** | 低 | 约 10 行。 |
| **LegacySessionExporter** | 低 | 委托到现有 ExportBySessionAsync，约 30 行。 |
| **Km001SessionExporter** | 中 | Summary（含 PassRate/FailRate）+ PASS/FAIL 12 列，约 120 行。 |
| **ISessionExporterFactory** | 低 | 按 ProductCode 返回 Exporter，约 40 行。 |
| **ExportAggregationService 修改** | 低 | 构造 ExportContext、取 Exporter、调 ExportAsync，约 30 行。 |
| **Storage 新 SQL** | 低 | GetProductCodeBySessionIdAsync，约 20 行。 |
| **合计** | **约 250 行** | 开发约 3～5 小时；测试约 1 天。 |

---

## 八、风险与兼容性

- **历史数据**：Session 所属 Order/Product 的 ProductCode 为空或未知时，建议回退为 Legacy 导出（带 filter），与 SOLTAG25 行为一致，避免旧数据导出失败。  
- **SOLTAG25**：仅通过 LegacySessionExporter 调用现有 `ExportBySessionAsync(sessionId, outputDir, filter)`，**不修改** Storage 内该方法的实现，保证行为一致。  
- **未来产品**：新增 ProductCode（如 KM002）时，新增对应 Exporter 实现并在 Factory 中注册即可，无需改 Storage 或聚合层分支逻辑。

---

## 九、实施顺序建议（评审通过后执行）

1. **ExportContext** 新增（SessionId、OutputDirectory、Filter?）。  
2. **IStorageService** 增加 `GetProductCodeBySessionIdAsync`；**StorageService** 实现；（可选）增加 `GetProductCodeByOrderIdAsync` 用于按订单导出时优化。  
3. **ISessionExporter**、**LegacySessionExporter**、**Km001SessionExporter**、**ISessionExporterFactory** 新增并实现；接口为 `ExportAsync(ExportContext context)`。  
4. **ExportAggregationService** 改为按 Session 构造 ExportContext、取 ProductCode、选 Exporter、调用 `exporter.ExportAsync(context)`；ZIP 与日志逻辑不变。  
5. **ServiceFactory** 注册 Factory 与 Exporter（Exporter 可注册为 Singleton）。  
6. 单测：Legacy 路径（SOLTAG25）、KM001 路径（Summary 含 PassRate/FailRate + 12 列）、ProductCode 为空回退 Legacy；回归现有导出测试。  
7. 编译与全量测试通过。

---

## 十、可反驳 / 补充点（供评审讨论）

| 点 | 说明 | 方案立场 |
|----|------|----------|
| **Filter 对 KM001 是否完全忽略** | 需求明确 KM001 不使用 VerifyType Filter；方案采用「工厂返回 KM001 Exporter 时忽略 filter 参数」，UI 仍可照常弹 Filter 对话框（选维度后即选类型），仅 KM001 导出时不生效。若希望 KM001 时连对话框都不弹出，可在 MainViewModel 中根据「所选对象是否全部为 KM001」分支跳过 Step 2，本方案不强制。 | 采纳「KM001 实现层忽略 filter」，UI 可后续优化。 |
| **Summary 是否必须** | 需求明确 KM001 导出 Summary Sheet；若业务后续认为可省略，仅需在 Km001SessionExporter 中去掉 Summary 写入即可，不影响其他设计。 | 按需求实现 Summary。 |
| **Exporter 是否必须独立类** | 若坚持不新增类型，可在 StorageService 内用 productCode 分支（见 3.3 方案 A）；代价是 Storage 职责膨胀、后续每加一个产品改一处。 | 建议采用独立 Exporter 类（方案 B）。 |
| **ProductCode 为空时的回退** | 历史数据或脏数据可能导致 Session 对应 Product 无 ProductCode；方案约定回退为 Legacy 导出（带 filter），保证不报错、行为可预期。 | 已纳入 3.5 与第八章。 |
| **按订单时 ProductCode 是否可只解析一次** | 同一订单下所有 Session 属于同一 Order → 同一 Product，故 ProductCode 相同；可为「按订单」做优化：先取第一个 Session 的 ProductCode，若全单一致则复用同一 Exporter。当前方案为「每 Session 解析一次」，实现简单，性能影响小；优化可后续做。 | 先按「每 Session 解析」实现，优化可选。 |

---

## 十一、评估结论汇总（对应需求中的 4 项评估）

1. **需要修改哪些文件**  
   - **必改**：`IStorageService`（新增 GetProductCodeBySessionIdAsync）、`StorageService`（实现 + 保持现有 ExportBySessionAsync）、`ExportAggregationService`（构造 ExportContext、取 ProductCode、选 Exporter、调用 ExportAsync）、`ServiceFactory`（注册 Factory 与 Exporter）。  
   - **新增**：`ExportContext`、`ISessionExporter`、`LegacySessionExporter`、`Km001SessionExporter`、`ISessionExporterFactory`。  
   - **可选**：`GetProductCodeByOrderIdAsync`（按订单导出时一次解析 ProductCode）。  
   - **可不改**：MainWindow.xaml、ExportDimensionDialog、ExportRecordFilterDialog、现有 `ExportBySessionAsync(sessionId, outputDir, filter)` 的方法体。

2. **是否需要新增 Exporter 类**  
   - **需要。** 建议新增接口 + Legacy 实现 + KM001 实现 + 工厂，以便 SOLTAG25 逻辑不动、KM001 独立可测、后续产品线仅新增实现即可。

3. **是否需要新增 Summary Sheet 生成逻辑**  
   - **需要。** 仅 KM001 使用；在 Km001SessionExporter 内实现，内容为单 Session 汇总行（SessionId、SessionName、Total、Pass、Fail、**PassRate**、**FailRate**）。

4. **代码复杂度评估**  
   - **整体：约 250 行代码；开发 3～5 小时；测试约 1 天。** ProductCode 解析与聚合层改造为低；Exporter 抽象 + ExportContext 为低；KM001 写 Excel（Summary + 12 列）为中；改动面清晰，风险可控。

---

## 十二、评审结论

**整体方法同意。评审结论：通过，按本方案实施。**

- **优点**：SOLTAG25 完全不受影响；KM001 新逻辑隔离；未来可扩展；重构成本可控；回退策略明确。结构为 **Strategy Pattern + Factory Pattern**，适合产线软件。
- **架构**：Exporter 独立类设计正确；ExportContext 为标准 command/context 模式，扩展性良好；Filter 策略、回退策略、KM001 列设计均采纳评审反馈。以下微调与实现细节已纳入方案。

---

## 十三、接口形态与 ExportContext（采纳评审微调）

- **ExportContext**（新建，建议放在 Domain/Export 或 Services 层）  
  - 属性：`int SessionId`、`string OutputDirectory`、`ExportRecordFilter? Filter`。  
  - 未来可扩展：ZipName、ProjectName、OrderId、**ExportTime** 等（标准 command/context 模式）。  
  - **作用**：Exporter 无状态，不把 filter 放在构造函数；可注册为 Singleton；单次调用参数通过 context 传入。

- **ISessionExporter**  
  - `Task ExportAsync(ExportContext context)`  
  - 无返回值；异常由调用方捕获；导出结果以「是否在 context.OutputDirectory 下生成预期 xlsx/txt」体现。

- **LegacySessionExporter**  
  - 构造：`LegacySessionExporter(IStorageService storage)`；**不**持有 filter。  
  - `ExportAsync(context)`：**必须**使用 `var filter = context.Filter ?? ExportRecordFilter.All` 再传入 Storage，避免 context.Filter 为 null 时出现 NullReference。

- **Km001SessionExporter**  
  - 构造：`Km001SessionExporter(IStorageService storage)`。  
  - `ExportAsync(context)`：忽略 context.Filter；`GetTestRecordsBySessionAsync(context.SessionId)` → 全量记录 → 写 xlsx（Summary 含 PassRate/FailRate + PASS/FAIL 12 列，设备字段连续）→ 可选写 txt。

- **ISessionExporterFactory**  
  - `ISessionExporter GetExporter(string? productCode)`  
  - **实现建议**：对 productCode 做 `productCode?.Trim()` 与 `string.Equals(..., StringComparison.OrdinalIgnoreCase)` 判断 "KM001"；真实库中常出现 KM001/km001/Km001 等混写，统一大小写比较可提升稳定性。示例：  
    `if (string.Equals(productCode?.Trim(), "KM001", StringComparison.OrdinalIgnoreCase)) return _km001Exporter;`  
    `return _legacyExporter;`  
  - 当 productCode 为 null/空或非 "KM001" 时返回 **LegacySessionExporter**；否则返回 **Km001SessionExporter**。Filter 由调用方在构造 ExportContext 时传入，不参与工厂选择。

---

## 十四、最终架构图（Phase3 Export Architecture）

```
UI (选维度 → 选 Filter → 选对象 → 选目录 → 执行)
 │
 ▼
ExportAggregationService
 │
 │ 解析 ProductCode（每 Session 或按订单时可选一次）
 ▼
ISessionExporterFactory.GetExporter(productCode)
 │
 ├── LegacySessionExporter
 │       │
 │       ▼
 │   StorageService.ExportBySessionAsync(sessionId, outputDir, context.Filter)
 │
 └── Km001SessionExporter
         │
         ▼
     StorageService.GetTestRecordsBySessionAsync(sessionId)
         │
         ▼
     ExcelWriter（Summary + PASS + FAIL，12 列，设备字段连续）
```

**特点**：SOLTAG25 → 旧逻辑（完全解耦）；KM001 → 新逻辑（无 filter，Summary + PassRate/FailRate + ExportTime + 12 列）；ProductCode 为空 → 回退 Legacy。

---

## 十五、未来扩展示例

| ProductCode | Exporter |
|-------------|----------|
| SOLTAG25 | LegacySessionExporter |
| KM001 | Km001SessionExporter |
| KM002 | Phase3Km002SessionExporter（未来） |

**说明**：新增产品导出策略只需两步——（1）新建对应 Exporter 实现；（2）在 Factory 中按 ProductCode 注册。无需改 Storage 或聚合层分支，扩展能力清晰。

---

*文档版本：v3（含评审结论、Factory string? 与大小写、Summary ExportTime、Legacy Filter null 防护、未来扩展示例）*  
*放置路径：docs/phase3/Phase3_ProductCode_Export_Strategy_Proposal.md*
