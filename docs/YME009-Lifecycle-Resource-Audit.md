# YME009 生命周期与资源管理现状审查

| 项 | 说明 |
|---|---|
| 文档性质 | 第五章「生命周期与资源管理」的前置审查报告。只收集源码和已有设计证据，不编写第五章正文，不修改任何已有文件。 |
| 审查日期 | 2026-11-21（Asia/Shanghai） |
| 主工程源码快照 | `/workspace/yme009-src/SnVerify`（只读，对应 2026-11-21 12:18 的工程快照；用户机路径为 `D:\wpf_workspace\SnVerify`） |
| 参考工程源码快照 | `/workspace/yme009-src/Mifi-YF_YunkeProductionTester`（只作对照，不得视为 YME009 必须采用的方案，不得照搬 SMT/CIT） |
| 业务依据 | PRD V0.6：`/workspace/uploads/YME009-Packaging-Station-PRD-V06.md`（不使用更旧的 V0.1 / V0.4） |
| 设计依据 | `/workspace/YME009-Technical-Design.md`（第一至第四章，含第四章最新修订） |
| 本报告输出 | 仅新增本文件。不修改 PRD、技术设计、源码、测试、数据库、配置或 uploads 副本。 |

本报告结论一律区分两类证据：

- **源码事实**：当前 SnVerify 已经实现的行为，附文件与行号（快照路径）。
- **已定设计**：第一至第四章或 PRD V0.6 已经确定、本审查不得重新讨论的规则。引用小节编号，不重开方案。

标为「建议方案」但尚未实施的组件名（如 `Yme009RoundCoordinator`）只作为已有设计候选被引用，不构成源码现状，也不在本报告中展开新的实现方案。

---

## 0. 审查范围与方法

审查围绕「YME009 一轮检测」的生命周期，覆盖以下五个主题：

1. 检测轮次如何开始和结束，以及重复启动、重复结束、状态残留风险。
2. 取消与中断：`CancellationToken` 的创建、传递和现有行为。
3. 设备连接资源：CDC 串口、ADB Transport ID、临时 / 正式连接上下文。
4. 资源释放：串口、进程、`CancellationTokenSource`、数据库事务；正常路径与异常路径是否一致。
5. 界面与流程协调：检测进行中的扫码、新轮次、批次切换，以及上一轮状态污染。

审查方法：通读主工程扫码入口、Phase3 协调、设备访问、存储、Session 与窗口生命周期相关源码；对照 PRD V0.6 第 5、11、12、13.6、16 节和技术设计第一至第四章已确定内容。参考工程只用于说明「SnVerify 当前没有对等实现」或「设计已允许参考但不得迁入 SMT/CIT」，不把其实现写成 YME009 必选方案。

本轮明确不做：编写第五章正文；重新讨论第二至第四章已定原则（含临时 vs 正式上下文、AT 三态与清理、保存成功定义、方案 B、状态 4/5/6、保存未完成是否关闭 ADB、不复制 Phase3 总 catch）；提出全局生命周期框架或通用资源管理框架；为尚未明确的行为补充业务规则或替用户定案；重开 D01–D16，或发明 AT / 串口 / Ready 值 / 身份关联算法。

---

## 0.1 源码现状与已定设计对照（总览）

| 主题 | 源码现状（SnVerify） | 已定设计（YME009） | 第五章需要接着定义的部分 |
|---|---|---|---|
| 一轮从何时开始 | 扫码或「人工检验」进入 `HandleScanInputAsync`，由 `ProcessScanAsync` 将 Snapshot 置为 `Processing` | 一次扫码对应一轮；YME009 在 `ProcessScanAsync` 取得 Profile 后分流给专属轮次协调。轮次包含设备接入生命周期和（仅接入成功后的）业务检测生命周期 | 轮次协调如何接管并保持过程锁，覆盖 30 秒等待；全部出口如何结束本轮并释放锁 |
| 正常结束 / 业务 FAIL / 读取异常 | 都走向 `SavePhase3ResultAsync` + `VerificationSnapshot.Completed`；`StatusText` 显示 PASS 或 FAIL | 完整检测结果走独立保存路径；只有保存成功（方法正常返回、Commit 成功且 `record.Id > 0`）才允许宣布保存成功；保存成功后再关闭 ADB；关闭失败不改变已保存结果 | 如何把专属轮次结果投影到界面，避免沿用「一律 Completed」；等待态不能只显示「正在检验...」 |
| 软件异常 / 保存失败 | `ProcessScanAsync` 总 catch 会写成 `EXCEPTION: ...` 的 FAIL 记录并显示完成；保存包装方法空 catch | 不得复制该总 catch；不得把未知异常或数据库异常写成读取失败码；状态 5 / 6 不得显示为已完成 | 这些出口如何结束过程锁、作废上下文、关闭串口；是否发送关闭 AT 仍为待决策 |
| 取消 | Phase3 正式扫码路径没有操作员取消入口，也不传递 `CancellationToken` | 检测中途不新增取消入口（3.11）；等待期间是否允许取消仍待确认。接入等待循环必须预留可响应取消令牌的能力（2.6.3 建议，非已确认业务规则） | 令牌由谁创建、传到哪些阶段、各阶段若收到取消应做何种技术收尾（不得自行决定是否允许取消） |
| 设备连接资源 | 无 CDC / AT / Transport ID / 临时或正式连接上下文。Phase3 使用进程级 `DeviceSessionManager` 做环境预热和默认 ADB 命令 | 临时上下文只用于接入中的 AT 与必要清理；正式上下文在身份关联成功并锁定 Transport ID 后才交给业务检测；下一轮不得沿用 | 两类上下文的持有者、结束与作废时机，以及拔出、发现失败、窗口关闭时的清理清单（关联算法本身仍不在此定案） |
| 资源释放 | `ProcessRunner` 在超时 / 取消 / finally 中 Kill 并 Dispose 进程；SQLite 为进程级长连接 + `_lockObject`；现有保存无显式事务；应用退出没有统一 Dispose | 每轮结束都要停止本轮观察、关闭本轮串口、作废两类上下文；AT 同一时刻只进行一次；保存采用方案 B 事务，Commit 前不得把 Id 暴露给调用方 | 串口、观察循环、CTS、进程、事务在各出口上的释放责任，且不得引入连接恢复框架 |
| UI 门闩 | `IsProcessing` 忽略新扫码；Session 激活后不能改产品、不能再「开始测试」；检验中不能「结束测试」；自检按钮不以 `IsProcessing` 为门闩 | 不取消过程锁，不要求并行检测；等待期间再次扫码的业务规则仍待确认；忙碌提示文案仍待确认（Q19） | 等待态下过程锁如何保持；自检与专属接入资源如何避免并发冲突（自检是否仍走现有 ADB 自检仍开放） |

---

## 1. 当前源码已经具备的能力

本节只描述 SnVerify 现有实现，不把它直接写成 YME009 的业务规则。

### 1.1 检测轮次生命周期（源码事实）

现有工程没有独立的「检测轮次」表或轮次对象。一次检验是一次扫码触发的 `ProcessCoordinator` 调用，用 `VerificationSnapshot` 的 Idle / Processing / Completed 表示状态。依据：`Domain/State/VerificationSnapshot.cs` 第 63–82 行；技术设计 1.2.3。

**开始事件：**

1. 操作员先「开始测试」，`MainViewModel.StartBatchAsync` 调用 `SessionLifecycleService.CreateAndStartSession`，再 `VerificationFlowServiceFactory.Create` 得到绑定该 Session 的 `ProcessCoordinator`。依据：`ViewModels/MainViewModel.cs` 第 1229–1312 行；`Infrastructure/VerificationFlowServiceFactory.cs` 第 54–69 行。
2. 一轮检验从扫码完成或点击「人工检验」开始，入口是 `MainViewModel.HandleScanInputAsync`。Phase3 产品（含将来挂载的 YME009）走 `StartPhase3VerificationAsync` → `ProcessCoordinator.ProcessScanAsync`。依据：`MainViewModel.cs` 第 1569–1578、1840–1883 行；`Services/Coordination/VerificationFlowService.cs` 第 62–70 行。
3. `ProcessScanAsync` 在方法开头用 `_lockObject` 检查 `!_snapshot.IsProcessing`，若空闲则立即 `UpdateSnapshot(VerificationSnapshot.Processing(sn, _sessionId))`。正在处理中的第二次调用直接 `return`，不排队。依据：`Services/Coordination/ProcessCoordinator.cs` 第 367–390 行。

**正常结束、业务 FAIL、读取失败、软件异常如何结束（现有 Phase3）：**

- 规则链 `RulePipelineExecutor.ExecuteAsync` 返回后，无论 PASS 还是业务 / 读取类 FAIL，都调用 `SavePhase3ResultAsync`，再 `UpdateSnapshot(Completed(...))`。`Completed` 的 `IsProcessing` 为 `false`。依据：`ProcessCoordinator.cs` 第 435–459 行；`VerificationSnapshot.cs` 第 79–82 行。
- 设备读取失败在规则链内部多被转换成 `ADB_READ_FAIL` 等 FAIL 结果返回，不作为未捕获异常；随后同样保存并 Completed。依据：`Services/Rules/RulePipelineExecutor.cs` 第 64–94 行。
- 未捕获异常由 `ProcessScanAsync` 总 `catch (Exception ex)` 写成 `EXCEPTION: {ex.Message}` 的 FAIL，再调用 `SavePhase3ResultAsync` 并 Completed。依据：`ProcessCoordinator.cs` 第 461–467 行。YME009 **不得复制**这条路径（见第 4 节）。
- `SavePhase3ResultAsync` 内部有空 `catch`，保存失败「不应中断上层流程」，因此即使没有真正落入数据库，Snapshot 仍可能被置为 Completed。依据：`ProcessCoordinator.cs` 第 723–757 行。这也是第四章已指出、YME009 新路径必须避开的行为。

`HandleScanInputAsync` 在 `await StartPhase3VerificationAsync` 返回后，还有一段按 100ms 轮询 `Snapshot.IsProcessing` 的循环。由于 `ProcessScanAsync` 在返回前通常已经 Completed，这段轮询在正常路径上基本是空转。若异常在置 Processing 之后、Completed 之前逃出到 ViewModel 的空 `catch`，该 `catch` 只清空扫码框，不复位 Snapshot，过程锁可能一直保持。依据：`MainViewModel.cs` 第 1885–1909 行。

`ProcessCoordinator.Reset()` 会无条件把 Snapshot 拉回 `Idle`，不检查是否仍在处理，也不取消正在执行的任务。生产路径中，`MainViewModel` **并不调用** `_verificationFlowService.Reset()`；结束测试只把界面快照置 Idle（`ResetVerificationUiToIdle`），并在下一次「开始测试」时创建新的 FlowService / Coordinator。`ScanInputService` 虽有调用 `Reset` 的代码，但 `ServiceFactory` 只 `new ScanInputService()` 且未注入到 `MainViewModel`，生产扫码不走该服务。依据：`ProcessCoordinator.cs` 第 474–479 行；`MainViewModel.cs` 第 762–770、1402–1403 行；`Infrastructure/ServiceFactory.cs` 第 99、131–144 行；`Services/Input/ScanInputService.cs` 第 198 行。

**重复启动、重复结束、状态残留风险（源码事实）：**

- 同一 Coordinator 上，`IsProcessing` 锁可防止第二次 `ProcessScanAsync` 并行执行。ViewModel 侧同样在 `IsProcessing` 为真时忽略新扫码。依据：`ProcessCoordinator.cs` 第 376–390 行；`MainViewModel.cs` 第 1850–1853 行。
- 若有人在处理中调用 `Reset()`，过程锁会被清掉，第二轮可能与尚未结束的第一轮重叠。生产 UI 当前不调用该方法，但这是方法本身的风险。
- 总 catch 若在 `SavePhase3ResultAsync` 之后、`UpdateSnapshot(Completed)` 之前再抛出，或 ViewModel 空 catch 接到逃逸异常，`IsProcessing` 可能残留为真，后续扫码会被一直忽略。
- Session 层：`CreateAndStartSession` 在已有活动 Session 时抛错，不开始新 Session。`EndBatchCommand` 在 `IsProcessing` 或自检时不可执行，故正常 UI 不能在一轮检验未结束时结束批次。`EndSession` 只更新内存 Snapshot，不把 `EndTime` 写入数据库。依据：`Services/Session/SessionLifecycleService.cs` 第 65–71、194–210 行；`MainViewModel.cs` 第 894 行；PRD 13.6 与技术设计 1.8 第 8 项已记录，本期默认不修复。
- 没有窗口关闭或进程退出时的轮次收尾：`App.xaml.cs` 为空，`MainWindow.xaml.cs` 无 `Closing` / `Closed` 处理，也不 Dispose `StorageService` / `LoggingService`。进程结束依赖操作系统回收。

### 1.2 取消与中断（源码事实）

**正式 Phase3 扫码路径不支持操作员取消，也不传递取消令牌。**

- `IVerificationFlowService.StartPhase3VerificationAsync`、`IProcessCoordinator.ProcessScanAsync`、`IRulePipelineExecutor.ExecuteAsync`、`IDeviceAccessService.ReadDeviceInfoAsync`、`IStorageService.SaveTestRecordAsync` 的签名都没有 `CancellationToken`。依据：相应接口与实现文件。
- 界面没有取消按钮或取消命令。`HandleScanInputAsync` 也没有创建 `CancellationTokenSource`。

现有代码里出现的 `CancellationToken`，用途是**超时或可选传入**，不是「停止本轮检测」的业务取消：

| 位置 | 谁创建 | 传到何处 | 取消时的现有行为 |
|---|---|---|---|
| `AdbDeviceService.ReadDeviceInfoAsync` | 方法内部 `new CancellationTokenSource(TotalTimeoutMs)`，总超时 10000ms | `EnsureSessionReadyAsync`、`DeviceCommandExecutor.ExecuteAsync` | 超时取消后，由下层返回或抛出；规则链再把它变成读取失败或未捕获异常。**没有调用方令牌可传入**。依据：`Infrastructure/DeviceAccess/Service/AdbDeviceService.cs` 第 25–26、58–65 行 |
| `DeviceSessionManager.EnsureSessionReadyAsync` | 接受可选 token；Bootstrap 再 `CreateLinkedTokenSource` 并 `CancelAfter` | `ProcessRunner.RunAsync` | 收到取消会 `ThrowIfCancellationRequested`。依据：`Infrastructure/DeviceAccess/Session/DeviceSessionManager.cs` 第 42–72、88–94 行 |
| `AdbAccessService.ReadDeviceSnAsync` / `ReadDeviceInfoAsync` | 将调用方 token 与 `CancelAfter(TotalTimeoutMs)` 链接 | `ProcessRunner` | 主要用于 Legacy / 自检，不在 Phase3 `ProcessScanAsync` 中调用。`StartVerificationAsync` 调用 `ReadDeviceSnAsync()` 时不传入 token。依据：`Services/Adb/AdbAccessService.cs` 第 145–150 行；`ProcessCoordinator.cs` 第 249 行 |
| `VersionVerificationFlowService.ExecuteVersionCheckAsync` | 参数可选，默认 `default` | `AdbAccessService.ReadDeviceInfoAsync` | `MainViewModel` 传入的是 `CancellationToken.None`。该服务已标记 Obsolete，不是 YME009 路径。依据：`ViewModels/MainViewModel.cs` 第 1761 行；`Services/Coordination/VersionVerificationFlowService.cs` 第 20、100–115 行 |
| `ProcessRunner.RunAsync` | 接受 token | 自身 `Task.Run(..., cancellationToken)` 与 `Task.Delay` | 取消时若进程未退出则 `Kill`，**返回 `ProcessExecutionResult.Timeout()`，不向外抛出 `OperationCanceledException`**。调用方会把取消和超时看成同一种结果。依据：`Services/Adb/ProcessRunner.cs` 第 69–81、102–119 行 |

因此：取消发生在「设备读取」阶段时，现有 Phase3 没有业务取消可发生；若底层超时 CTS 触发，表现是命令超时或读取失败，而不是「本轮已取消、不保存」。取消发生在「数据库保存」阶段时，`SaveTestRecordAsync` 不接受 token，一旦进入 INSERT，现有实现无法合作式取消。取消发生在「ADB 关闭」阶段时，**现有正式扫码路径没有关闭设备 ADB 的实现**，该问题对源码现状不适用，只对 YME009 设计有效。

设备拔出或连接丢失：现有 Phase3 没有 USB / CDC 拔出监听，也没有按 Transport ID 跟踪在线状态。断开通常表现为后续 `adb` 命令超时、失败或输出为空，再由规则链归为读取失败。没有针对拔出的专门清理。

### 1.3 设备连接资源（源码事实）

SnVerify 生产代码中**没有** `SerialPort`、CDC 枚举、AT 命令发送、Transport ID 锁定，也没有临时设备身份上下文或正式设备连接上下文。对 `SerialPort` / `CDC` / `TransportId` 的源码检索在 `*.cs` 中无匹配。这与技术设计 1.8 第 12 项一致。

Phase3 实际使用的设备资源是：

- **进程级单例**：`ServiceFactory` 创建一份 `ProcessRunner`、`DeviceSessionManager`、`DeviceCommandExecutor`、`AdbDeviceService`，供所有 Session 和自检共用。依据：`Infrastructure/ServiceFactory.cs` 第 83–96、144 行。
- **`DeviceSessionManager`**：注释写明 SessionReady 是「环境级屏障」，「不绑定设备身份」，进程内 Shell warmup 只做一次；每批可执行 Bootstrap 命令，但生产配置若无 Bootstrap，则只是预热。`CheckMultipleDevices` 在该类型上有定义，但生产扫码路径不调用（仅自检使用 `AdbAccessService.CheckMultipleDevices`）。依据：`DeviceSessionManager.cs` 第 14–27、39–58、146–174 行；技术设计 1.5.2。
- **ADB 命令**：`DeviceCommandExecutor` 通过 `ProcessRunner` 启动 `adb.exe`，不附加 `-t` Transport ID 或 `-s` serial，即使用 ADB 默认设备。依据：`Infrastructure/DeviceAccess/Command/DeviceCommandExecutor.cs` 第 29–33 行。
- 临时连接与正式连接的区分在现有源码中不存在，因此也没有「结束临时 / 正式上下文」的实现。

### 1.4 资源释放（源码事实）

| 资源 | 创建与持有 | 正常路径 | 异常路径 | 跨轮次 / 重复或遗漏 |
|---|---|---|---|---|
| ADB 子进程 | `ProcessRunner` 每次 `RunAsync` 新建 `Process` | 等待退出后在 `finally` 中若未退出则 `Kill`，再 `Dispose` | 超时、`OperationCanceledException` 或其他异常同样进入 `finally` | 单次命令的进程释放是对称的。没有跨轮次复用同一个 `Process` 对象。Kill 失败被吞掉。依据：`ProcessRunner.cs` 第 29–145 行 |
| `CancellationTokenSource` | `AdbDeviceService` 用 `using` 包住一次 `ReadDeviceInfoAsync`；`DeviceSessionManager` 的 Bootstrap 链接 CTS 也在 `using` 中；`AdbAccessService.GetDeviceSNAsync`（已废弃）有手动 `Dispose` | 方法返回即释放 | `using` 保证释放 | Phase3 路径没有轮次级 CTS |
| SQLite 连接 | `StorageService` 进程内长连接，操作时用 `_lockObject` 串行化 | 连接保持打开，供后续调用复用 | 单次命令失败抛出，不关闭长连接 | 跨轮次、跨 Session 复用同一连接是有意设计。`Dispose` 会关闭连接，但应用退出路径没有调用。依据：`Services/Storage/StorageService.cs` 第 30–31、75–81、1210–1245、2059–2079 行 |
| 保存事务 | **现有 `SaveTestRecordAsync` 没有 `BeginTransaction`**。在锁内先后执行自动提交的 `INSERT` 和 `SELECT last_insert_rowid()`，并立刻把 Id 赋给 `record.Id` | 两句都成功才返回 | 若 INSERT 已提交、取 Id 失败，调用方看到异常，但行可能已在库中 | 这是第四章方案 B 要收紧的窗口，本审查不重开方案。依据：`StorageService.cs` 第 1199–1245 行 |
| 串口 | 无 | 无 | 无 | YME009 必须新建 |
| 日志文件 | `LoggingService.StartSession` 打开 `session_{SessionName}.log` | `EndBatch` / `EndBatchInternal` 关闭并 Dispose writer | 写入失败不向外抛（第四章 4.7.1 已引用） | 开始新 Session 时会先结束上一个 writer。应用退出若未 `EndBatch`，依赖进程结束回收。依据：`Services/Logging/LoggingService.cs` 第 86–127、146–166 行 |
| Snapshot 定时器 | `MainViewModel` 构造时 `new Timer(..., 500ms)` | 窗口存活期间一直运行 | 无停止或 Dispose | 不是轮次资源，但结束测试后仍在刷新快照。依据：`MainViewModel.cs` 第 902、1123–1127 行 |

### 1.5 界面与流程协调（源码事实）

`IsProcessing` 来自 `VerificationSnapshot.IsProcessing`。`IsScanInputEnabled => !IsProcessing && !IsSelfChecking`，扫码框在 XAML 中绑定该属性，处理中或自检中禁用输入。依据：`MainViewModel.cs` 第 550、636 行；`MainWindow.xaml` 中 `ScanInputTextBox` 的 `IsEnabled` 绑定。

检测进行中（`IsProcessing == true`）的现有门闩：

- 再次扫码：`HandleScanInputAsync` 直接返回，**静默忽略**，无忙碌提示。过程锁与 D08 边界一致。依据：`MainViewModel.cs` 第 1850–1853 行；技术设计 1.2.2；PRD 5.3 / Q19。
- 启动新一轮检验：同上，被过程锁挡住。
- 「开始测试」：`CanExecuteStartBatch` 在 `IsSessionActive || IsSelfChecking || IsProcessing` 时为假。Session 已激活时即使不在检验中也不能再开始。依据：`MainViewModel.cs` 第 1213–1216 行。
- 「结束测试」：`EndBatchCommand` 要求 `IsSessionActive && !IsSelfChecking && !IsProcessing`。依据：第 894 行。
- 切换产品：`SelectedProductCode` 在 `IsSessionActive` 时拒绝修改；组合框 `IsProductCodeComboBoxEnabled => !IsSessionActive`。这是 Session 级锁定，不是单轮检验级。依据：第 118–127、176 行。
- 导出：仅当 `!IsSessionActive` 可执行，整个测试时段都不可导出。依据：第 896 行。
- **自检：`SelfCheckCommand` 的 CanExecute 只有 `!IsSelfChecking`，不以 `IsProcessing` 或 `IsSessionActive` 为门闩。** 因此源码允许在一轮检验进行中启动自检；自检会调用同一个进程级 `IDeviceAccessService.ReadDeviceInfoAsync` 以及 `AdbAccessService.CheckMultipleDevices`。反之，自检进行中会忽略扫码，并禁用开始 / 结束测试和人工检验。依据：第 899、1588–1638、1846–1848 行。

避免上一轮界面残留的现有机制：

- 新 Session 创建新的 `VerificationFlowService` / `ProcessCoordinator`，初始 Snapshot 为 `Idle(sessionId)`，不复用上一 Session 的 Coordinator 状态。依据：`MainViewModel.cs` 第 1308–1312 行；`ProcessCoordinator.cs` 第 127 行。
- 结束测试调用 `ResetVerificationUiToIdle`，并把 `_allowIdleVerificationSnapshotOverwriteOnce` 打开一次，允许定时器把界面校验区刷回 Idle。它不调用 Coordinator.Reset，也不取消任何任务。依据：`MainViewModel.cs` 第 220–226、762–770、1402–1403 行。
- `StatusText` / `UiState` 只有「等待检验 / 正在检验... / PASS / FAIL」，没有等待设备接入或保存未完成等状态。依据：第 692–703、798–808 行；技术设计 1.2.4、1.8 第 10 项。

---

## 2. YME009 可以直接复用的机制

这里的「复用」指继续使用现有实现或现有门闩，而不是改写其业务规则，也不是把 YME009 专属流程塞进这些组件内部扩散。允许的共享改动原则仍以第一章 1.6.4、1.7 为准。

1. **扫码入口与 Session 绑定的 Coordinator。** 继续从 `HandleScanInputAsync` 进入，在 `ProcessScanAsync` 取得 `ProductProfile` 之后按 `ProductCode` 分流。KM001 / KM008 原路径不改。依据：技术设计 1.6.1、2.2.3。
2. **过程锁（`IsProcessing`）与 D08 边界。** 检测处理中忽略新扫码，不引入并行检测。YME009 的 30 秒等待也必须落在这一过程锁内，否则等待期间会误启动第二轮。忙碌提示文案仍待确认，现有静默忽略在确认前可保持为源码行为，但不是新的业务决策。依据：PRD 5.3、D08；技术设计 1.2.2、2.6.3。
3. **Session 级 UI 门闩。** 测试进行中不能改产品、不能再「开始测试」、不能导出、检验中不能「结束测试」。这些可以继续防止「检测中切换批次 / 产品」。无需为 YME009 另做一套 Session 生命周期服务。`EndTime` 未落库的既有缺陷本期不修，YME009 不得拿库中 `EndTime` 判断批次是否结束。依据：PRD 13.6；技术设计 1.8 第 8 项。
4. **`StorageService.SaveTestRecordAsync` 作为独立保存切点。** YME009 直接调用该方法，不经过 `SavePhase3ResultAsync` 的空 catch。方法签名保持不变；内部改为方案 B 属于第四章已定实现约束，本审查不重开。长连接与 `_lockObject` 继续作为共享存储机制。依据：技术设计 4.2、4.3.4。
5. **`LoggingService`。** 按 Session 写入同一日志文件，`LogError` 可带完整异常对象。不新增日志框架。依据：技术设计 4.7.1。
6. **`ProcessRunner` 作为 ADB 进程执行原语。** 超时和异常路径会 Kill 并 Dispose 进程，可供 YME009 定向 ADB 命令使用。但必须注意：它把取消映射为 `Timeout()`；YME009 若引入取消令牌，不能把这种返回值直接当成「设备读取超时 / 读取异常记录」。这是复用时的约束，不是新框架。
7. **现有日志 / 存储的释放习惯。** 会话结束时关闭日志 writer；数据库连接按进程持有。YME009 轮次不应在每轮结束时 Dispose 共享的 `StorageService` 或 `LoggingService`。

明确**不应作为 YME009 一轮检测的复用方案**（已有设计，此处只重申）：

- 不调用现有 `RulePipelineExecutor`、`SavePhase3ResultAsync`、`CheckMultipleDevices`、`ReadDeviceSnAsync` 作为正式扫码检测。
- 不把 `DeviceSessionManager` 的环境级 SessionReady 当作正式设备连接上下文，也不把它当成 Transport ID 锁定。
- 不复制 `ProcessScanAsync` 总 catch，不把未预期异常写成 `EXCEPTION: ...` 记录。
- 不把参考工程 SMT/CIT 状态机、拔板流程或生产装配之外的 AT 客户端当作必选实现。

---

## 3. YME009 必须独立处理的生命周期

这些是现有源码没有对等实现、必须由 YME009 专属路径处理的部分。其中职责切分和第二至第四章已经给出的原则只做引用，不重新设计。

1. **一次扫码的完整轮次，而不是一次规则链调用。** 现有 Phase3 一轮 ≈ 读设备 + 规则 + 保存 + Completed。YME009 一轮由专属轮次协调串联：设备接入生命周期（CDC 发现与唯一性门禁、临时上下文、AT 开启、Ready、身份关联、锁定 Transport ID 并形成正式上下文）以及仅在接入成功之后的业务检测生命周期（定向读取、统一判定、一次 INSERT、保存成功后再关闭 ADB）。结束整个检测过程的是轮次协调，不是 `ProcessCoordinator`，也不是设备接入协调。依据：PRD 第 5 节；技术设计 2.2、2.2.3。
2. **接入失败与正式检测失败的不同结束方式。** 等待超时、门禁未通过、AT 未发送、AT 不确定、Ready 超时、身份关联失败：不创建 TestRecord，不算业务 FAIL，按 2.6.1 结束本轮。业务 FAIL 与读取异常：按第三章构造完整结果并走第四章独立保存；保存成功后再关闭 ADB。未预期软件异常：不创建伪装记录，不显示完成。依据：PRD D11/D13/D14/D09；技术设计 2.6.1、3.9.4、4.4、4.5。
3. **临时设备身份上下文与正式设备连接上下文的创建、交接和作废。** 源码中不存在这两类对象。临时上下文不得交给业务检测；正式上下文不得在身份关联完成前形成；下一轮必须重新接入，不得沿用。依据：技术设计 2.5.1、2.6.2、2.6.4。
4. **CDC 串口与 AT 交换的生命周期。** 包括打开、同一时刻只进行一次 AT、关闭本轮串口，以及 AT 三态（未发送 / 成功 / 不确定）已经对应的清理原则。本审查不选择 AT 客户端实现，不写协议参数。依据：技术设计 2.6.1、2.6.4、2.7.3。
5. **Transport ID 的锁定与失效。** 现有 ADB 命令不定向。YME009 必须在关联成功后锁定，并在本轮结束或设备丢失后作废，不得把未关联的 Transport ID 交给业务检测。具体关联算法仍待真机验证，本审查不定案。依据：技术设计 2.4.3、2.7.2。
6. **30 秒等待观察循环。** 现有 Snapshot 没有等待态，也没有可停止的 CDC 观察。该循环的启动、超时结束、条件恢复后继续接入、以及被取消时（若将来允许）停止观察，都必须由专属接入协调处理。依据：PRD D11/D15；技术设计 2.4.1、2.6.3。
7. **保存成功之后的 ADB 关闭，以及保存未完成时的收尾分叉。** 旧产品没有「保存后再关闭设备 ADB」。关闭链路按第二章新建。保存成功后关闭已确认；保存未完成是否发送关闭 AT 仍为待决策，见第 6 节。无论是否发送关闭 AT，结束时都要停止观察、关闭串口、作废上下文。依据：技术设计 2.6.2、4.6.1、4.6.2。
8. **取消令牌在 YME009 路径上的贯通（作为技术能力，不是已确认允许取消）。** 现有 Phase3 没有轮次级 CTS。若等待期间取消在业务上被确认，现有接口接不住。即便业务上暂不允许取消，窗口关闭或停止观察也需要可中断的等待循环。依据：技术设计 2.6.3。
9. **与自检、默认 ADB 设备路径隔离。** 正式扫码不得接入 `CheckMultipleDevices` 或 `ReadDeviceSnAsync`。YME009 下自检是否仍走现有 ADB 自检仍开放，但专属 CDC/AT/正式上下文不能与现有自检混用同一套「默认设备」假设。依据：技术设计 1.5.2、1.9 第 7 项、2.8 第 6 项。

参考工程存在 `SmtAtSession` 一类 AT 互斥与端口关闭实现，技术设计 2.6.4 已允许把「互斥与关端口、开启前清零 Ready」作为原则参考，同时明确不把 SMT 拔板状态机迁入。本审查维持该边界，不把参考实现列为必选方案。

---

## 4. 第二至第四章已经覆盖的内容

第五章不应重开或改写下列已经确定的内容，只应在轮次生命周期和资源管理上把它们衔接起来。

**第二章已覆盖：**

- 两个生命周期的顺序、唯一成功出口、谁调用谁、谁结束本轮（2.2、2.2.3，图 2）。
- 临时上下文与正式上下文的职责和交接（2.5.1）。
- 接入失败各出口是否创建记录、是否尝试关闭 ADB（2.6.1）。AT 已确认成功但未进入正式检测时，建议用临时上下文尝试关闭；AT 已发送但结果不确定时不自动补发关闭命令。
- 正式检测路径：保存成功后用正式上下文关闭；关闭失败不改已保存结果；下一轮不得沿用上下文（2.6.2）。
- 资源释放基本要求：停止本轮观察、关闭本轮串口、作废两类上下文、AT 同一时刻只一次（2.6.4）。
- 等待循环应能响应取消令牌或停止请求，以便业务一旦确认允许取消不必改门禁结构；取消时的建议清理原则（尚未发送开启 AT 则只停观察，AT 已成功则按 2.6.1 用临时上下文尝试关闭）。这是建议，不是已确认允许取消（2.6.3）。
- 暂不决定：等待期间取消、等待期间再次扫码、保存失败后是否关闭 ADB、提示文案、AT 客户端选型、Ready 数值、CDC 身份字段和关联算法（2.7.3、2.8）。

**第三章已覆盖：**

- 业务检测只使用已锁定且已完成身份关联的 Transport ID（3.1.2、3.4）。
- 读取异常与业务 FAIL 的判定和 FailReason（3.7、3.8）。
- 保存调用方式、保存失败与未预期软件异常不得显示为已保存（3.9.1、3.9.4）。
- 检测中途不新增操作员取消入口（3.11）。
- Q12：不重连；已开始的检测按读取异常保存。除此之外的断开恢复方式仍待确认（3.11；PRD Q12）。
- 保存失败是否关闭 ADB、保存失败和未预期异常的界面文案，继续开放（3.11）。

**第四章已覆盖：**

- 独立保存路径，不使用会吞异常的旧包装方法（4.2）。
- 方案 B：同一事务内 INSERT、读取 `LastInsertRowId`、检查后暂存局部变量，Commit 成功后再回填 `record.Id`（4.3.4）。本审查不重开 A/B/C。
- 保存成功定义：方法正常返回、Commit 成功且 `record.Id > 0`。INSERT 成功或仅取得 `LastInsertRowId` 都不算保存成功。不得仅凭内存中的 Id 判断成功（4.2.3、4.3.5、4.4）。
- 六种状态，以及状态 4 才能在运行时宣布保存成功；导出以数据库实际行为准（4.4、4.8）。
- 异常分类与出口，不复制 Phase3 总 catch（4.5）。
- 保存未完成的三类技术场景（尚未执行 INSERT；INSERT 已执行但结果不能确认；未预期软件异常未形成完整结果），是否关闭 ADB 一律待决策（4.6.2、4.10）。
- 事务失败后尝试 Rollback、再抛出原始异常；Rollback 失败不得吞掉原始异常；Commit 失败仍为「保存结果不能确认」；事务资源必须释放，避免污染共享长连接（4.2.4 图 8 及图后约束）。
- 继续使用现有 `LoggingService`，日志失败不得反向影响保存结果（4.7）。

**第一章中与生命周期相关、且后续章节已承接或明确保留的内容：** 过程锁与扫码入口（1.2.2、1.2.3）；Snapshot 没有等待态（1.8 第 10 项、1.9 第 6 项）；`EndTime` 不在本期修复（1.8 第 8 项）；自检是否沿用仍开放（1.9 第 7 项）。

---

## 5. 第五章真正需要定义的内容

第五章应只补「一轮如何完整地开始、结束和释放资源」，把已有原则落实为可实施的轮次协调约定。不重新设计接入协议、判定规则或保存方案，不引入全局生命周期框架或通用资源管理框架。建议覆盖下列题目；其中涉及仍待确认的业务规则时，只预留技术挂钩，不得在第五章里定案。

1. **轮次起点与过程锁范围。** 明确：扫码通过现有入口进入后，从何时起由 YME009 轮次协调持有本轮；`IsProcessing`（或等价门闩）如何覆盖 30 秒等待、AT、Ready、正式检测、保存和关闭收尾；全部分流出口（含接入失败、早返回）如何释放该门闩，避免残留「正在检验...」。
2. **各结束类型的收尾顺序（只编排，不改已有分类）。** 把已经确定的出口排成轮次结束步骤：接入失败（2.6.1）、业务 PASS / 业务 FAIL / 读取异常且保存成功（保存 → 关闭 ADB → 作废上下文）、状态 5 / 状态 6（不显示完成；是否发关闭 AT 留待决策分支；但必须停止观察、关串口、作废上下文）。不得把关闭结果写回保存结果。
3. **取消令牌的技术设计（不决定是否允许取消）。** 定义 CTS 的创建者、生命周期、传递范围（等待观察、AT、Ready、属性读取、关闭 AT），以及收到取消或停止请求时各阶段的技术动作。等待期间取消的业务开关仍按第 6 节开放。检测中途不新增取消入口（3.11）应被遵守。注意：若复用 `ProcessRunner`，不能把取消误记为读取超时记录。保存阶段现有方法无 token，取消不得破坏方案 B 的事务语义，也不得把未确认提交当成保存成功。
4. **两类上下文与串口、Transport ID 的持有和释放清单。** 谁创建、谁持有、何时作废；拔出、发现失败、Ready 失败、保存结束、窗口关闭时，哪些资源必须释放。关联算法和身份字段仍不在第五章定案。
5. **设备断开时的资源行为。** 正式读取中断开已按 D09 / 3.11 分类为读取异常且不重连。第五章需要说明断开后如何停止观察、关闭本轮串口、作废已锁定的 Transport ID / 上下文，以免污染下一轮。Q12 其余恢复方式仍不在此补充业务规则。
6. **与 UI、Session、自检的协调。** 等待态如何投影（不能只用「正在检验...」）；过程锁下再次扫码在业务确认前的技术行为（继续忽略，提示方式待确认）；检验进行中不得开始新轮次或结束 / 切换批次时，现有门闩是否足够覆盖等待阶段；自检与专属接入若并发会争用 ADB / 未来串口，第五章需定义互斥或隔离方式，但「YME009 自检是否仍走现有 ADB 自检」本身仍开放。
7. **`Reset`、Session 结束、窗口关闭 / 进程退出。** 定义：不得在本轮尚未结束时用 `Reset()` 清掉过程锁；结束测试与进程退出时，若仍有未结束轮次，技术上如何停止观察并释放串口 / CTS / 进程，且不把未保存结果显示为完成。窗口关闭若落在「保存未完成」或「AT 已成功但未正式检测」，关闭 AT 分别沿用既有待决策或 2.6.1 已有建议，不另造规则。
8. **事务与共享长连接。** 只把第四章图 8 的 Rollback / 释放约束接到轮次结束，不修改保存成功定义，不增加连接恢复框架。

第五章不需要再定义：方案 B 细节、六种状态含义、FailReason 清单、导出分桶、AT 原文、Ready 数值、CDC 识别常量、身份关联算法。

---

## 6. 仍需用户决策的问题

下列问题本审查不定案。已在设计中标为待决策或仍开放的，继续保留并注明出处。

1. **等待期间是否允许取消本轮检测。** PRD 5.4；技术设计 2.6.3、2.7.3、1.9 第 6 项、4.10。源码现状：没有取消入口和轮次级令牌。
2. **等待期间再次扫码如何处理。** PRD 5.4；技术设计 2.6.3、2.7.3、4.10。已确认的只是检测处理中过程锁仍在、不要求并行检测。源码现状：静默忽略。
3. **检测忙碌期间再次扫码如何提示操作员（Q19）。** PRD 16 节 Q19、5.3。未决的只是提示方式，不是过程锁本身。
4. **保存没有完成时，是否仍发送关闭 ADB 的 AT。** 已在 2.6.2、2.7.3、3.11、4.6.2、4.10 中保留。须继续区分 4.6.2 的三类技术场景，但三类均不得自行定案为关闭或不关闭。窗口关闭或进程退出若发生在保存未完成阶段，是否发送关闭 AT 属于同一未决项，不另开新规则。
5. **保存失败的界面文案。** 3.11、4.10。已确认的只有不得显示为已保存或已完成。
6. **未预期软件异常的界面文案。** 3.11、4.10。处理边界已在 3.9.4、4.5：记完整日志，不创建伪装记录，不显示 PASS 或已保存。
7. **等待态、多设备等待、两类 FAIL 的界面表达方式。** 1.9 第 6 项、2.7.3、2.8 第 5 项。与过程锁的关系在取消 / 再扫码确认之后再落实，本审查不预设文案。
8. **检测中设备断开时，除读取异常分类以外的恢复方式（Q12）。** PRD 16 节 Q12；3.11 已确认不重连，已开始的检测按读取异常保存。其余恢复方式仍待确认。
9. **YME009 下的自检是否仍走现有 ADB 自检。** 1.9 第 7 项、2.8 第 6 项。源码现状补充：自检按钮目前不以 `IsProcessing` 为门闩，与专属接入并存时存在资源争用风险；如何互斥有待第五章给出技术方案，但自检产品形态本身仍开放。
10. 下列跨章节问题仍然开放，但不属于本轮生命周期审查要推进的决策：Q17、Q20、订单内目标版本一致性、`ProductName` 跨产品复用、MES、`EndTime` 未落库（本期默认不修复）。依据：4.10、2.8、PRD 13.6。

---

## 7. 审查结论与边界确认

- 现有 SnVerify 能够提供一轮检验的扫码入口、Session 绑定、`IsProcessing` 过程锁、大部分 Session 级 UI 门闩、共享存储 / 日志，以及 ADB 进程的超时与释放。这些可以复用。
- 现有实现不足以表达 YME009 的一轮检测：没有取消通路，没有 CDC / AT / Transport ID / 两类上下文，没有保存后关 ADB，没有等待态，Phase3 结束方式（总 catch、空 catch 后仍 Completed）与第四章状态模型不兼容。
- 第二至第四章已经把接入与业务两段生命周期、上下文、保存成功定义、异常出口和多数清理原则说清楚。第五章应定义轮次协调下的开始 / 结束、令牌与资源释放清单、以及和 UI / 自检 / 退出的衔接，而不是重新设计前四章。
- 本轮只新增本报告，未修改 PRD、技术设计、源码或其他文档，未编写第五章正文。
