# Phase4：KM008 项目接入 + Phase3 架构修正（技术方案 · 含评审收敛补丁）

| 项 | 内容 |
|---|------|
| 状态 | **已定需求边界** + **上线前补丁级强约束**；实现须同时满足主方案与本文件 **§11 速查清单** |
| 依据 | `SnVerify` 仓库真实代码 + 多轮评审定稿 + **评审收敛补丁（必须执行）** |
| 修订 | 初稿 → 二次评估 → **三次：Parser 命名 / 数据校验 / 导出兜底 / ChipId 强制清零** |

---

## 0. 代码扫描结论（真实架构 · 2026-04-07）

以下与实现强相关，并已核对源码路径。

| 主题 | 现状（代码事实） | 与初稿差异 / 注意 |
|------|------------------|-------------------|
| **产品注册** | `Infrastructure/Product/ProductRegistry.cs` 静态字典；运行时代码经 `ProductRegistryAdapter` → `IProductRegistry` 注入（`ServiceFactory.cs`）。 | 初稿未强调 Adapter；新增 KM008 仍只需改**静态** `ProductRegistry`。 |
| **聚合解析** | `ServiceFactory.cs` 构造 `Dictionary<string, IAggregateDeviceInfoParser>`，当前**仅**注册 `ParserKeys.Aggregate.Km001McuVersion` → `Km001McuVersionAggregateParser`；`ParserFactory` 无 productCode 分支，**仅按 Key 取 Parser**。 | KM008 必须**独立 Key + Parser**；**禁止**复用 KM001 Mapper。 |
| **Parser Key 契约** | `Domain/DeviceAccess/Parsing/ParserKeys.cs`：配置须引用常量，**禁止散落魔法字符串**。 | 命名以 **§3.2** 为准：`ParserKeys.Aggregate.Km008AndroidVersion`（~~`Km008McuVersion`~~ **作废**）。 |
| **规则链** | `Services/Rules/RulePipelineExecutor.cs` 中 ChipId 步骤**曾**无条件执行；实现须按 **§5.2** 切分并**强制清零 ChipId**（`!EnableChipIdCheck`）。 | 见第 5 节。 |
| **SN 唯一** | `IsStickerSnPassedInBatchAsync(projectName, orderId, sticker)`：**ProjectName + OrderName**，非纯 OrderId。 | 与现网 KM001 一致，**不改为纯 Order**。 |
| **版本比对** | `VersionVerificationService`：仅对**非空** Expected 比对。 | KM008 可不配 Board/Charge。 |
| **导出** | 曾：`SessionExporterFactory` 写死 KM001；`ExportContext` 无 `ProductCode`。 | 须 **§6.2**：`ProductCode` 校验、**双 Registry** 分工、禁止静默失败。 |
| **导出列配置** | `IProductExportRegistry.GetProfile(productCode)`，与校验用 `ProductProfile` **不同**。 | 取列**必须** `_exportRegistry.GetProfile`，**禁止**误用 `ProductRegistry` 当导出 Profile。 |
| **UI** | `MainWindow.xaml`：`IsPhase3Product` 控制大块显隐；未按 `Enable*` 拆列/行。 | Profile 驱动，见第 7 节。 |

---

## 1. 目标（与评审一致）

在**不破坏** `KM001` 与 **Legacy（SOLTAG25）** 行为的前提下：

1. 新增 **KM008** 项目类型（Phase3）。
2. KM008：**SN 校验**、**Android 版本校验**、**WifiMac 读取与展示（不参与校验）**。
3. 同步修复 Phase3 **规则链 / 导出 / UI** 与 Profile 不一致问题。

---

## 2. KM008 行为定义（严格执行）

KM008 = Phase3 **子集**（相对 KM001）：

| 类别 | 内容 |
|------|------|
| **必做** | 贴纸 SN 与设备 SN 一致；**与 KM001 相同批次语义**下 SN 的 PASS 唯一（`IsStickerSnPassedInBatchAsync`）；Android 与 Session 参数快照中 `ExpectedAndroidVersion` 一致。 |
| **不做** | ChipId 格式校验；ChipId 批次唯一；BoardVersion / ChargeBoardVersion 校验（Expected 为空即不比）。 |
| **WifiMac** | 解析、展示、可落库；**不参与**规则链比对（与现 KM001 一致）。 |
| **ChipId 落库语义** | 在 `EnableChipIdCheck == false` 时，进入规则链后的 **`DeviceInfo.ChipId` 必须为 `null`**（见 **§5.2**），避免 DB / 导出 / UI 不一致。 |

---

## 3. ADB 与解析（必须：新 Parser，禁止复用 KM001）

### 3.1 背景（评审定稿）

- **命令**：与 KM001 相同（`shell dumpsys window getmcuversion`）。
- **第二行 CSV**：
  - **KM001**：`charge, board, chipId, android, sn, wifiMac`（6 列）
  - **KM008**：`android, sn, wifiMac`（3 列）

→ **必须**独立聚合解析器；禁止 KM008 走 `Km001McuVersionAggregateParser`。

### 3.2 命名规范（**评审收敛 · 强约束**）

| 项 | 必须使用 | 禁止使用 |
|----|----------|----------|
| `ParserKeys.Aggregate` 常量 | **`Km008AndroidVersion`**（完整：`ParserKeys.Aggregate.Km008AndroidVersion`） | `Km008McuVersion`、`Km008Parser`、未入 `ParserKeys` 的裸字符串 |
| Parser 类名 | **`Km008AndroidVersionAggregateParser`** | `Km008McuVersionAggregateParser`、`Km008Parser` |
| 命名原则 | **`KMxxx` + `AndroidVersion` + `AggregateParser`** | 随意缩写 |

修改范围须同步：

- `ParserKeys.Aggregate.Km008AndroidVersion`
- `Km008AndroidVersionAggregateParser` 类文件
- `ProductRegistry` 中 KM008 的 `ParserKey`
- `ServiceFactory` 中 `aggregateParsers` 注册

### 3.3 `Km008AndroidVersionAggregateParser` 行为（必须）

1. 解析**第二行** CSV；列数不足抛 `AggregateProtocolException`（与 KM001 风格一致）。
2. 格式：`android, sn, wifiMac`。
3. 映射：  
   `DeviceInfo.AndroidVersion`、`DeviceInfo.DeviceSn`、`DeviceInfo.WifiMac`（**`WifiMac`：`ToUpperInvariant()`**）。
4. **强制清空**：`ChipId = null`；`BoardVersion = null`；`ChargeBoardVersion = null`。

### 3.4 Parser 字段校验（**评审收敛 · 必须新增**）

在 `Km008AndroidVersionAggregateParser` 内，映射后**进入返回值前**须校验：

- 若 `string.IsNullOrWhiteSpace(deviceSn)` → `throw new AggregateProtocolException("Device SN is empty");`（或同等中英文消息，**须为 `AggregateProtocolException`**以便与 ADB 协议错误一致处理）。
- 若 `string.IsNullOrWhiteSpace(androidVersion)` → `throw new AggregateProtocolException("Android version is empty");`

**目标**：进入 `RulePipelineExecutor` 的 KM008 `DeviceInfo` 在**核心字段**上已有效，避免脏数据静默流入规则链。

> **WifiMac**：评审未要求强制非空；若产品后续要求「MAC 必填」，再单独立项，本补丁不强制。

---

## 4. ProductRegistry（新增 KM008）

在 `ProductRegistry.cs` 中新增一项（保持 KM001 不变）：

| 字段 | 值 |
|------|-----|
| `ProductCode` | `"KM008"` |
| `Mode` | `VerificationMode.Phase3` |
| `AdbConfig.AggregateCommand` | 与 KM001 **相同 Command**，`ParserKey` = **`ParserKeys.Aggregate.Km008AndroidVersion`** |
| `EnableChipIdCheck` | `false` |
| `EnableBoardVersionCheck` | `false` |
| `EnableChargeBoardVersionCheck` | `false` |
| `EnableWifiMacCheck` | `true` |
| `FieldLabels` | 至少包含 `DeviceSn`、`AndroidVersion`、`WifiMac` |

---

## 5. RulePipelineExecutor（必须修改）

### 5.1 问题

ChipId 规则曾**无条件执行**，`EnableChipIdCheck` 失效。

### 5.2 修改要求

1. **仅当** `profile.EnableChipIdCheck == true` 时执行：ChipId F50 校验 + `IsChipIdPassedInBatchAsync`。
2. **当** `profile.EnableChipIdCheck == false` 时：  
   - **不得**因 ChipId 空/非法失败；  
   - **必须执行**（**评审收敛 · 不再是建议**）：

```csharp
if (!profile.EnableChipIdCheck)
{
    di.ChipId = null;
}
```

   （在持有 `DeviceInfo` 实例且已完成读设备之后、**在进入 ChipId 校验分支之前或代替该分支**统一处理，保证 **KM008 `ChipId` 恒为 null**；**KM001** `true` 时行为不减损。）

3. **不得**改变 Phase3 阶段顺序：Parameter → 读设备 → （必要时规范 ChipId）→ SN → SN 唯一 →（可选 ChipId 规则）→ 版本服务 → PASS。

---

## 6. 导出模块（必须去硬编码 + 稳定性兜底）

### 6.1 问题回顾

- Factory / Exporter 写死 KM001；`ExportContext` 曾无 `ProductCode`；存在空路径、静默跳过风险。

### 6.2 修改要求

1. **`ExportContext`** 增加 `ProductCode`。
2. **`ExportAggregationService`**：两处创建 `ExportContext` 均赋值 `ProductCode`（来自 `GetProductCodeBySessionIdAsync`）。
3. **Exporter 执行开头（评审收敛 · 必须）**  
   - `ProductCode` 为 null 或空白 → `throw new InvalidOperationException("ProductCode is null or empty in export context");`  
   - 使用 **`IProductRegistry` / `ProductRegistry`**：`var product = ...Get(context.ProductCode);` 若 `product == null` → `throw new InvalidOperationException($"Unknown productCode: {context.ProductCode}");`  
   - **导出列**：**必须** `var exportProfile = _exportRegistry.GetProfile(context.ProductCode);` —— **禁止**用 `ProductRegistry.Get` 当导出列配置。  
   - **禁止**：静默失败、无记录仍冒充成功、隐式 fallback 到 KM001（若 `exportProfile == null`，Phase3 Exporter 应 **显式抛异常或显式记录失败**，由项目组在实现时二选一，**不得**默默生成空文件）。

4. **Factory**：`ProductRegistry.Get(productCode)?.Mode == Phase3` → Phase3 配置化 Exporter；否则 Legacy。

5. **`ProductExportRegistry`**：注册 KM008，列模板与 KM001 **一致**（允许空列）。

### 6.3 双 Registry（必须确认）

| 用途 | Registry / 接口 |
|------|------------------|
| 校验 Profile、Mode、`Enable*` | `ProductRegistry` / `IProductRegistry.Get` |
| 导出列、`ProductExportProfile` | **`IProductExportRegistry.GetProfile`** |

---

## 7. UI 改造（必须 Profile 驱动）

### 7.1 禁止

在 XAML / ViewModel 写死 `KM001`/`KM008` 产品码分支（测试桩等除外）。

### 7.2 属性（MainViewModel 或子 VM）

| 属性 | 来源 |
|------|------|
| `ShowChipIdColumn` | `EnableChipIdCheck` |
| `ShowBoardVersion` | `EnableBoardVersionCheck` |
| `ShowChargeVersion` | `EnableChargeBoardVersionCheck` |
| `ShowWifiMac` | `EnableWifiMacCheck` |

### 7.3 KM008 预期界面

SN 区：扫码/设备 SN + WifiMac；无 ChipId 列。版本区：仅 Android；Board/Charge 隐藏。

---

## 8. 测试要求（必须补充）

1. **KM008 正常路径**：SN / Android PASS；WifiMac 展示与落库。
2. **KM008 ChipId**：无 ChipId 校验；规则链视图中 **`ChipId` 为 null**；非法 ChipId 预填亦应在 `EnableChipIdCheck==false` 时被清零且不误判 FAIL。
3. **Parser（KM008）**：
   - 合法 3 列；
   - 列数不足 / 空输出 → 异常；
   - **`DeviceSn` 空 → `AggregateProtocolException`**；
   - **`Android` 空 → `AggregateProtocolException`**。
4. **导出**：`ProductCode` 缺失 → **明确异常**；未知 `ProductCode` → **明确异常**；KM008 正常生成 xlsx、列模板同 KM001。
5. **KM001 回归**：全量行为不变（含 ChipId、导出）。

---

## 9. 禁止修改范围（再次强调）

- **不修改**数据库结构。
- **不修改** SN 唯一性逻辑与存储契约（`IsStickerSnPassedInBatchAsync`）。
- **不修改** Phase3 流程顺序（仅增加分支与校验）。
- **不引入** MES 逻辑变更。

---

## 10. 验收标准

### 10.1 主方案（既有）

- **KM001**：与当前行为完全一致。
- **KM008**：无 ChipId 校验；解析 `android, sn, wifiMac`；导出由 ProductCode + Registry 驱动；UI 隐藏 ChipId/Board/Charge。

### 10.2 评审收敛补丁（新增 · 必须通过）

| # | 标准 |
|---|------|
| 1 | 所有 KM008 相关 **`ParserKey` 已为 `Km008AndroidVersion`**（类名为 `Km008AndroidVersionAggregateParser`）。 |
| 2 | Parser 已增加 **DeviceSn / Android 非空**校验。 |
| 3 | `RulePipelineExecutor` 在 **`EnableChipIdCheck == false` 时将 `ChipId` 置 `null`**。 |
| 4 | 导出：**`ProductCode` 非法或缺失 → 明确异常**；**列配置只取自 `IProductExportRegistry`**。 |
| 5 | **KM001** 回归测试全通过。 |

### 10.3 提交结束标准（评审原文）

满足以下才允许合并/发布：

- ParserKey 已全部改为 **`Km008AndroidVersion`** 系列命名；
- Parser 已增加字段校验；
- RulePipeline 已统一 ChipId 处理；
- 导出已增加 `ProductCode` / 未知产品校验；
- KM001 回归通过。

---

## 11. 评审收敛补丁速查（强约束清单）

以下为**上线前补丁**强约束摘录，与上文 **§3.2～§3.4、§5.2、§6.2、§8、§10.2** 一致：

1. **命名**：`ParserKeys.Aggregate.Km008AndroidVersion` + `Km008AndroidVersionAggregateParser`；禁止 `Km008McuVersion` / 模糊类名。
2. **Parser 校验**：`DeviceSn`、`AndroidVersion` 空白 → `AggregateProtocolException`。
3. **ChipId**：`!profile.EnableChipIdCheck` → **`deviceInfo.ChipId = null`**（执行层强制，不仅依赖 Parser）。
4. **导出**：`ExportContext` 含有效 `ProductCode`；Exporter 内校验 `ProductCode` 与 **`IProductRegistry` 已知产品**；**`exportProfile = _exportRegistry.GetProfile(...)`**。
5. **WifiMac**：`ToUpperInvariant()`；三列映射与三字段清空与 **§3.3** 一致。

---

## 12. 修订记录

| 日期 | 修订说明 |
|------|----------|
| 2026-04-07 | 初稿 |
| 2026-04-07 | 二次评估：源码对齐、双 Registry、SN 批次语义、独立 Parser |
| 2026-04-07 | **三次：评审收敛补丁** — Parser 重命名为 **AndroidVersion** 系列；Parser 核心字段校验；RulePipeline **强制** `ChipId = null`；导出 **ProductCode / 未知产品** 显式异常；验收与清单固化 |

附录（签字、排期）由项目组另行维护。
