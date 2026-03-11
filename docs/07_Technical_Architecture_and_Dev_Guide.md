# 07_Technical_Architecture_and_Dev_Guide

> 本文档为 **SnVerify Phase 2.5 / 3.0 及之后阶段** 的技术架构与开发指引。
> Phase 1 版本已完成历史使命，仅作为背景参考；当前实现与后续开发应以本版为准。
>
> 目标读者：
>
> - 架构者（你 / ChatGPT）
> - 开发者（Cursor Agent）
> - 审核者（人工 Review）

---

## 1. 版本与适用范围

- **本版适用阶段**
  - Phase 2.5：
    - 完成 `Product / Order / TestSession / TestRecord` 新数据模型。
    - SN 校验决策树（StickerSN / DeviceSN / 绑定关系）落地。
    - 版本校验（VersionMatch）流程落地并写入统一 `TestRecord`。
    - MES Gate 抽象（`Mes.Gate` + `IMesPlugin`）与 `JekeMesPlugin` Stub 接入。
  - Phase 3.0（规划中）：
    - 杰科 MES 协议真实对接（在 `JekeMesPlugin` 中落地）。
    - 进一步的多工位/并发、权限与审计能力等。

- **与 Phase 1 文档的关系**
  - Phase 1 的目标是「最小闭环 + 可测试」，对应的技术约束已体现在本版的基础原则中。
  - 原 Phase 1 文档（老版 07）视为 **历史归档**，仅用于理解缘起，不再作为当前实现的唯一约束来源。

---

## 2. 总体技术架构概览

### 2.1 分层结构（当前实际实现）

```text
┌───────────────┐
│     View      │  XAML / Code-behind（仅 InitializeComponent / 事件转发）
└───────▲───────┘
        │ DataBinding / Command
┌───────┴───────┐
│  ViewModel    │  UI 状态 / Command / PropertyChanged / Snapshot 轮询
└───────▲───────┘
        │ Snapshot（只读状态）
┌───────┴───────┐
│    Service    │  业务流程 / 决策树 / Session 生命周期 / 导出 / MES Gate
└───────▲───────┘
        │ ADB / SQLite / FileLog / MES 插件
┌───────┴───────┐
│ Infrastructure│  ADB CLI / SQLite / FileLogger / ServiceFactory / Mes.Gate
└───────────────┘
``>

- View：`MainWindow.xaml` + 若干 Dialog，仅布局与绑定。
- ViewModel：`MainViewModel`，通过构造函数注入所有 Service 接口。
- Service：
  - 协调层：`ProcessCoordinator`、`VerificationFlowService`、`VersionVerificationFlowService`、`SessionLifecycleService` 等。
  - ADB / Storage / Logging / Mes.Gate / Export / ScanInput。
- Infrastructure：
  - `StorageService`（SQLite）、`AdbAccessService`（ADB）、`LoggingService`、`ServiceFactory`、`VerificationFlowServiceFactory` 等。

### 2.2 关键运行时技术

- .NET Framework 4.7.2，WPF 桌面应用。
- 语言：C# 8.0。
- 存储：`System.Data.SQLite` 本地单文件数据库。
- 导出：EPPlus 7.x，生成 Excel，再由 `ExportAggregationService` 打包 ZIP。
- 日志：自研 `LoggingService`，每 Session 一个日志文件 + 内存 Snapshot。
- 设备访问：`AdbAccessService` + `IProcessRunner`，封装 ADB CLI 调用。
- MES：`Mes.Gate` 抽象 + `JekeMesPlugin` Stub（Phase 3 接入真实协议）。

---

## 3. 核心架构原则（硬约束）

> 本节为长期硬约束，从 Phase 1 延续到 Phase 3+，任何阶段都必须遵守。

### 3.1 MVVM 硬规则

- View：
  - 不允许包含任何业务逻辑。
  - Code-behind 仅允许：`InitializeComponent`、简单事件转发到 ViewModel。
- ViewModel：
  - 不允许直接访问 IO / 协议 / ADB / SQLite / 文件系统等。
  - 所有真实业务状态由 Service 层维护，通过 **Snapshot + 属性** 暴露到 UI。
  - 不直接依赖 WPF 特有类型（例如 `Dispatcher` / `Application.Current`）。
- Service：
  - 负责所有流程控制、状态变更和持久化。
  - 不依赖 View / ViewModel / XAML。

### 3.2 状态 vs 事件

- **状态（State）**
  - 使用属性 + 不可变 Snapshot 表达（如 `VerificationSnapshot`、`SessionSnapshot` 等）。
  - 必须可重放、可缓存、可安全导出（例如导出为 Excel）。
  - 通过 `INotifyPropertyChanged`（ViewModel）驱动 UI。
- **事件（Event）**
  - 只用于一次性事实（如 “本次 MES Pre-Gate 失败但降级放行”）。
  - 不得直接作为 UI 状态来源，所有可见状态必须可以从 Snapshot 推导。

### 3.3 Snapshot 模型

Snapshot 对象（`VerificationSnapshot`、`SessionSnapshot`、`LoggingSnapshot` 等）必须：

- 不包含行为（无业务方法）。
- 只读 / 不可变（属性在创建时确定）。
- 可以安全：
  - 绑定到 UI。
  - 序列化 / 写日志 / 导出（例如导出为 Excel 中的一行）。

### 3.4 命令与 UI 规则

- Command 只作为流程入口，不承载复杂逻辑（复杂逻辑在 Service 内）。
- `CanExecute` 只依赖显式状态（Snapshot / 简单属性），不可做隐式 IO。
- 状态变化（如 Snapshot 更新）后必须显式调用 `RaiseCanExecuteChanged`。

---

## 4. 数据与存储架构（Phase 2.5 版本）

### 4.1 SQLite 作为唯一事实源

- 单文件 SQLite 数据库（默认文件名 `SnVerify.db`，位于应用目录）。
- 所有业务结果（SN 校验 / 版本校验）必须落地到 `TestRecord` 表。
- 通过索引保证在 10 万级数据量内仍能稳定查询、导出。

### 4.2 核心物理模型

实际存储模型由四张表构成（建表 SQL 见 `StorageService.CreateTablesAsync`）：

- `Product`
  - `Id`：自增主键。
  - `ProductName`：产品名，`NOT NULL`，`UNIQUE`。
  - `Description`：说明（可空）。
  - `CreatedAt`：创建时间。
- `"Order"`
  - `Id`：自增主键。
  - `OrderName`：订单展示名。
  - `ProductId`：外键 → `Product.Id`。
  - `CreatedAt`：创建时间。
  - 约束：`UNIQUE (OrderName, ProductId)`，保证同一产品下订单名唯一。
- `TestSession`
  - `Id`：自增主键。
  - `SessionName`：业务 SessionId（字符串，如 `OrderId_yyyyMMdd_HHmmss`），`UNIQUE`。
  - `OrderId`：外键 → `"Order"`.`Id`。
  - `StartTime` / `EndTime`：开始 / 结束时间。
  - `Status`：状态字符串（预留）。
- `TestRecord`
  - `Id`：自增主键。
  - `SessionId`：外键 → `TestSession.Id`。
  - `StickerSN`：贴纸/扫码 SN（SN 校验时为真实 SN；版本校验时为 `"-"`）。
  - `DeviceSN`：设备 SN（ADB 读出）。
  - `WifiMac` / `ChipId` / `BoardVersion` / `ChargeBoardVersion`：Phase3 设备扩展字段（ADB 读出）。
  - `ExpectedBoardVersion` / `ExpectedChargeBoardVersion`：Phase3 主板/充电板目标版本（写录时从 VerificationParameter 固化，便于审计与导出）。
  - `Result`：`"PASS"` / `"FAIL"` / `"TIMEOUT"` 等。
  - `FailReason`：失败原因。
  - `VerifyTime`：本次检验时间。
  - `ExpectedVersion`：版本校验期望版本（SN 校验为空）。
  - `ActualVersion`：版本校验实际版本。

### 4.3 SN 唯一性与历史记录规则

> 与 Phase 1 的「同批次内唯一」不同，当前实现为 **全局历史维度**（跨 Session / 跨 Order）。

- **贴纸 SN 唯一性**
  - `IsStickerSnInPassHistoryAsync(stickerSN)` 查询：
    - `SELECT COUNT(1) FROM TestRecord WHERE Result = 'PASS' AND StickerSN = @StickerSN`。
  - 不区分批次 / 订单，任何历史 PASS 记录都视为「贴纸已使用」。  
  - 对应决策树规则：
    - SN 与设备 SN 不匹配，且贴纸 SN 在 PASS 历史中 → FAIL（贴纸重复）。
- **设备 SN 唯一性**
  - `IsDeviceSnInPassHistoryAsync(deviceSN)` 查询：
    - `SELECT COUNT(1) FROM TestRecord WHERE Result = 'PASS' AND DeviceSN = @DeviceSN`。
  - 任何历史 PASS 记录都视为「设备已出站」。  
  - 对应决策树规则：
    - 匹配或不匹配场景下，只要设备 SN 在 PASS 历史中 → FAIL（设备已存在）。
- **绑定历史**
  - `IsBindingInPassHistoryAsync(sn)` 查询：
    - 约定 PASS 时 `StickerSN = DeviceSN`，只按 SN 本身查一次。
  - 用于规则 1/2 的快捷判断：SN 是否已经有 PASS 绑定。

### 4.4 导出与统计视图

- 所有导出数据均通过 `StorageService` 查询，按 `Product / Order / TestSession` 维度聚合。
- `ExportRecordFilter` 作为数据过滤入口，可以区分：
  - SN 校验记录（`StickerSN != "-"`）。
  - 版本校验记录（`StickerSN == "-"`）。
- **按 ProductCode 的导出策略（Phase3 已落地）**：`ExportAggregationService` 对每个 Session 通过 `GetProductCodeBySessionIdAsync` 得到 ProductCode，经 `ISessionExporterFactory.GetExporter(productCode)` 选择 Exporter（如 Legacy、Km001SessionExporter）；KM001 使用 **Km001SessionExporter**，列定义由 `IProductExportRegistry`（ProductExportProfile）与 `IExportValueResolver` 配置化提供，表头文案来自 Resources（Export_Km001_*、Export_Summary_*）；构造 `ExportContext` 后调用 `ISessionExporter.ExportAsync(context)`；扩展新产品可新增 Exporter 实现并在工厂注册，或为新产品在 ProductExportRegistry 中注册配置。
- 聚合层基于各 Session 的导出结果生成 Excel，并聚合 Session 日志文件打 ZIP 包。

---

## 5. SN 校验架构与执行模型（SnMatch）

### 5.1 执行模型

- 一次扫码 → 启动一次原子化 SN 校验流程（`ProcessCoordinator.StartVerificationAsync`）。
- 流程未结束前，`VerificationSnapshot.IsProcessing = true`，UI 禁止新的扫码输入。
- 所有异常（ADB / SQLite / MES 等）必须最终体现在某条 `TestRecord` 上，并更新 Snapshot。

### 5.2 主要参与者

- `MainViewModel`
  - 维护 `ScanInputText`，当检测到 `\r` / `\n` 时，截取第一行作为 SN，调用内部处理。
  - 通过 `IVerificationFlowService` 触发 SN 校验并轮询 `VerificationSnapshot`。
- `IVerificationFlowService` / `VerificationFlowService`
  - 对 `IProcessCoordinator` 的门面封装，暴露统一 Snapshot 和 MES 事件。
- `IProcessCoordinator` / `ProcessCoordinator`
  - 负责：MES Pre-Gate → ADB 读 SN → 决策树 → 写入 `TestRecord` → MES Post-Report → 更新 Snapshot。
- `IStorageService` / `StorageService`
  - 提供所有历史查询（PASS 记录）与持久化。
- `IAdbAccessService` / `AdbAccessService`
  - 与 ADB CLI 交互，读取当前设备 SN。

### 5.3 决策树（简要）

- 情况 A：`StickerSN == DeviceSNNormalized`
  - 若存在 PASS 绑定（`IsBindingInPassHistoryAsync == true`）→ FAIL（设备 SN 已存在）。
  - 若无绑定，则进一步看贴纸 SN 与设备 SN 是否在 PASS 历史中：
    - 都不存在 → PASS，写入 PASS 记录。
    - 任一存在 → FAIL（设备 SN 已存在）。
- 情况 B：`StickerSN != DeviceSNNormalized`
  - 若贴纸 SN 在 PASS 历史中 → FAIL（贴纸重复）。
  - 若设备 SN 在 PASS 历史中 → FAIL（设备已出站）。
  - 若都不在历史 PASS 中 → FAIL（包装不一致）。
- 异常与超时：
  - ADB 超时 → 结果 TIMEOUT，FailReason = `ADB读取设备超时`。
  - ADB 失败 / 其他异常 → 结果 FAIL，FailReason 含详细错误信息。

### 5.4 落库规则

- 所有 SN 校验结果写入 `TestRecord`：
  - `SessionId`：当前 Session 对应的内部 Id。
  - `StickerSN` / `DeviceSN` / `Result` / `FailReason` / `VerifyTime`。
  - 版本字段保持空值。
- 对 FAIL / TIMEOUT 记录：
  - 若该 SN 已有非 PASS 记录 → 覆盖（允许重试）。
  - 若已有 PASS 记录 → 追加一条 FAIL/TIMEOUT，但不破坏原 PASS 事实。

---

## 6. 版本校验架构与执行模型（VersionMatch）

### 6.1 执行模型

- 前置要求：已有激活的 `TestSession`（批次）。
- 输入：目标版本（可从 UI 或 `TestSession.ExpectedVersion`）。
- 流程：
  - `MainViewModel` 调用 `IVersionVerificationFlowService.ExecuteVersionCheckAsync`。
  - `VersionVerificationFlowService` 调 ADB 读取设备信息，比较版本。
  - 将结果写入 `TestRecord`，并更新自身 `VerificationSnapshot`。

### 6.2 主要规则

- 版本比较：忽略大小写、去空格。
- 结果：
  - 相等 → PASS。
  - 不相等 → FAIL，FailReason 描述「目标版本 / 实际版本」。 
  - ADB 超时 / 异常 → TIMEOUT 或 FAIL，FailReason 记录错误。
- 落库约定：
  - `StickerSN = "-"` 表示版本校验记录。
  - `DeviceSN`、`ExpectedVersion`、`ActualVersion` 都会被填充。

---

## 7. MES 集成架构（Mes.Gate）

### 7.1 抽象层设计

- Gate 接口：
  - `IMesPreCheck`：Pre-Gate 接口，处理「是否允许本次校验」。
  - `IMesResultReporter`：Post-Report 接口，处理「结果上报」。 
  - `IMesPlugin`：组合入口接口，要求实现上述二者并提供：
    - `MesCapabilities Capabilities`（是否支持 PreCheck / 是否要求 PreCheck / 是否支持 ResultReport）。
- 上下文模型：
  - `MesContext`：Pre-Gate 上下文（SessionId / OrderId / StickerSN / 时间等）。
  - `TestResultContext`：Post-Report 上下文（SessionId / OrderId / StickerSN / DeviceSN / Result / 时间等）。
  - `MesPreCheckDecision`：`Allow / Reject / DegradedAllow`。
  - `MesPreCheckResult`：Decision + Reason。
  - `MesEventType` / `MesEventArgs`：面向 UI 的弱提示事件。
- 模式控制：`MesMode`
  - `Disabled`：完全不调用 MES。
  - `Enabled`：调用 MES，但 Reject/异常仅做弱提示，不阻断 SN 结果；结果仍以本地决策为准。
  - `Strict`：MES Reject/异常会直接导致本次 SN 判定为 FAIL 或被阻断。

### 7.2 在 SN 流程中的挂载点

- `ProcessCoordinator` 构造函数注入：
  - `IMesPreCheck mesPreCheck`、`IMesResultReporter mesReporter`、`MesMode mesMode`。
- 在 `StartVerificationAsync` 中：
  - **每条 SN 前** 调用 `_mesPreCheck.CheckAsync(MesContext)`（若 `MesMode != Disabled` 且 PreCheck 非 null）。
  - 决策逻辑：
    - Strict + Reject / 异常 → 直接 FAIL / 返回。
    - Enabled + Reject / DegradedAllow → 只发 `MesEventOccurred`，继续本地 SN 校验。
  - SN 校验完成、结果落库后，调用 `_mesReporter.ReportTestResultAsync(TestResultContext)`：
    - 任何 MES Report 失败只记日志 + 发事件，不得修改本地 PASS/FAIL 结果。

### 7.3 杰科 MES 插件当前状态（Phase 2.5 → Phase 3）

- `JekeMesPlugin : IMesPlugin` 已实现为 **骨架 / Stub**：
  - `Capabilities` 声明支持 PreCheck 和 ResultReport。
  - `CheckAsync`：当前固定返回 `Allow` + `"Stub / 骨架实现"`，不调用真实 MES API。
  - `ReportTestResultAsync`：当前为空实现（`Task.CompletedTask`）。
- 计划（Phase 3）：
  - 在 `JekeMesPlugin` 中接入真实杰科 MES 接口，例如：
    - Pre-Gate：`getDutTestFlowResult`、`getDutStationInfo` 等。
    - Post-Report：`postTestDataStr` 等上报接口。
  - 需要在不破坏 `ProcessCoordinator` 冻结约束的前提下完成实现。

---

## 8. 日志策略

- 业务结果：SQLite（`TestRecord`）。
- 运行日志：文件日志（每个 Session 一个 log 文件），只追加不清理。
- `LoggingService`：
  - 写入文本日志文件。
  - 维护 `LoggingSnapshot`（最近 N 条消息），供 UI 展示。
- 日志只用于问题定位，不被业务流程直接依赖（不能通过解析日志来驱动逻辑）。

---

## 9. 代码规范（Cursor Agent / 开发者 强制遵守）

### 9.1 注释与作者标注

- 所有 public 类 / 方法 / 复杂逻辑必须有注释（中英文均可，建议中文为主）。
- AI 生成文件统一在文件头部标注：

```csharp
/// <author>AI Assistant</author>
```

### 9.2 禁止项（红线）

- ViewModel 禁止使用 `Dispatcher` / `Application.Current` / 直接操作 WPF 控件。
- 禁止在 View 中写业务逻辑（仅允许事件转发和极少量 UI 层 glue code）。
- 禁止绕过 Service 层直接操作存储或协议（例如在 ViewModel 中直接 new `SQLiteConnection`）。
- 禁止擅自引入大型 MVVM 框架（如 Prism）或为未来阶段提前堆叠复杂插件化框架。

---

## 10. Phase 3+ 扩展方向（非冻结，供规划使用）

- 多工位 / 多设备并发：
  - 在保持当前 Snapshot + Service 分层的前提下，引入新的协调服务或队列。
- MES 强绑定 / 多厂商支持：
  - 通过 `IMesPlugin` 与 `MesCapabilities` 支持多插件并存、按配置选择。
- 权限 / 审计：
  - 基于 SQLite 或额外存储，引入用户 / 操作审计表，与 `TestSession` / `TestRecord` 做关联。

---

## 11. 对 Cursor Agent 的明确约束（长期有效）

- 生成代码 / 文档时：
  - **优先参考本项目 `docs` 目录中的最新架构与规则文档（尤其是本文件）**。
  - 参考开源项目仅用于理解模式，不直接复制复杂框架结构。
- 禁止：
  - 擅自改变核心分层（View / ViewModel / Service / Infrastructure）。
  - 擅自减少或绕过单元测试覆盖，特别是对 SN 决策树 / MES Gate / StorageService 的测试。

> 结论性原则：
> **Phase 2.5 / 3.0 的目标是在「可测试、可演进」的前提下，支撑 SN 校验 + 版本校验 + MES Gate 的稳定运行，而不是为未来的所有可能性过度设计。**

