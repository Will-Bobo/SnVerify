# YME009 需求 B 实施前技术可行性审查

**审查对象：** `docs/YME009-SN-Pass-Dedup-And-Expected-Data-PRD.md` V0.8  
**审查范围：** 需求 B，项目预期数据 Excel 校验  
**审查方式：** 对照当前工程源码的只读审查  
**审查日期：** 2026-11-24  
**本次未修改代码、测试和既有文档。未运行测试。**

本文是技术审查，不是产品需求。文中的表结构、类名和接入位置是实施建议，不是已经确认的业务规则。PRD DEC-B01 仍然不冻结表名、字段、索引和 SQL。OQ-015～OQ-020 保持开放。

---

## 1. 审查结论

**PASS：可以进入需求 B 实施。**

V0.8 里已经确认的导入、暂存、事务、界面状态和身份比对，能够接到当前 YME009 启动和轮次链路上。不需要先改 PRD，也不需要重做测试启动架构。

没有发现必须先改方案才能动手的阻断项。开放问题继续留着，不挡住已经确认的路径。历史 PASS 命中之后是否还执行需求 B，仍然是 OQ-016，实施时不要把其中任何一种写成规则。

---

## 2. 现有架构和代码复用情况

### 2.1 可以直接复用

| 能力 | 位置 |
|---|---|
| 开始测试入口和前置校验 | `MainViewModel.StartBatchAsync`、`CanExecuteStartBatch`、`TryRejectYme009StartAsync` |
| Session 名称、Product、Order 的现有创建顺序 | `SessionLifecycleService.CreateAndStartSession`、`SessionIdGenerator.Format` |
| SQLite 单连接、锁和单条事务样例 | `StorageService` 的 `_connection`、`_lockObject`；`SaveTestRecordAsync` 中的 `BeginTransaction` / `Commit` / `Rollback` |
| 按内部 SessionId 取得项目名和订单名 | `StorageService.GetYme009BatchScopeBySessionIdAsync` |
| 表结构幂等创建和列迁移方式 | `StorageService.CreateTablesAsync`、`MigrateTestRecordAddColumnsAsync` |
| 扫码 SN 与设备 SN 的现有比较 | `Yme009TextComparison.Equals`，门禁在 `Yme009PassHistoryDecision.CanQueryHistory` |
| 轮次读取、五项判定、去重、保存 | `Yme009RoundCoordinator.InspectAsync`、`Yme009InspectionJudge.Judge`、`Yme009PassHistoryDecision.Apply`、`Yme009TestRecordMapper.Create` |
| IMEI1、IMEI2、WiFi MAC 的设备读取 | `Yme009AttributeCatalog` 与 `Yme009ParsedAttributes` 已包含这三个信息字段 |
| 工位快照和五项卡片 | `Yme009WorkstationSnapshot`、`Yme009WorkstationProjection.FillJudged`、`MainWindow.xaml` 中 `UniformGrid Columns="5"` |
| 现有状态区 | YME009 产品下可见的状态卡片，绑定 `Yme009HeadlineKind`；扫码行旁的 `Yme009ProcessPrompt`；底部 `StatusBarMessage` |
| 弹窗抽象 | `IUserDialogService.ShowWarning`，实现在 `WpfUserDialogService` |
| UI 线程封送 | `MainViewModel` 用构造时捕获的 `SynchronizationContext`。现有注释禁止在 ViewModel 中使用 `Dispatcher` |
| 重复点击的现有门闩 | `CanExecuteStartBatch` 在 `IsSessionActive`、`IsSelfChecking`、`IsProcessing` 时返回 false |
| Excel 组件 | 工程已引用 EPPlus 7.0.0。当前只用于导出，见 `StorageService` 导出方法和 `Yme009SessionExporter` |

### 2.2 需要新增

- 内存暂存对象：路径、文件名、记录数量、SHA-256、是否通过预校验。放在 ViewModel 或一个无 UI 的暂存模型中。进程退出后不落库，自然失效。
- 预校验和身份比对的纯函数。MAC 比较不能直接调用 `Yme009TextComparison.Equals`，因为需求 B 还要忽略大小写并去掉冒号。
- `.xlsx` 的只读逐行解析。不要用现有 `new ExcelPackage(FileInfo)` 充当流式读取。EPPlus 会把工作簿载入内存，不能满足「不把全部设备记录载入内存」。
- 同一次文件读取上的 SHA-256。先做开始测试前的整文件指纹；正式导入时在实际读取的那一次流上再计算，结束时核对。
- `IStorageService` 上的一个事务方法：在同一把 `_lockObject` 和同一个 `SQLiteTransaction` 中写入 TestSession、预期数据集和设备行。
- 新表和唯一索引，沿用 `CreateTablesAsync` 的 `IF NOT EXISTS` 方式。名称由技术设计确定。
- `IUserDialogService` 增加选择 `.xlsx` 的方法。工程里目前没有 `OpenFileDialog`。
- 第六张结果卡片，以及预期数据校验的失败码和工位文案。失败码登记到 `Yme009FailReasonCodes`，不要在流程里散落新字符串。

### 2.3 需要调整的现有流程

- `StartBatchAsync` 在 YME009 且已有通过预校验的暂存时，把 `CreateAndStartSession` 延后到导入事务提交成功之后。无暂存时继续调用现在的 `CreateAndStartSession`。
- 事务提交成功之前，不调用 `_loggingService.StartSession`，不创建 `_verificationFlowService`，不把 `SessionSnapshot` 设为活动。
- `CanExecuteStartBatch` 在导入进行中返回 false。现有判断挡不住「Session 尚未创建时的第二次点击」。
- `Yme009RoundCoordinator.InspectAsync` 增加一个独立的预期数据步骤。不改 `Yme009PassHistoryDecision.Apply` 的判定、失败码和保存时机。
- YME009 状态卡片现在由 `Yme009HeadlineKind` 固定成「等待扫码 / 检验中... / PASS / FAIL」。导入三态要显示在这张已有卡片或它旁边已经绑定的 `Yme009ProcessPrompt` 上，不新开窗口。

### 2.4 影响范围

影响限于 YME009 的开始测试、存储事务、工位卡片和轮次判定。KM001、KM008、SOLTAG25 和 `ProcessCoordinator` 的旧规则链不用改。YME009 的扫码入口已经是 `MainViewModel.EnterIndependentProductRoundAsync`，不是 `ProcessCoordinator`。需求 B 应接在这条已有专属链路上。

---

## 3. 需求 B 技术实施映射

### 3.1 Excel 选择与预校验

现有代码没有选择 Excel 的入口。`IUserDialogService` 只有导出用的 `ChooseFolder`。

拟实施：在 `WpfUserDialogService` 增加选择单个 `.xlsx` 的方法，由 `MainViewModel` 调用。预校验放在不依赖 WPF 的读取组件中：只打开第一个 Sheet，按第一行表头识别 `SN`、`WIFIMAC`、`IMEI1`、`IMEI2`，逐行检查空值和 SN 重复，只保留 SN 集合。成功后把轻量状态交给 ViewModel。失败沿用 `ShowWarning`，标题使用 PRD 已确认的「导入失败」。

### 3.2 暂存、替换和清除

`MainViewModel` 已经持有会话输入和 YME009 工位状态，适合保存这份内存配置。替换成功时覆盖。清除时丢掉这份状态。不要调用 `StorageService`。

OQ-020 未确认替换失败后是否保留上一次暂存。成功路径可以做。失败路径不要选定保留或清除。

### 3.3 点击开始测试

位置是 `MainViewModel.StartBatchAsync`。现有顺序已经包括项目名、订单名、`TryRejectYme009StartAsync`、项目名重复确认，然后才 `CreateAndStartSession`。

有 Excel 暂存时，这些前置校验仍然先做。它们不创建 TestSession。校验失败则保持今天的行为，不进入导入。

无 Excel 暂存时，从 `CreateAndStartSession` 起完全沿用现在的方法，不显示导入三态。

### 3.4 文件存在性与 SHA-256

在进入正式导入前，对暂存路径做 `File.Exists` 和打开读取检查，并用 `SHA256` 重算整文件指纹。不一致就停在 `StartBatchAsync`，用现有警告框提示重新导入，不创建 Session。

这一步是 PRD 要求的第一次指纹核对，发生在正式导入之前。

### 3.5 后台正式导入

`StartBatchAsync` 已经使用 `await Task.Run`。正式导入放在同一种后台执行里，完成后回到 UI 上下文更新状态。

解析使用新的前向读取器，而不是 EPPlus 的整包加载。读取过程中同步更新 SHA-256。导入结束再与预校验指纹比较。比较失败则回滚，不把文件再完整读一遍。

ZIP 包解析通常会移动流位置。实施时让哈希只跟着这一次实际读取累加；如果解析器必须寻址，就在这一次磁盘读取中同时完成哈希和供解析的缓冲，不能为了核对再开第二次全文件读取。

### 3.6 SQLite 事务和 Session 关联

`CreateAndStartSession` 现在分成多次独立提交：Product、Order，然后 `StorageService.CreateSessionAsync`。每次 `CreateSessionAsync` 自己加锁并立即插入，没有把 Session 和别的写入放进同一事务。

Product 和 Order 可以继续在事务前按现有逻辑准备。它们今天也不是和 TestSession 原子提交的。PRD 要求原子的是 TestSession、预期数据集和设备行。

拟实施一个存储方法，在持有 `_lockObject` 期间：

1. 插入 `TestSession`。
2. 插入一条数据集记录，并用 Session 唯一索引保证一个 Session 只有一个数据集。
3. 逐行插入设备记录，并用数据集内 SN 唯一索引保证不重复。
4. 任一步失败就 `Rollback`。
5. `Commit` 之后才把新 SessionId 返回给 `StartBatchAsync`。

日常连接没有执行 `PRAGMA foreign_keys = ON`。外键声明目前不会自动生效，这一点和现有 TestRecord 外键相同。一个 Session 一个数据集要靠唯一索引，不能只靠外键声明。

检测查询沿用 `GetInternalSessionIdBySessionNameAsync` 得到内部 Id，再按该 Id 找数据集，按 SN 找设备行。SN 查询键使用去掉首尾空格后的原始大小写，不使用 `Yme009TextComparison.Normalize` 之后的另一套规则去改大小写。

### 3.7 导入完成与待测试

提交成功后，先把现有 YME009 状态区设为「导入完成」和文案「预期数据导入完成。」记录数量可以显示。然后自动进入「待测试」，再执行现在提交成功后的动作：`StartSession` 日志、`_flowServiceFactory.Create`、快照、参数保存和 `Settings.Default.Save`。

YME009 在 `ShowAndroidVersionSection` 为真时，参数快照仍然在 Session 已经存在之后保存，和今天的相对顺序一致。它不必塞进 Excel 事务。

「待测试」对应现在的 `ApplyYme009SessionBoundary` 进入活动 Session 后调用的 `Yme009WorkstationSnapshot.IdleWaiting`。导入完成停留时长 PRD 没有规定，实施时只要求短暂显示后自动切换，不另定毫秒数。

### 3.8 检测时查询预期数据

位置是 `Yme009RoundCoordinator.InspectAsync`。这里已经先 `Yme009InspectionJudge.Judge`，再 `ResolvePassHistoryAsync`，再 `Yme009PassHistoryDecision.Apply`，最后 `SaveTestRecordAsync`。

SN 不一致时，`CanQueryHistory` 已经返回 false。需求 B 使用同一门禁，不查询预期数据，卡片显示「未执行」。

未配置数据集时，卡片显示「未启用」，不增加 FAIL。

### 3.9 四个身份字段比对

设备侧字段已经在 `Yme009ParsedAttributes.Imei1`、`Imei2`、`WifiMac`，并由 `FillJudged` 显示。比对函数放在 Domain，输入是预期行和这三个字段加 SN。找不到 SN、字段无效、任一不匹配都只产生这项自己的 FAIL。不要让这个 FAIL 提前返回而跳过五项读取。五项读取在 `InspectAsync` 里发生在判定之前，保持这个顺序即可。

### 3.10 测试项显示和综合判定

在 `MainWindow.xaml` 现有五项 `UniformGrid` 上增加一列，绑定新的判断和原因属性。`Yme009WorkstationSnapshot` 增加对应字段。`Yme009PassHistoryDecision` 继续只改主失败原因，不了解 Excel。

主失败原因如果要纳入预期数据失败，必须接在现有优先级之后，并且不改变 `Yme009PassHistoryDecision` 已经确认的 SN 不一致、历史 PASS 和查询异常优先级。OQ-016 未确认时，不要为了排序而规定命中历史 PASS 后是否还运行这项。

### 3.11 异常回滚和重复启动

文件不存在、不可访问、预校验指纹不一致：事务尚未开始，不插入 Session。文件流指纹不一致、解析失败、写入失败：回滚已开始的事务。`StartBatchAsync` 的现有 `catch` 今天是空的，导入失败必须把明确原因送到 `ShowWarning` 或状态区，并保持 `SessionSnapshot` 非活动，允许再次点击。

重复执行保护放在 `StartBatchAsync` 入口：先置导入中标志并 `StartBatchCommand.RaiseCanExecuteChanged`，方法结束时清除。存储方法本身也只提交一次。不要依赖两次点击自然撞上 `SessionExistsAsync`。

---

## 4. 技术风险与阻断项

| 问题 | 现有代码 | 是否阻断 | 建议 | 是否要改 PRD |
|---|---|---|---|---|
| TestSession 现在单独提交，早于导入 | `SessionLifecycleService.CreateAndStartSession`、`StorageService.CreateSessionAsync` | 否 | 有 Excel 时把 Session 插入移入导入事务，提交成功后再激活会话 | 否 |
| 没有跨多表事务的现成方法 | `SaveTestRecordAsync` 只有单表事务 | 否 | 新增一个持锁事务方法，内部不要再进其他会异步重入的存储方法 | 否 |
| 日常连接未打开外键 | `EnsureConnectionInitialized` 的连接串没有 `Foreign Keys=True` | 否 | 用唯一索引保证一个 Session 一个数据集、一个数据集内 SN 不重复 | 否。表结构仍属 DEC-B01 |
| EPPlus 会整包载入内存 | `StorageService`、`Yme009SessionExporter` 的 `ExcelPackage` | 否 | 导出继续用 EPPlus。导入另写前向读取器 | 否 |
| 文件流哈希与 ZIP 寻址可能互相干扰 | 工程中没有现成哈希流 | 否 | 一次实际读取同时完成解析和哈希，失败回滚 | 否 |
| 开始测试在 Session 创建前可以连点 | `CanExecuteStartBatch` 只看活动 Session 和处理中 | 否 | 导入期间关闭开始命令 | 否 |
| 状态卡片文案是固定枚举，不是自由句子 | `MainWindow.xaml` 中 YME009 状态卡片的 `DataTrigger` | 否 | 在这张卡片或已绑定的 `Yme009ProcessPrompt` 上显示三态文案 | 否 |
| 没有文件选择对话框 | `IUserDialogService` | 否 | 给现有对话框接口加一个方法 | 否 |
| 只有表头没有设备记录 | 无对应代码 | 否 | OQ-015。不要判通过，也不要判成已确认的导入失败 | 否，保持开放 |
| 历史 PASS 命中后是否执行需求 B | `InspectAsync` 先五项、再去重 | 否 | OQ-016。两种接法架构都允许。不要在代码里选定 | 否，保持开放 |
| 预期数据查询异常如何判定 | 去重异常已有 `QueryFailed`，预期数据没有对应规则 | 否 | OQ-017。正常命中、未命中可以做。异常不要当成未找到或 PASS | 否，保持开放 |
| 预期值是否写入 TestRecord | `TestRecord` 尚无这些列 | 否 | OQ-018。本次不把预期值写入 TestRecord | 否，保持开放 |
| 提交后测试未开始就结束 Session | `EndBatchAsync` 对无 TestRecord 的结束有自己的提示 | 否 | OQ-019。不新增清理或补偿 | 否，保持开放 |
| 替换预校验失败后旧暂存是否保留 | 无暂存代码 | 否 | OQ-020。只实现替换成功。失败分支不选定 | 否，保持开放 |

没有第 3 类问题：不存在「不先改 PRD 或架构就无法实现已确认需求」的阻断项。

---

## 5. 分阶段实施建议

这是一条连续实现顺序，不是多套独立设计。

1. 先写 Domain 预校验、暂存状态和四字段比对，用测试固定已确认规则。空文件、替换失败和查询异常不写死结果。
2. 加上前向 `.xlsx` 读取和同一次读取的 SHA-256。
3. 加上存储事务：创建 Session、数据集和设备行，失败回滚；并提供按 SessionId 与 SN 的查询。
4. 把文件选择、清除和暂存接到 `MainViewModel`。无文件时 `StartBatchAsync` 保持现有路径。
5. 有文件时，在现有前置校验之后做指纹检查和后台导入；成功后才激活 Session，并在现有状态区走「正在导入数据」「导入完成」「待测试」。
6. 最后把身份校验接进 `Yme009RoundCoordinator.InspectAsync` 和第六张卡片。SN 不一致显示「未执行」。未启用显示「未启用」。不要改需求 A 的判定函数。OQ-016 的命中后续接不接，留在调用点外，不在这一步做决定。

---

## 6. 最终判断

**可以按照 PRD V0.8 开始需求 B 里已经确认部分的实施。**

可以保留、并且不挡住已确认部分的开放问题：

- OQ-015：只有表头、没有设备记录。
- OQ-016：需求 A 命中历史 PASS 后是否继续需求 B。
- OQ-017：预期数据查询失败、超时或数据库异常如何判定。
- OQ-018：是否把预期值额外写入 TestRecord。
- OQ-019：事务提交后测试尚未开始或 Session 随即结束时，已写入数据如何处理。
- OQ-020：替换预校验失败后，上一次待用配置是否保留。

实施时只做 PRD 已经确认的路径。上面六项继续保持开放，不在代码里替它们选择结果。
