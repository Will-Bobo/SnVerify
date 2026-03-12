# 当前「导出」功能流程与 Phase3 适用性分析

**说明**：仅做流程梳理与 Phase3 场景评估，不修改任何代码。

---

## 一、UI 与命令绑定（MainWindow.xaml 249-261）

- **控件**：`ExportBatchButton`，Content="导出"，`Command="{Binding ExportCommand}"`。
- **可用性**：
  - 默认 `IsEnabled="False"`。
  - 通过 `DataTrigger`：当 `IsSessionActive == False` 时 `IsEnabled="True"`。
- **语义**：**仅在「当前无进行中 Session」时可点击导出**（即未点「开始测试」或已「结束测试」后）。

与 ViewModel 中 `ExportCommand` 的 CanExecute 一致：`() => !IsSessionActive`。

---

## 二、导出流程总览（ExportAsync）

| 步骤 | 动作 | 说明 |
|------|------|------|
| 1 | 选择导出维度 | `ChooseExportDimension()` → **按项目** 或 **按订单**（二选一）。 |
| 2 | 选择导出内容类型 | `ChooseExportRecordFilter(new[] { CurrentVerificationType })` → **SN 检验 / 版本检验 / 全部**（至少勾选一项）；默认根据当前 `CurrentVerificationType` 勾选。 |
| 3 | 选择具体对象 | **按项目**：`GetAllProjectIdsAsync()` → 选一个 ProjectId（实际为 ProductName）；**按订单**：`GetAllOrdersAsync()` → 选一个 Order（取 OrderName）。 |
| 4 | 选择导出文件夹 | `ChooseFolder(...)`，初始路径为上次导出目录或日志目录。 |
| 5 | 覆盖确认 | 若目标 `{selectedId}.zip` 已存在，弹窗确认是否覆盖；确认则先删除再执行导出。 |
| 6 | 执行导出 | **按项目**：`ExportByProjectIdAsync(selectedId, folder, filter)`；**按订单**：`ExportByOrderIdAsync(selectedId, folder, filter)`。 |

任一步用户取消或校验失败（如无数据、选空）则中止并打日志，不抛到 UI。

---

## 三、后端导出链路

### 3.1 聚合层（ExportAggregationService）

- **按订单**：`GetSessionsByOrderIdAsync(orderId)` → 得到该订单下所有 Session → 对每个 Session：`GetProductCodeBySessionIdAsync(session.Id)` → `ISessionExporterFactory.GetExporter(productCode)` → 构造 `ExportContext`（SessionId、SessionName、OutputDirectory、Filter）→ `ISessionExporter.ExportAsync(context)`，结果打 ZIP（结构：`{OrderName}/{SessionName}.xlsx` + 对应 Session 日志）。
- **按项目**：`GetSessionsByProjectIdAsync(projectId)`（projectId 为 ProductName）→ 同上，逐 Session 按 ProductCode 选 Exporter、传 ExportContext 后导出，再打 ZIP。

ZIP 内除 xlsx 外，会按 Session 拷贝运行时日志（依赖 `ILoggingService` 的 Session 日志路径）。

### 3.2 单 Session 导出（按 ProductCode 选 Exporter，Phase3 已落地）

- **策略选择**：`ISessionExporterFactory.GetExporter(productCode)` 根据 ProductCode（如 KM001 忽略大小写/空格）返回对应 Exporter；未知或空 ProductCode 使用 **LegacySessionExporter**。
- **LegacySessionExporter**：内部调用 `IStorageService.ExportBySessionAsync(sessionId, outputDir, context.Filter ?? ExportRecordFilter.All)`，行为与原有「按 Session 导出」一致（见下段）。
- **Km001SessionExporter**：不按 Filter 过滤，导出该 Session 全量记录；通过 **ProductExportRegistry** 与 **IExportValueResolver** 配置化生成表头与数据；生成 Summary Sheet（列头资源化）+ PASS/FAIL 两个 Sheet（**14 列**，列定义见 Domain/Export，列头由 Resources 的 Export_Km001_* 提供）；FAIL 按 `(StickerSN, DeviceSN)` 去重保留第一条。

**Legacy 单 Session 行为（StorageService.ExportBySessionAsync）**：

1. `GetTestRecordsBySessionAsync(sessionId)` 取该 Session 下所有 **TestRecord**。
2. **按 ExportRecordFilter 过滤**：
   - 约定：`StickerSN == "-"` 视为 **VersionMatch**，否则为 **SnMatch**。
   - `filter.IncludeVersionMatch` / `filter.IncludeSnMatch` 决定是否保留对应类型记录。
3. 过滤后为空则跳过该 Session（不生成 xlsx/txt）。
4. 非空则：
   - 生成 **xlsx**：PASS / FAIL 两个 Sheet，FAIL 按 `(StickerSN, DeviceSN)` 去重保留第一条。
   - 生成 **txt**：SessionId、PASS/FAIL 计数及明细行。

### 3.3 导出 Excel 列（当前实现）

表头与数据列（共 8 列）：

| 列 | 表头 | 数据来源 |
|----|------|----------|
| 1 | Id | TestRecord.Id |
| 2 | 条形码SN | StickerSN |
| 3 | 设备SN | DeviceSN |
| 4 | Result | Result |
| 5 | FailReason | FailReason |
| 6 | VerifyTime | VerifyTime |
| 7 | 目标版本号 | ExpectedVersion（仅 VersionMatch 有值，SnMatch 为空） |
| 8 | 设备版本号 | ActualVersion（同上） |

**TestRecord 模型** 中已有 Phase3 扩展字段：**WifiMac、ChipId、BoardVersion、ChargeBoardVersion、ExpectedBoardVersion、ExpectedChargeBoardVersion**；Legacy 导出仅写上述 8 列；KM001 导出写 14 列（含目标/设备主板版本、目标/设备充电板版本）。

---

## 四、Phase3 场景评估

### 4.1 能满足的部分

| 维度 | 评估 |
|------|------|
| **启用时机** | Phase3 下「结束测试」后 Session 非 Active，导出按钮可用，与「批次结束后再导出」的使用习惯一致。 |
| **按项目 / 按订单** | 按项目 = 按 ProductName（项目个体）；按订单 = 按 OrderName。与 Phase3 的 Product–Order–TestSession 模型一致，数据来源正确。 |
| **记录类型过滤** | ExportRecordFilter 的 SnMatch / VersionMatch 与 Phase3 双模式（SN 检验、版本检验）对应；默认用 `CurrentVerificationType` 设勾选，同一 Session 内混合两种记录时用户可勾选「全部」或只导一种。 |
| **版本号列** | 目标版本号、设备版本号已导出，满足版本检验结果的追溯。 |
| **ZIP 与覆盖** | 按项目/订单生成单一 ZIP、覆盖确认逻辑清晰，满足常规导出与复写需求。 |

### 4.2 不足或需注意的点

| 维度 | 说明 |
|------|------|
| **Phase3 设备扩展字段未导出** | TestRecord 已有 **WifiMac、ChipId、BoardVersion、ChargeBoardVersion**（及可能的 AndroidVersion 等），当前 xlsx 仅 8 列，**不包含这些字段**。若 Phase3 产品（如 KM001）需要导出设备信息做追溯或分析，当前导出内容不完整。 |
| **「设备版本号」列语义** | 当前「设备版本号」列对应 **ActualVersion**（通常为 Android 版本或单一版本号）。Phase3 若区分 AndroidVersion / BoardVersion / ChargeBoardVersion，仅一列可能无法表达多版本维度，需在后续扩展中考虑多列或约定合并展示方式。 |
| **按项目列表含义** | `GetAllProjectIdsAsync()` 返回的是 **ProductName** 去重列表（有 Order 的 Product）。Phase3 中「项目」= 项目个体 = ProductName，与 UI 文案「按项目导出」一致；若未来产品/业务把「项目」定义为其他实体，需要再对齐接口语义。 |

### 4.3 小结

- **流程与数据维度**：当前导出流程（选维度 → 选类型 → 选对象 → 选目录 → 覆盖确认 → 执行）**能支撑 Phase3 的按项目/按订单导出**，且 **SN/版本 双类型过滤** 与 Phase3 的 SnMatch/VersionMatch 一致。
- **数据内容**：**基础 8 列 + 版本两列** 可满足「谁、何时、结果、失败原因、目标/实际版本」的常规追溯；**Phase3 新增的设备字段（WifiMac、ChipId、BoardVersion、ChargeBoardVersion 等）当前未纳入导出**，若业务要求设备级或多版本维度追溯，需要在导出层扩展列或单独方案，而不是「流程是否支持 Phase3」的问题。

---

## 五、结论（是否满足 Phase3）

- **流程与可用性**：能满足 Phase3——导出在无进行中 Session 时可用，按项目/按订单、按 Sn/版本/全部 的维度与 Phase3 数据模型和校验类型一致。
- **导出内容**：在「仅需条形码 SN、设备 SN、结果、失败原因、校验时间、目标/实际版本」的前提下，**可以满足** Phase3；若需要 **设备扩展字段（ChipId、WifiMac、多版本号等）进 Excel**，则**当前实现不满足**，需在导出逻辑中增加列与 TestRecord 字段映射。

**建议**：若产品/工艺明确要求导出设备信息或多版本列，可将「扩展导出列（Phase3 设备字段 + 多版本列）」列为独立需求，在本文档基础上补充列清单与落库字段后再改代码。
