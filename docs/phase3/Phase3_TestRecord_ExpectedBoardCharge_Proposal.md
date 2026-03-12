# Phase3 TestRecord 增加主板/充电板「目标版本」存储方案（评审稿）

> **实施状态**：评审通过，已按 §四 实施（TestRecord 模型与表、迁移、StorageService、ProcessCoordinator、Km001SessionExporter 配置化 14 列、单测与迁移断言、架构文档已更新）。

## 数据设计原则（评审采纳）

> **测试记录必须自包含测试时的参数与结果，不依赖后续可变配置。**

因此：历史记录不应依赖 VerificationParameter 等可修改的配置表；「记录时固化参数」是测试系统的正确做法。本方案在 TestRecord 中落库 ExpectedBoardVersion / ExpectedChargeBoardVersion 符合该原则。

---

## 一、问题与背景

当前 **TestRecord** 中：

- **Android 版本**：有 **ExpectedVersion**（目标）与 **ActualVersion**（设备实际），一一对应，写入与导出一致。
- **主板版本 / 充电板版本**：仅有 **BoardVersion**、**ChargeBoardVersion**（设备实际），**没有对应的目标版本**。

目标版本在 **VerificationParameter**（按 ProjectId）和 **MainViewModel** 的输入框中存在，校验时 **VersionVerificationService** 用 `parameter.ExpectedBoardVersion` / `parameter.ExpectedChargeBoardVersion` 与设备值比较决定 PASS/FAIL，但这两项**从未写入 TestRecord**。

带来的问题：

1. **导出**：Excel 中只有「设备主板版本」「设备充电板版本」，没有「目标主板版本」「目标充电板版本」，无法在报表中直接看出「期望 vs 实际」。
2. **追溯与审计**：单条记录无法自洽表达「当时期望是什么、实际是什么」；若后续修改了 VerificationParameter，历史记录无法还原当时的期望值。
3. **与 Android 版本不对称**：ExpectedVersion/ActualVersion 已落库并导出，Board/Charge 仅落实际值，语义不统一。

---

## 二、可选方案

### 方案 A：在 TestRecord 中增加 ExpectedBoardVersion、ExpectedChargeBoardVersion（推荐）

- **做法**：在 TestRecord 模型与 TestRecord 表中增加 `ExpectedBoardVersion`、`ExpectedChargeBoardVersion`；在写入记录时（如 `SavePhase3ResultAsync`）从当次使用的 `VerificationParameter` 写入这两项；导出时在 Excel 中增加对应列（如「目标主板版本」「目标充电板版本」）。
- **优点**：
  - 与 ExpectedVersion/ActualVersion 一致，每条记录自包含「期望 vs 实际」。
  - 导出可直接展示目标/实际对照，无需再查 Parameter。
  - 参数表后续变更不影响历史记录的解读。
- **缺点**：
  - 与 VerificationParameter 存在冗余（同一 Session 内多条记录目标值相同）。
  - 需要一次 DB 迁移、模型与读写/导出逻辑的联动修改。

### 方案 B：不落库，导出时由 Session 反查 VerificationParameter 填充

- **做法**：TestRecord 与表结构不变；导出时根据 Session → Order → Product → ProjectId 查 `GetVerificationParameterAsync(projectId)`，用 parameter 的 ExpectedBoardVersion/ExpectedChargeBoardVersion 作为该 Session 下所有记录的「目标主板/充电板版本」写入 Excel。
- **优点**：无需改表、改写录逻辑。
- **缺点**：
  - **审计语义错误**：Parameter 可能在后继被修改，导出看到的是「当前配置的期望值」，而非「该条记录测试时的期望值」。
  - 导出逻辑需增加 Session→ProjectId 解析与 Parameter 查询，且需处理 Parameter 缺失或 ProjectId 变更的边界。
- **结论**：不推荐。若业务要求「报表反映当时期望」，必须把当时使用的期望值固化到记录或 Session 快照，方案 B 无法满足。

### 方案 C：仅在 Session 级快照目标版本，不写入 TestRecord

- **做法**：在 TestSession 或单独「Session 版本参数快照」表中保存当次 Session 使用的 ExpectedBoardVersion、ExpectedChargeBoardVersion；TestRecord 不存；导出时 Session 内记录共用该快照。
- **优点**：不冗余到每条记录；历史期望值有据可查。
- **缺点**：导出与报表需做 Session→快照 的关联；若未来出现「同 Session 内不同记录用不同期望」（当前不会），扩展性不如记录级。且需新增 Session 级存储与写入时机（如 StartSession 时快照 Parameter）。
- **结论**：可行，但实现量和复杂度不低于方案 A，而记录级存储与现有 ExpectedVersion/ActualVersion 更一致，故仍推荐方案 A。

---

## 三、推荐结论与需要澄清的点

- **推荐采用方案 A**：在 TestRecord 中增加 **ExpectedBoardVersion**、**ExpectedChargeBoardVersion**，写入时从当次 `VerificationParameter` 带入，导出时在 Excel 中增加对应列。

**建议在评审前确认：**

1. **业务是否要求「报表中能看到主板/充电板的目标 vs 实际」？**  
   - 若否，可只做落库（便于审计），导出列可暂不增加或延后。  
   - 若是，则落库 + 导出列一起做（与 Android 目标/设备版本号一致）。

2. **Legacy 导出（8 列，无 Board/Charge）是否需要在本次一并增加主板/充电板相关列？**  
   - 当前 Legacy 导出不含 BoardVersion/ChargeBoardVersion。若 Legacy 产品（如 SOLTAG25）不做主板/充电板校验，可保持 8 列不变；仅 **KM001 等 Phase3 导出**（Km001SessionExporter，通过 ProductExportProfile 配置）增加「目标主板版本」「目标充电板版本」列即可。

3. **历史数据**：迁移后旧记录的 ExpectedBoardVersion/ExpectedChargeBoardVersion 为 NULL，导出时显示为空或「--」即可，与现有 ActualVersion 为空时的处理一致。

---

## 四、方案 A 实施范围（评审通过后执行）

### 4.1 数据模型与数据库

| 项 | 说明 |
|----|------|
| **TestRecord 模型** | 增加属性 `ExpectedBoardVersion`、`ExpectedChargeBoardVersion`（string，可为 null）。 |
| **TestRecord 表** | 迁移增加列 `ExpectedBoardVersion TEXT`、`ExpectedChargeBoardVersion TEXT`；兼容旧库（新列允许 NULL）。 |
| **StorageService** | INSERT/SELECT（含 GetTestRecordsBySessionAsync 及所有返回 TestRecord 的查询）增加两列；若有 UPDATE TestRecord 也需同步。 |

### 4.2 写录逻辑

| 项 | 说明 |
|----|------|
| **ProcessCoordinator.SavePhase3ResultAsync** | 构造 TestRecord 时增加：`ExpectedBoardVersion = parameter?.ExpectedBoardVersion ?? null`，`ExpectedChargeBoardVersion = parameter?.ExpectedChargeBoardVersion ?? null`，确保 parameter 为空时不出问题。 |

（若存在其他写入 TestRecord 的路径，需同样带入当次使用的 Parameter 或约定为 null。）

**Parameter 加载说明**：当前实现为**每条记录**校验时调用 `GetProductNameBySessionNameAsync` + `GetParameterAsync(productName)` 获取 parameter。评审建议：**StartSession 时加载 Parameter 并缓存到 Session 上下文，写记录时直接使用缓存**，可避免每条记录一次 DB 查询。本次实施可先保持现有「每条取一次」逻辑，将「Session 级缓存 Parameter」列为**可选优化**，后续单独实施。

### 4.3 导出逻辑

| 项 | 说明 |
|----|------|
| **Km001SessionExporter** | PASS/FAIL Sheet 由配置化 14 列（ProductExportProfile + IExportValueResolver）生成；列顺序：Id、条形码SN、设备SN、WifiMac、ChipId、目标芯片版本、设备芯片版本、目标充电板版本、设备充电板版本、Result、错误详细、验证时间、Android目标版本号、Android实际版本号。 |
| **Legacy 导出（StorageService.ExportBySessionAsync）** | 当前为 8 列，不含 Board/Charge；**本次不改**。SOLTAG25 等不校验 Board/Charge，保持历史导出格式稳定。 |

### 4.4 UI 与其它

- **UI**：无需改；目标主板/充电板版本已由 MainViewModel 的 ExpectedBoardVersion/ExpectedChargeBoardVersion 参与校验，本次仅把同一值写入 TestRecord 并导出。
- **VerificationParameter**：不删、不改；仍为「当前项目配置的期望版本」，写录时仅读取并写入 TestRecord。

### 4.5 测试与回归

- 单测：写录时 ExpectedBoardVersion/ExpectedChargeBoardVersion 被正确写入并读出；导出（Km001SessionExporter / DefaultExportValueResolver）中 14 列含两列目标值且与记录一致。
- 回归：现有 TestRecord 与导出相关用例（含 Legacy 与 KM001）仍通过；旧库迁移后旧记录两列为 NULL，导出显示空或「--」。

### 4.6 复杂度评估（评审估算）

| 模块 | 改动 |
|------|------|
| TestRecord 模型 | +2 字段 |
| DB migration | +2 列 |
| SavePhase3ResultAsync | +2 赋值 |
| Km001SessionExporter | 表头+数据 14 列（配置化）、列顺序由 ProductExportProfile 定义 |
| StorageService INSERT/SELECT | +2 列 |
| **合计** | ≈ 80～120 行；开发约 1～2 小时，测试约半天，风险极低。 |

---

## 五、风险与兼容性

- **历史数据**：迁移后旧记录新列为 NULL，导出与报表中按空处理即可。
- **未启用主板/充电板校验的产品**：Parameter 中对应期望可为空，写录为 null，与现有行为一致。
- **与现有导出方案的兼容**：仅扩展 TestRecord 与 KM001 导出列，不改变 Legacy 导出格式与 ProductCode 导出策略选择逻辑。

---

## 六、小结

- **建议**：采用 **方案 A**，在 TestRecord 中增加 **ExpectedBoardVersion**、**ExpectedChargeBoardVersion**，写录与导出（KM001）同步支持，与 ExpectedVersion/ActualVersion 对齐，便于追溯与报表。
- **评审结论**：见下文 §七。

---

## 七、最终评审结论（已通过）

- **结论**：**评审通过**。采用 **方案 A**。
- **评审理由摘要**：  
  1）**审计一致性**：测试记录必须自包含测试时的条件；若导出时再查 Parameter，配置被修改会导致历史导出显示「当前配置」而非「当时期望」，破坏历史真实性。  
  2）**与现有 Android 版本设计一致**：ExpectedVersion/ActualVersion 已是记录级；Board/Charge 同样采用 Expected/Actual 成对，数据模型对称。  
  3）**导出简单**：方案 A 为 TestRecord → Exporter → Excel（仅写列）；方案 B 需多表关联与参数缺失/历史参数处理，复杂度高。  
- **方案 B/C 不采纳原因**：Parameter 是可变配置，历史记录不应依赖配置表；Session 快照（方案 C）会导致混合粒度且导出需 JOIN，冗余在工业场景可接受（两字段约 40MB/百万条）。
- **实施范围**：
  - TestRecord 增加 **ExpectedBoardVersion**、**ExpectedChargeBoardVersion**（与现有 VerificationParameter 命名一致）。
  - DB 迁移增加两列，允许 NULL；写入与 SELECT 同步。
  - ProcessCoordinator.SavePhase3ResultAsync 写入 `parameter?.ExpectedBoardVersion ?? null`、`parameter?.ExpectedChargeBoardVersion ?? null`。
  - KM001 导出 12 列 → 14 列，列顺序按 Expected→Actual 成对（目标主板/设备主板、目标充电板/设备充电板）。
  - Legacy 导出不变（8 列）。
- **风险**：极低。  
- **收益**：历史数据可审计、导出完整、数据模型统一（Android/Board/Charge 均为 Expected+Actual+Result）。

---

## 八、未来扩展建议（可选）

若 Phase4 再扩展版本检测（如 MCU、BLE 等），建议统一为 **ExpectedXxxVersion / ActualXxxVersion** 成对字段，TestRecord 保持「Expected → Actual → Result」的整齐结构，便于维护与导出。
