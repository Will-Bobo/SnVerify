# YME009 设备接入技术审查报告（只读）

**文档性质：** 参考工程 YunkeProductionTester 设备接入实现的只读审查  
**审查日期：** 2026-11-21（Asia/Shanghai）  
**对照需求：** YME009 包装上位机 PRD **V0.4**  
**审查结论用途：** 为后续技术设计提供源码依据，本次不实施代码、不修改既有工程、不提出完整架构方案

---

## 1. 调研范围和源码基线

### 1.1 审查范围

本次只审查参考工程中已经存在的设备接入实现，并对照 PRD V0.4 已确认决策（尤其 D09、D10、D11、D12，以及等待 30 秒、八个 Android 属性、老化两项均为 `1` 才通过、不再比较老化次数、读取异常用 `FailReason` 区分且不计入业务 FAIL）。

覆盖：

1. CDC 设备发现与唯一性门禁
2. AT 与 ADB 生命周期（开启、Ready、关闭、不确定状态、重试）
3. YME009 已确认的八个 Android 属性是否已有读取实现
4. 设备读取异常的实际处理
5. 设备生命周期与资源释放

明确不研究、不搬入 YME009 的内容：CIT/SMT 测试业务本身、批次/扫码/TestRecord/导出实现、SnVerify 通用架构治理、设备接入抽象层设计、任何代码实施。

### 1.2 源码与文档基线

| 项目 | 位置与说明 |
|---|---|
| 参考工程镜像 | 盒子路径：`/workspace/yme009-src/Mifi-YF_YunkeProductionTester`。对应 Windows 路径：`D:\wpf_workspace\Mifi-YF_YunkeProductionTester`。镜像打包时间：2026-11-21 12:18（Asia/Shanghai）。镜像中无 `.git`。审查以源码 `.cs` 为准；不依据 `bin`/`obj` 编译产物下结论。抽查源码时间戳：`CdcAcmCandidateMatcher.cs` 为 2026-11-18 15:42，`SmtStationCoordinator.At.cs` 为 2026-11-17 17:33（均为 Asia/Shanghai）。 |
| 目标工程镜像 | `/workspace/yme009-src/SnVerify`，对应 `D:\wpf_workspace\SnVerify`。仅作对照背景，本次不审查其批次、扫码、TestRecord 或导出实现，也不修改该镜像。 |
| 必须使用的 PRD | **V0.4**。盒子路径：`/workspace/uploads/YME009-Packaging-Station-PRD-V04.md`（52642 字节）。来源：用户机 2026-11-21 16:10 的 `D:\wpf_workspace\SnVerify\docs\YME009-Packaging-Station-PRD.md`。不使用旧版 V0.1 文件 `/workspace/uploads/YME009-Packaging-Station-PRD.md`。 |
| 可参考但不得替代本次追踪 | `/workspace/YME009-feasibility-report.md`、`/workspace/SnVerify-TestRecord-Result-Classification-Review.md`。先前可行性结论若与本次源码追踪不一致，以本次源码为准。 |

下文证据同时给出盒子路径、对应 Windows 路径和行号。Windows 路径的工程根为 `D:\wpf_workspace\Mifi-YF_YunkeProductionTester\Mifi-YF_YunkeProductionTester\`，与盒子路径 `/workspace/yme009-src/Mifi-YF_YunkeProductionTester/Mifi-YF_YunkeProductionTester/` 对应。下文简写为「盒子 `…/相对路径`，Windows `…\相对路径`」。

### 1.3 结论分类约定

- **源码已经证明的事实：** 有完整调用链和关键代码位置。
- **根据代码推导的行为：** 由分支、状态机或默认参数推出，未经真机观察。
- **尚未验证的事项：** 缺源码证据，或源码已标注待设备验证，或必须真机联调才能确认。

能力分类仅使用：

1. 源码已经实现并有调用路径的能力
2. 源码中存在但当前流程未调用的能力
3. 源码中没有找到的能力
4. 需要真机联调才能确认的行为

与 PRD 对照时的复用判断仅使用：可直接复用 / 可参考但需要适配 / 未发现实现 / 需要真机验证。因参考工程与 SnVerify 不是同一代码库，不存在可直接链接的共享程序集；「可直接复用」仅表示该段算法在源码中已经完整，产品常量确认后可以按原逻辑搬迁，不表示可以原工程引用。

### 1.4 本次实际追踪到的主调用链

已完整追踪生产路径：

**确认批次 / 操作员开始等待 → CDC 轮询发现 → 连续两次唯一目标 CDC 确认 → AT 开启 ADB → 15 秒窗口内同一 Transport ID 连续两次 `device` 观察 → ADB Ready → 定向读取 `ro.build.display.id` →（参考工程随后启动 CIT 并轮询 CIT 属性，此段属 SMT/CIT 业务，不搬入 YME009）→ 保存成功后 AT 关闭 ADB → 等待拔板后再接受下一块。**

生产装配入口见 `Presentation/Composition/CompositionRoot.cs` 第 239–251 行：`SmtStationCoordinator` 注入 `CitAdbProtocol`、`AdbDisplayVersionReader`、`SmtAtSession(SetupApiSerialPortDiscovery, WindowsOverlappedAtCommandClient)`。

---

## 2. PRD V0.4 已确认、本次对照使用的决策摘要

以下只摘录与设备接入直接相关的已确认规则，作为对照基线，不改写 PRD。

| ID | 要点 |
|---|---|
| D10 | 接入顺序：CDC 发现 → 目标 CDC 唯一性门禁 → AT 开启 ADB → ADB Ready → 读取 → 按既有流程关闭 ADB。多台目标 CDC 时不自动选择、不发送 AT、不进入读取，等待条件恢复。只有连续两次确认存在唯一目标 CDC，才允许进入 AT。不得把 Mifi 的 `VID_0E8D`、`M6x-66B` 直接认定为 YME009 条件。AT 原文仍待源码核实，不在 PRD 写死。 |
| D11 | 扫码时已有目标设备则直接进入接入和读取；没有设备则等待插入 30 秒，期间接入立即继续。超时结束等待并提示，不判定业务 FAIL，不创建 TestRecord。30 秒只用于等待插入，不用于 AT、ADB Ready 或字段读取。 |
| D09 | 业务 FAIL 与设备读取异常都保存 `Result=FAIL`，用 `FailReason` 稳定代码区分。读取异常必须保留记录，不计入业务 FAIL 和业务通过率分母。 |
| D12 | 同一次既有必要字段读取失败、又有已能判断的业务失败时，主分类为读取异常。 |
| D07 | 分项读取、统一判定；一个字段失败不立即终止其他独立字段；必要判定项失败或缺失不得综合 PASS。IMEI/MAC 不参与综合判定。 |
| 判定项 | SN（`sys.skyroam.osi.sn`）、软件版本（`ro.build.display.id`）、产测（`persist.sys.yl.cit` = 1）、重启老化（`persist.sys.yl.aging.reboot` = 1）、恢复出厂老化（`persist.sys.yl.aging.reset` = 1）。两项老化必须都为 `1`。不再读取或比较老化次数。属性读取失败不得直接等同于值为 `0`。 |
| 第 17 条第 10、13 条 | 不将 SMT/CIT 业务流程引入 YME009；不得将 Mifi 的 VID/父设备名直接当作 YME009 识别条件。 |

---

## 3. CDC 发现及唯一性门禁完整流程

### 3.1 完整调用链（源码已证明）

生产装配：

`CompositionRoot`（盒子 `…/Presentation/Composition/CompositionRoot.cs`，Windows 对应路径，第 248–251 行）创建 `SmtAtSession(new SetupApiSerialPortDiscovery(), new WindowsOverlappedAtCommandClient())`。

等待启动：

1. 操作员确认批次后，`SmtStationCoordinator.HandleResumeBatch`（`…/Application/Station/SmtStationCoordinator.cs` 第 462–488 行）将阶段切到 `WaitingForDevice`，调用 `ArmBoardWatch()`。
2. 通过后进入等待的路径还有：开启失败后的继续（`ResumeBoardWatchAfterOpenFailure`，`SmtStationCoordinator.At.cs` 第 130–136 行）、PASS 后的拔板间隔（`BeginPassRoundGap`，同文件第 547–557 行）、FAIL 后操作员点继续（`HandleContinue`，`SmtStationCoordinator.cs` 第 434–458 行）。
3. `ArmBoardWatch`（`SmtStationCoordinator.At.cs` 第 587–613 行）在 `Idle` 且处于等待设备/拔板/失败待继续时启动后台任务 `WatchBoardAsync`。
4. `WatchBoardAsync`（同文件第 621–680 行）循环调用 `SmtAtSession.ScanAsync` → `ISerialPortDiscovery.DiscoverAsync`。
5. `SetupApiSerialPortDiscovery.DiscoverAsync`（`…/Infrastructure/Prepare/SetupApiSerialPortDiscovery.cs` 第 20–46 行）用 SetupAPI 枚举当前在线的 Ports 类设备（GUID `4d36e978-e325-11ce-bfc1-08002be10318`，`DIGCF_PRESENT`），读取实例 ID、Service、Compatible IDs、父设备实例 ID、父设备 Bus Reported Description、`PortName`，再交给 `CdcAcmCandidateMatcher.Select`。不使用 WMI。枚举失败返回 `SerialDiscoveryKind.Failed`。

### 3.2 目标 CDC 识别条件（源码已证明；这是参考工程的 Mifi 条件，不是 YME009 已确认条件）

`CdcAcmCandidateMatcher.TryMatch`（`…/Application/Prepare/CdcAcmCandidateMatcher.cs` 第 20–22、64–82 行）同时要求：

| 条件 | 源码规则 |
|---|---|
| VID | 实例 ID 含 `VID_0E8D`（忽略大小写） |
| CDC ACM 兼容 ID | Compatible IDs 中同一条同时含 `Class_02`、`SubClass_02`、`Prot_01`（忽略大小写） |
| 父设备产品名 | `ParentProduct` 必须与 `M6x-66B` **区分大小写**完全相等（`StringComparison.Ordinal`） |
| 父实例 ID | 非空 |
| 端口名 | 去空白后形如 `COMn`，`COM` 后全为数字 |

明确不作为接受/拒绝条件的字段：

- **PID：** 源码没有要求特定 PID。测试用 `PID_2006` 与 `PID_2012` 均可匹配（`AtPreparationTests.cs`）。
- **Service：** 注释写明「只随原始字段保留，不参与候选接受或拒绝」（`CdcAcmCandidateMatcher.cs` 第 3 行）。`usbser` 或 `wdm_usb` 都不影响筛选。

PRD 第 17 条第 13 条已确认：不得把上述 `VID_0E8D`、`M6x-66B` 直接认定为 YME009 条件。本次只报告参考工程实际规则。

### 3.3 多设备检测与是否自动选择（源码已证明）

`ClassifyBoard`（`SmtStationCoordinator.At.cs` 第 876–889 行）：

- 发现失败 → `Fault`
- 候选数 0 → `None`
- 候选数 1 → `Unique`
- 候选数 ≥ 2 → `Multiple`

`HandleBoardWatch` 在普通等待阶段（同文件第 714–755 行）：

- `None`：提示「请接入主板。」，继续监视，不发 AT。
- `Unique`：若 `CanStartAtEnable()` 为真，先 `DisarmBoardWatch()`，再 `StartAtEnable()`。
- `Multiple`：提示「设备识别异常。请只接入一块目标主板。」，记录警告「发现多个目标 CDC，不选择其中一块主板。」（第 734–739、51 行）。**不选择第一台，不调用 `StartAtEnable()`，监视继续。**
- `Fault`：提示「目标串口发现失败。」，不发 AT，监视继续。

`SmtAtSession.TryEnableAsync` / `EnableCoreAsync`（`…/Application/Prepare/SmtAtSession.cs` 第 92–98 行）在真正发送前再次扫描：若发现失败或候选数不是 1，直接返回 `AtEnableOutcome.NotSent()`，不打开串口、不写 AT。关闭 ADB 的 `DisableCoreAsync`（第 121–127 行）同样要求唯一候选，否则 `NotSent`。

**结论：** 多台目标 CDC 时，参考工程不自动选择、不发送 AT。这一行为与 D10 一致。

### 3.4 连续两次确认的时间间隔与状态判断（源码已证明；间隔长度需真机验证）

`WatchBoardAsync`（`SmtStationCoordinator.At.cs` 第 633–663 行）在 `acceptEpoch != 1`（即不是 PASS 后强制拔板、也不是 FAIL 后等待拔除的阶段）且第一次分类为 `Unique` 时，**立即再调用一次 `ScanAsync`**，用第二次结果作为本轮上报的 `kind`。只有第二次仍为 `Unique` 才 `break` 并交给 `HandleBoardWatch` 启动 AT。

两次确认之间 **没有 `Task.Delay`**。循环里的 `BoardWatchDelay()`（默认 1 秒，第 666–668、891–894 行）只在本次结果不是「可接受的 Unique」时，用于下一次轮询之前。

`acceptEpoch == 1` 出现在 PASS 后 5 秒间隔和 FAIL 后等待拔除期间（第 547–552、453–456 行）。这些阶段的目的是确认目标 CDC 消失，不做「两次 Unique 后发 AT」。

**根据代码推导：** 「连续两次确认」是两次连续的 SetupAPI 枚举，间隔等于两次 `DiscoverAsync` 的执行时间，不是固定的 1 秒或 15 秒。是否足以滤除短暂枚举抖动，需要真机验证。

发送 AT 前还有第三次唯一性检查（见 3.3）。若此时已变成 0 台或多台，AT 不会发出，结果走「开启失败、请点击继续」（见第 4 节）。

### 3.5 插入、拔出、重新插入与等待流程（源码已证明）

- **等待插入：** 有明确等待流程，但是 **无限等待**，源码中 **没有 30 秒上限**，也没有「等待超时不创建记录」的分支。提示一直是「请接入主板。」
- **已插入再开始等待：** 监视启动后会马上扫描。若已是唯一目标 CDC，两次连续扫描通过后立即进入 AT。这在效果上接近 D11「已接入则直接进入」，但触发点是「确认批次后开始等板」，不是「扫码」。
- **多台恢复为唯一：** 监视不退出，条件恢复且两次 Unique 确认后才会发 AT。
- **PASS 之后：** `BeginPassRoundGap` 进入 5 秒倒计时，且必须观察到目标 CDC 缺席（`_targetCdcAbsentObserved`）后，才 `BeginAcceptingNextBoard`（`boardAcceptEpoch = 2`），重新要求两次 Unique 确认。这是 SMT 工位的拔板节奏，不应搬入 YME009。
- **业务 FAIL 之后：** 操作员点继续后，必须先观察到目标 CDC 缺席，再接受下一块。
- **开启失败后继续：** `ResumeBoardWatchAfterOpenFailure` 复位生命周期并重新等待 CDC，不会在点击后立刻重发 AT（隔离状态除外，见第 4.5 节）。

### 3.6 与 D10 的符合性

**部分符合，需要适配。**

符合 D10 的部分：有 CDC 发现；以目标 CDC 数量做唯一性门禁；多台不自动选择、不发 AT；进入 AT 前有连续两次 Unique 确认；发送前再次校验唯一性。

需要适配的差异：

1. 识别常量是 Mifi 的 `VID_0E8D` + `M6x-66B` + CDC ACM，不能直接用于 YME009。
2. 两次确认之间没有固定间隔。
3. 等待是无限等待，不是 D11 的 30 秒；超时不建 TestRecord 在参考工程中不存在。
4. 触发方式是「批次确认后等插板」，不是「扫码后若无设备再等 30 秒」。
5. PASS/FAIL 后的强制拔板间隔属于 SMT 业务，不应带入包装工位。

---

## 4. AT 与 ADB 生命周期

### 4.1 串口打开与 AT 指令（源码已证明；部分参数源码标明待验证）

生产客户端是 `WindowsOverlappedAtCommandClient`（`CompositionRoot.cs` 第 250 行）。`SerialPortAtCommandClient` 在源码中存在，但生产装配未使用，属于「源码中存在但当前流程未调用的能力」。

一次开启过程：`StartAtEnable` → `SmtAtSession.TryEnableAsync` → `EnableCoreAsync`：

1. 再次发现并要求唯一候选。
2. `IAtCommandSession.OpenAndClearAsync(PortName, settings)` 打开串口（`SmtAtSession.cs` 第 101–106 行）。打开失败返回 `NotSent`，若此时端口释放无法确认则标记 `Quarantined`。
3. 写入开启命令。

已确认的 AT 原文（`…/Application/Prepare/AtCommands.cs` 第 8–14 行）：

- 开启 ADB：`at^optprop setprop persist.sys.skyroam.adb.enable 1`
- 关闭 ADB：`at^optprop setprop persist.sys.skyroam.adb.enable 0`
- 成功响应特征：先出现行 `SIMO AT COMMAND RESULT:`，随后出现行 `OK`（`AtResponseMatcher.IsCompleteSuccess`，`…/Application/Prepare/AtResponseMatcher.cs` 第 5–34 行）。回显的命令行和空行会被跳过。不是成功匹配时，**不会被判定为明确失败**，而是走 `Uncertain`（见 4.3）。

串口参数（`AtLinkSettings.CreateUnverifiedDefaults`，`…/Application/Prepare/AtLinkSettings.cs` 第 3–25 行）：

- 源码注释：`115200`「已在主板上观测到」；其余为「待设备验证的默认值」。
- 默认值：8 数据位、None 校验、One 停止位、无握手、读超时 2 秒、缓冲上限 4096、结束符 `\r\n`。
- `AtCommands.UnverifiedTerminator` 的注释明确：结束符是待设备验证的默认值，不表示主板已经确认。

底层重叠 I/O 时限（`…/Infrastructure/Prepare/IOverlappedSerialChannel.cs` 第 6–16 行）同样注明「数值是试验值，尚未经真机确认」：写等待 2 秒，单次读等待 200 毫秒，取消确认 1 秒。

**这些指令和参数是参考工程针对其 Mifi 主板的实现，不能未经核实写成 YME009 已确认协议。**

### 4.2 超时（源码已证明）

- 开启 ADB 业务预算：`SmtAtSession.EnableBudget = 15 秒`（`SmtAtSession.cs` 第 13、108–116 行）。该预算从写命令时的时间戳起算，同时作为后续 ADB Ready 的截止时间。
- 读响应：开启时使用上述 15 秒业务截止时间；若未传入截止时间，则退回 `settings.ReadTimeout`（默认 2 秒）（`WindowsOverlappedAtCommandClient.ReadResponse`，第 128–130 行）。
- 读超时或始终不能匹配成功响应：返回 `Uncertain`，不是单独的 `Failed` 枚举值。

### 4.3 失败、自动重试、以及「不确定状态下不自动重发 AT」（源码已证明）

`AtExchangeStatus` 只有三种（`…/Application/Ports/IAtCommandClient.cs` 第 6–11 行）：`NotSent`、`Succeeded`、`Uncertain`。**没有明确的 `Failed`。**

一旦写操作已经调用，底层若不能确认完整成功，状态会被提升或改写为 `Uncertain`：

- `SmtAtSession` 在写后若底层仍报 `NotSent`，改为 `Uncertain`（第 88、117、138 行）。
- 写不完整、读超时、解码失败、缓冲区超限、取消确认失败等，均返回 `Uncertain`（`WindowsOverlappedAtCommandClient.cs` 第 86–100、154–172 行）。
- 关闭路径若发生未捕获异常，协调器也记为 `Uncertain`（`SmtStationCoordinator.At.cs` 第 207–209 行）。

协调器处理开启结果（`HandleAtEnableFinished`，第 312–333 行）：

- 端口 `Quarantined`：进入工位异常停止，后续 AT 被拒绝。
- `Status != Succeeded` 或预算未启动（包括 `NotSent` 和 `Uncertain`）：进入 `OpenFailed`，提示「AT 开启失败。请点击继续后重新开启。」**不会自动再发 AT。**

`StartAtEnable` 本身每次只发起一次 `TryEnableAsync`，源码中没有开启阶段的自动重试循环。

操作员点击继续（`HandleBeginRound`，第 91–125 行，界面为 `ShowRetryOpen`）：

- 若未隔离：`ResumeBoardWatchAfterOpenFailure()`，重新等待两次 Unique 确认后才可能再次发送。
- 若已隔离：仍调用 `StartAtEnable()`，但 `SmtAtSession` 会因隔离直接返回 `PortBlocked`，然后再次进入异常停止。这是把隔离状态暴露出来，不是真正自动重发。

**必须明确列出的规则：** 参考工程把「已发送但无法确认成功」统一标为 `Uncertain`，并与 `NotSent` 一样进入 `OpenFailed`；**开启失败后不会自动重发 AT。** 关闭失败同样不自动重试（见 4.7）。另有一条相邻但不属于 AT 的规则：测试结果保存结果不确定时，「不重试保存，也不进入 AT 关闭」（`SmtStationCoordinator.cs` 第 1047 行）。

界面不区分「未发送（例如当时已变成多台）」和「已发送但不确定」，提示都是「AT 开启失败」。YME009 若需要把二者分开统计，要自行适配。

### 4.4 如何开启 ADB，以及 ADB Ready（源码已证明）

开启成功后，`HandleAtEnableFinished` 把 `_readyAfter` 设为 AT 成功时间戳，`_deadline` 设为开启预算截止时间，进入 `ReadyCheck`，提示「正在等待 ADB 就绪」（第 335–340 行）。

Ready 判定在 `TryConsumeReady`（第 417–496 行），消费的是 `SmtDeviceMonitor` 在保留窗口内缓存的 `adb devices -l` 观察：

1. 只接受当前监视世代、序号递增、查询成功、时间戳晚于 `_readyAfter` 且不超过 `_deadline` 的观察。
2. 只统计状态为 `device` 的条目，并按 Transport ID 去重。
3. 若已有 streak 的 Transport ID 仍在本帧列表中，则 `_readyCount++`。
4. 否则若本帧恰好只有 1 个 `device`，则将该 Transport ID 作为新 streak，`_readyCount = 1`。
5. 否则（0 个或多个且不包含原 streak）清零。
6. **`_readyCount >= 2`** 时，调用 `GrantReadyConfirmation()`，再用该设备的 Transport ID 与 Serial 构造 `AdbTarget`，进入 `StartRound`。

监视器本身也有「同一 Transport ID 连续两次 `device` 才给出 `ReadyTarget`」的状态机（`SmtDeviceMonitor.RequiredConsecutiveDeviceObservations = 2`，`…/Application/Monitoring/SmtDeviceMonitor.cs` 第 14、331–357 行）。列表轮询默认间隔 1 秒（第 26 行），单次 `adb devices -l` 超时建议值 15 秒（`AdbCommandDefaults.DeviceListTimeout`，`…/Infrastructure/Adb/AdbCommandDefaults.cs` 第 6 行）。协调器的 Ready 确认以 `TryConsumeReady` 的观察消费为准，不单独再做第三次软件握手。

`HandleMonitorAsync` 在 `ReadyConfirmed` 且监视快照仍为 `DeviceReady` 时才会调用 `StartRound`（`SmtStationCoordinator.cs` 第 420–428 行）。`StartRound` 要求当前生命周期必须是 `ReadyConfirmed`（第 1228–1233 行），上一轮确认不能沿用：每次 `StartAtEnable` 都会把 `_streakTransport`、`_readyCount`、`_lastConsumedSequence` 清零，并 `BeginPendingRetention()` 清空旧观察（`SmtStationCoordinator.At.cs` 第 145–151 行）。

超时：截止时间之前没有凑满两次确认，则进入 `ReadyTimedOut`，提示「ADB 就绪超时。可以重新开启或结束本轮。」（第 381–384 行）。此时仍不自动重发 AT。

**根据代码推导：** 15 秒窗口从 **AT 写入时刻** 起算，不是从 AT 成功或第一帧 ADB 列表起算。若 AT 响应本身已接近 15 秒，留给 Ready 的时间会很短。窗口是否足够，需要真机验证。

另：当第一帧已锁定唯一 Transport ID 后，第二帧即使同时出现其他 `device`，只要原 Transport ID 仍在，计数仍会增加。这与 CDC 阶段「多台一律拒绝」不完全相同，YME009 若要求 Ready 阶段也必须全程唯一，需要适配。

### 4.5 Transport ID 定向执行（源码已证明）

`AdbClient.ExecuteForTargetAsync`（`…/Infrastructure/Adb/AdbClient.cs` 第 51–83 行）在调用方参数前插入 `-t` 和 Transport ID 十进制字符串，并禁止调用方自行携带 `-s`/`-t`/`-d`/`-e`/`--one-device` 等设备选择参数。`AdbTarget` 注释写明：命令只使用 `TransportId`，`Serial` 只进入日志（`…/Application/Adb/AdbTarget.cs` 第 3–6 行）。

若 stderr 包含 `error: no device with transport id`，状态改为 `DeviceNotFound`（第 76–80 行）。定向命令建议超时 5 秒（`AdbCommandDefaults.TargetedCommandTimeout`）。

生产路径中，检验版本读取和 CIT 属性读取都走该执行器。

### 4.6 设备中途断开（源码已证明）

- Ready 阶段：成功列表中目标消失会打断 streak；截止后尚未 Ready 则超时。
- 读取阶段：`SmtDeviceMonitorSnapshot.HasLostTransport`（`…/Application/Monitoring/SmtDeviceMonitorSnapshot.cs` 第 44–46 行）定义为「最近一次列表查询成功，且列表中已没有该 Transport ID」；查询失败不算消失。
- `AdbDisplayVersionReader` 在命令未成功时，若监视器已报丢失 Transport，则分类为 `DeviceDisconnected`（`…/Application/DisplayVersion/AdbDisplayVersionReader.cs` 第 72–74 行）。
- CIT 轮次在命令失败达到阈值后，会再等最多 3 次轮询，以区分「设备已拔出」和「软件/命令失败」（`CitRoundRuntime.FinishCommandFailureAsync`，第 316–343 行）。CIT 业务本身不搬入 YME009，但其「先看监视器再定性」的做法可参考。

### 4.7 关闭 ADB 及关闭失败（源码已证明）

关闭不在读取刚结束时立即发生，而是 **测试结果保存成功之后**：`CompleteSavedRoundAsync` 将生命周期切到 `AtClosing`，再 `StartAtClose()`（`SmtStationCoordinator.cs` 第 1056–1061 行）。

`StartAtClose` → `SmtAtSession.TryDisableAsync` 发送关闭命令（`SmtStationCoordinator.At.cs` 第 179–214 行）。`HandleAtCloseFinished`（第 216–243 行）：

- `Quarantined`：异常停止，停止后续 AT。
- `Succeeded`：`ApplySavedRoundContinuation()`，进入拔板或下一块等待。
- 其他（含 `NotSent`、`Uncertain`）：进入 `CloseFailed`，提示「AT 关闭异常」。**不自动重试。** 操作员可 `RequestRetryAtClose`（手动再关一次）或 `RequestContinueNextRound`（放弃关闭，复位后等待下一块，第 270–290 行）。

若保存不确定，则不进入 AT 关闭，ADB 可能保持开启。这是参考工程的实际行为。

### 4.8 下一轮不沿用上一轮设备状态（源码已证明）

每次准备开启时清零 Ready 计数、streak、截止时间和观察缓存。`GrantReadyConfirmation` 只允许从 `Idle` 或 `ReadyCheck` 进入 `ReadyConfirmed`（第 522–531 行）。`StartRound` 要求当前必须是 `ReadyConfirmed`，并新建本轮 `CancellationTokenSource`、Transport ID、开始时间。PASS 后会清 `_passTransportId`（接受下一块时，第 865–873 行）。AT 会话对象跨轮复用，但每次交换新建串口会话并在结束后关闭端口，用互斥锁保证同一时刻只有一次 AT 交换（`SmtAtSession` 第 8–10、142–176 行）。

---

## 5. 八个 Android 属性逐项核实结果

全库检索了这八个属性名、`getprop`、以及 Property / Shell 命令拼装。生产源码里实际出现的 ADB shell 命令只有两类：

1. `shell getprop ro.build.display.id`
2. `shell getprop | grep -F persist.sys.yl.cit`

另外还有 CIT 启动命令 `shell am startservice -n com.yl.miniscreen/.FactoryAutoService`，这不是属性读取，也不应进入 YME009。

全库（含测试与文档，排除 `bin`/`obj`）没有出现：`persist.sys.wifi.mac`、`sys.skyroam.osi.sn`、`sys.skyroam.imei1`、`sys.skyroam.imei2`、`persist.sys.yl.aging.reboot`、`persist.sys.yl.aging.reset`。不得根据属性名推断实现。

### 5.1 逐项对照表

| 属性 | 是否已有读取实现 | ADB 命令 | 解析规则 | 现有调用位置 | YME009 需要适配的内容 |
|---|---|---|---|---|---|
| `persist.sys.yl.cit` | **已实现，且有生产调用路径** | 定向执行：`adb -t {TransportId} shell "getprop \| grep -F persist.sys.yl.cit"`（`CitAdbProtocol.ReadArguments`，`…/Application/Cit/CitAdbProtocol.cs` 第 27–30、68–79 行） | 解析 `[name]: [value]` 行。目标键共七个，其中包括总结果键 `persist.sys.yl.cit`。空输出视为格式错误；`grep` 无匹配（退出码 1 且 stdout/stderr 皆空）被当成「成功读到空属性集」，不是命令失败。解析本身不做 0/1 判定（`CitPropertyParser`，`…/Application/Cit/CitPropertyParser.cs` 第 11–13、35、37–108 行）。超时 5 秒（`CitRoundRuntime.DefaultCommandTimeout`）。判定在 `CitResultJudge`：总结果为空时 **继续等待**，不提前按分项判 FAIL；总结果或分项为 `0` 才判设备 FAIL；非法值走协议数据异常。 | `CitRoundRuntime.ExecuteAsync` 在 `StartAutoTestAsync` 之后轮询 `ReadSnapshotAsync`（`…/Application/Cit/CitRoundRuntime.cs` 第 136–190、243 行）。这是 SMT/CIT 主路径。 | **不要启动 FactoryAutoService，不要等待产测跑完。** 包装工位只读取既有结果。建议单独读取总结果键，不要复用 CIT 的七键判定和「空则继续等」。成功读到 `1` 才算产测通过；`0` 或产测尚未完成（在读取成功的前提下）是业务 FAIL；命令失败、超时、断开、无法解析是读取异常，不得把读取失败写成值 `0`。 |
| `persist.sys.wifi.mac` | **未发现** | — | — | — | 需要新增定向 `getprop`（或等价读取）、解析、空值/失败分类。该字段不参与综合判定，失败只保留字段状态。 |
| `sys.skyroam.osi.sn` | **未发现** | — | — | — | 需要新增读取。这是必要判定项，用于与扫码 SN 比较。读取失败属读取异常，不得当作 SN 不匹配。比较的大小写/空白语义 PRD Q17 仍开放。 |
| `sys.skyroam.imei1` | **未发现** | — | — | — | 需要新增读取。不参与综合判定。 |
| `sys.skyroam.imei2` | **未发现** | — | — | — | 需要新增读取。不参与综合判定。 |
| `ro.build.display.id` | **已实现，且有生产调用路径** | 定向执行：`adb -t {TransportId} shell getprop ro.build.display.id`（`AdbDisplayVersionReader.Arguments`，`…/Application/DisplayVersion/AdbDisplayVersionReader.cs` 第 9–15、38–49 行） | 命令 `Completed` 且退出码 0 视为读取成功；只去掉末尾连续 `\r`/`\n`，其余字符保留。成功结果允许空字符串。超时默认 5 秒。失败分类：`TimedOut`、`AdbUnavailable`、`TransportNotFound`、`DeviceDisconnected`、`CommandFailed`（第 62–84 行）。比较在 `SmtDisplayVersionComparison`：区分大小写的完全一致为 PASS；去掉末尾换行后为空或不一致为 FAIL（`…/Domain/DisplayVersion/SmtDisplayVersionComparison.cs` 第 10–32 行）。 | `SmtStationCoordinator.ExecuteRoundAsync` 在启动 CIT **之前** 调用 `_displayVersions.ReadAsync`（`SmtStationCoordinator.cs` 第 1302–1326 行）。 | 读取命令和定向执行、超时与失败分类可参考。YME009 必须改掉「读取失败则不保存记录、直接 RoundCrashed」的行为（见第 6 节）。空字符串在参考工程算「读成功但比较 FAIL」；YME009 需自行决定空值是业务 FAIL 还是读取异常。比较是否忽略大小写仍待 Q17/业务确认，不能默认沿用 SMT 的 Ordinal 比较。 |
| `persist.sys.yl.aging.reboot` | **未发现** | — | — | — | 需要新增读取。与 reset 是独立结果。成功读到 `1` 才通过，成功读到 `0` 为业务 FAIL；读取失败不是 `0`。 |
| `persist.sys.yl.aging.reset` | **未发现** | — | — | — | 同上。两项都必须为 `1` 才通过。参考工程没有「老化次数」读取，这一点与 V0.4「不再比较老化次数」一致，但不能因此认为两项老化结果已经实现。 |

### 5.2 参考工程读取了、但不在 YME009 八项之内的属性或命令

以下 **不是** YME009 已确认项，不得误当作包装工位必读字段：

| 名称 | 用途 | 位置 |
|---|---|---|
| `persist.sys.yl.cit.version` | CIT 分项「耦合校准」 | `CitPropertyParser` / `CitResultJudge` |
| `persist.sys.yl.cit.wifi` | CIT 分项 WiFi | 同上 |
| `persist.sys.yl.cit.bt` | CIT 分项 BT | 同上 |
| `persist.sys.yl.cit.sim` | CIT 分项 SIM | 同上 |
| `persist.sys.yl.cit.charge` | CIT 分项 Charge | 同上 |
| `persist.sys.yl.cit.key` | CIT 人工按键项 | 同上 |
| `persist.sys.yl.cit.gps`（若出现） | 解析时跳过，不是目标属性 | `CitPropertyParser` 第 13 行注释 |
| `persist.sys.skyroam.adb.enable` | 通过 AT `setprop` 开关 ADB，不是 `getprop` 读取 | `AtCommands.cs` |

`CitAdbProtocol` 还会启动 `com.yl.miniscreen/.FactoryAutoService`。这是 CIT 测试启动，禁止迁入 YME009。

---

## 6. 设备读取异常处理

本节省先报告参考工程 **实际行为**，不要求它符合 D09、D12。然后再说明 YME009 需要适配的规则。

### 6.1 参考工程实际如何分类

参考工程 **没有** 与 D09 对应的「业务 FAIL / 读取异常」二分统计。它把问题分散到：CIT 业务 FAIL（保存记录）、工位异常停止（多数 **不保存记录**）、AT 开启/Ready/关闭失败（通常 **尚未建本轮记录**）等。

| 情况 | 参考工程实际行为 | 证据 |
|---|---|---|
| ADB 不可用（检验版本） | `SmtDisplayVersionFailure.AdbUnavailable`。`ExecuteRoundAsync` 将其视为读取异常：**不启动 CIT，不保存记录**，发 `RoundCrashed`，协调器 `EnterAbnormal`。 | `AdbDisplayVersionReader.cs` 第 79–80 行；`SmtStationCoordinator.cs` 第 1316–1323、314–325 行 |
| ADB 命令失败 / 超时 / Transport 不存在（检验版本） | 同样走 `RoundCrashed`，不保存记录。 | 同上，第 77–83、1316–1323 行 |
| 必要属性读取失败（CIT 轮询） | 单次失败会重试，连续 3 次失败后再等最多 3 次监视轮询：若已断开则 CIT FAIL（`DeviceDisconnected`，保存记录）；否则 `SoftwareFault`，`EnterAbnormal`，**不走保存成功路径**。 | `CitRoundRuntime.cs` 第 17–20、206–216、319–336 行；`HandleRoundCompletedAsync` 第 987–996 行 |
| 属性返回空字符串（检验版本） | 视为 **读取成功**。随后比较时，去掉末尾换行后为空则版本判定 FAIL。若版本 FAIL 且 CIT 也有结果，会进入综合判定并保存。 | `SmtDisplayVersionReading.cs` 第 13–14 行；`SmtDisplayVersionComparison.cs` 第 16–27 行 |
| 属性返回空（CIT 总结果） | 读取算成功，判定为 `Continue` 或等待按键，**继续轮询，不结束、不记 FAIL**。这与 YME009「产测未完成不能视为 PASS、也不应等待产测跑完」直接冲突。 | `CitResultJudge.cs` 第 4–5、40–47 行；`CitAdbProtocol.IsGrepNoMatch` 第 91–94、226–230 行 |
| 属性返回非 0/1 的非法值（CIT） | `ProtocolData` → `ProtocolDataFault` → `EnterAbnormal`，不保存记录。 | `CitResultJudge.cs` 第 32–38 行；`SmtStationCoordinator.cs` 第 987–991 行 |
| 读取过程中断开（检验版本） | 分类 `DeviceDisconnected`，仍走 `RoundCrashed`，不保存记录。 | `AdbDisplayVersionReader.cs` 第 72–74 行 |
| 读取过程中断开（CIT 已开始） | 判 `CitFailReason.DeviceDisconnected`，保存 FAIL 记录，然后关闭 ADB，再回到等待设备。 | `CitRoundRuntime` `FinishDisconnected`；`ApplySavedRoundContinuation` 第 1107–1118 行 |
| 多字段部分成功、部分失败 | 参考工程不是 YME009 那种「一次读八项再统一判定」。它先读版本，失败则整轮崩溃；版本成功后才轮询整组 CIT 属性。CIT 解析格式错误会整次快照作废。 | `ExecuteRoundAsync` 第 1302–1327 行；`CitPropertyParser` 第 58–61 行 |
| 未捕获异常 | CIT 轮次捕获后 `RoundCrashed` 或 `SoftwareFault`，进入异常停止；AT 开启未捕获异常被改写为 `NotSent`；AT 关闭未捕获异常被改写为 `Uncertain`。监视循环未捕获异常会停止监视并让工位异常停止。 | `ExecuteRoundAsync` 第 1346–1353 行；`SmtStationCoordinator.At.cs` 第 170–172、207–209 行；`SmtDeviceMonitor.RunLoopAsync` 第 204–207、248–250 行 |
| 设备接入失败（无 CDC、多 CDC、发现失败、AT 未成功、Ready 超时） | 停在等待/开启失败/就绪超时，**不创建本轮测试记录**。这与 D11「等待插入超时不建记录」部分相似，但参考工程把 AT/Ready 失败也同样不建记录，这与 D09「读取阶段异常必须保存 TestRecord」不一致。D11 已说明 30 秒不用于 AT、ADB Ready 或字段读取。 | `HandleAtEnableFinished`、`ReadyTimedOut`、`HandleBoardWatch` |

参考工程对「设备接入失败」「设备读取异常」「正常业务判定 FAIL」的实际分界大致是：

- 接入失败：还没进入 `StartRound`（无唯一 CDC、AT 未成功、Ready 超时、端口隔离）。通常无记录。
- 读取/协议/软件异常：进入测试后，版本读失败、CIT 协议/软件故障、未捕获异常。多数 `EnterAbnormal`，无记录。
- 业务 FAIL：CIT 判定失败或版本比较失败，且走完 `CompleteSavedRoundAsync`。有记录，然后关 ADB。

### 6.2 YME009 后续需要如何适配 D09、D12

PRD 已确认，不能照搬参考工程的「很多异常直接停工位、不写记录」：

1. 一旦已经进入 AT / ADB Ready / 字段读取阶段，失败都要保存 TestRecord。
2. `Result=FAIL`。
3. 用 `FailReason` 中稳定的读取异常代码区分，不新增表或字段。
4. 读取异常不计入业务 FAIL 数量，也不计入业务通过率分母。
5. 同一次既有必要字段读取失败、又有已能判断的业务失败时，主分类为读取异常（D12）。
6. 等待插入 30 秒超时：不判定业务 FAIL，不创建 TestRecord，也不归入读取异常（D11）。这与参考工程「无限等待、无超时记录」不同，要在包装流程里单独实现。
7. 分项读取：一个必要字段失败，不应立即放弃其余独立字段（D07）。参考工程「版本失败即整轮崩溃、不读 CIT」不符合该规则。
8. 不要把属性读取失败写成值为 `0`，从而误判为业务 FAIL。
9. IMEI/MAC 失败不否决综合 PASS。

---

## 7. 设备生命周期和资源释放

### 7.1 CDC 监视的启动和停止（源码已证明）

- 启动：确认批次后 `ArmBoardWatch()`；开启失败恢复、PASS 间隔、FAIL 后继续时也会重新武装。
- 停止：`DisarmBoardWatch()` 通过增加 `_boardWatchGeneration` 让后台循环退出（`SmtStationCoordinator.At.cs` 第 615–619 行）。Unique 确认后、条件不再匹配时都会解除。
- CDC 监视是按轮武装的后台扫描，不是操作系统级的永久设备通知。ADB 监视器是另一路：`AttachNewMonitor` 在确认批次时创建 `SmtDeviceMonitor` 并 `RunAsync`（`SmtStationCoordinator.cs` 第 1448–1475 行）。
- 停止监视：`StopMonitorAsync` 先退订事件，再 `monitor.StopAsync()`，然后 `StopPrepareLoopAsync()`（第 1496–1519 行）。旧监视尚未确认停止时拒绝创建新监视，避免两个监视器并存（第 1448–1453 行）。停止失败则工位必须保持异常停止。

`PrepareLoopAsync` 注释写明：「P1 不按旧规则自动发送 AT。循环只为停止时能等待仍在进行的交换。」（`SmtStationCoordinator.At.cs` 第 974–995 行）。它在 `ReadyCheck` 时发 `RoundTick`，不发送 AT。

### 7.2 AT 串口资源释放（源码已证明）

每次交换 `await using` 会话。`WindowsOverlappedAtCommandClient.Session` 在写读结束后 `TryClosePort()`；`DisposeAsync` 若未隔离且仍打开也会再关一次（第 109–116、175–183 行）。关闭无法确认时标记 `Quarantined`，之后 `SmtAtSession` 拒绝新的 AT，并打日志「串口资源无法确认已释放，已停止后续 AT 操作。」协调器相应地 `EnterAbnormal`。

`StopPrepareLoopAsync` 在取消准备循环后，若 `_atSession.SendInFlight` 仍为真，会按 20 毫秒间隔等待交换结束（第 965–971 行）。

### 7.3 ADB 开启与关闭的配对关系（源码已证明）

设计上是「本轮 AT 开启 → 读取/测试 → 保存成功 → AT 关闭」。但配对并不总是完成：

- 保存不确定：不关闭 ADB。
- 版本读取失败或未捕获异常导致 `RoundCrashed`：不关闭 ADB。
- CIT 协议/软件故障进入异常停止：不关闭 ADB。
- 关闭失败：停在 `CloseFailed`，等操作员重试或跳过。

**根据代码推导：** 异常退出后，设备端 ADB 可能仍保持开启。下一轮若仍能两次 Unique 确认，会再次发送开启命令；若 ADB 已经可用，Ready 仍可能在 15 秒内成功。这需要真机确认，不能当成已经设计好的恢复策略。

### 7.4 拔出、结束、连续两台是否串状态（源码已证明）

- 拔出：CDC 监视把数量变为 `None` 或 `Multiple`/`Fault` 并更新提示；ADB 监视把 `ReadyTarget` 清掉。PASS 后必须先观察到 CDC 缺席才接受下一块。
- 检测结束后：`ResetRoundLifecycle()` 解除板监视、清间隔标志、将 `boardAcceptEpoch` 置 0（第 534–545 行）。
- 连续两台：Ready 计数与观察缓存会在新的 `StartAtEnable` 清零；新一轮 `StartRound` 使用新的 Transport ID 和取消源。AT 端口不跨交换保留句柄。
- **根据代码推导：** 正常 PASS/FAIL 后再插下一台，源码意图是不沿用上一轮 Ready 确认。若异常停止后未关 ADB、也未复位操作员流程，则存在残留状态风险，需要在 YME009 设计里单独处理，本次不给架构方案。

### 7.5 异常退出时的释放（源码已证明）

`EnterAbnormal` 会 `InvalidateRound()`：复位生命周期（从而解除 CDC 监视）并取消本轮 CTS（第 1361–1401 行）。它 **不会** 自动发送 AT 关闭，也 **不会** 在此处停止 ADB 监视器。监视器停止发生在更外层的工位停止/替换路径。若取消时重叠 I/O 无法确认完成，端口会被隔离且不再使用，而不是强行泄漏后再打开第二次；隔离本身会使后续 AT 不可用，直到进程重启（源码未见解除隔离的接口）。

---

## 8. 与 PRD V0.4 的逐项对照表

| YME009 需求 | 参考工程现有能力 | 复用判断 | 需要适配或补充 |
|---|---|---|---|
| CDC 发现 | 用 SetupAPI 枚举当前在线 Ports 设备，再按 CDC ACM + VID + 父产品名 + COM 口筛选。生产路径已调用。 | 可参考但需要适配；识别条件需要真机验证（针对 YME009 设备，而不是照搬 Mifi 常量） | 必须重新确认 YME009 的 VID/PID、兼容 ID、父设备名或其他识别条件。不得直接使用 `VID_0E8D`、`M6x-66B`。枚举方式（SetupAPI、Ports 类、读父设备 Bus Reported Description）可以参考。 |
| CDC 唯一性门禁 | 按目标 CDC 候选数量分类；≥2 台提示且不选第一台、不发 AT；发送前再检查必须恰好 1 台。 | 可参考但需要适配 | 门禁逻辑与 D10 一致，可按该决策搬迁。提示文案、与扫码流程的衔接需要按包装工位改写。 |
| 连续两次确认唯一目标 CDC 才允许 AT | `WatchBoardAsync` 在普通接受阶段对 Unique 连续扫描两次，第二次仍为 Unique 才启动 AT。 | 可参考但需要适配；两次扫描的间隔需要真机验证 | 源码两次扫描之间没有固定等待。若 YME009 需要可配置间隔，要补充，不要误认为已有 1 秒间隔。 |
| AT 开启 ADB | 生产路径发送 `at^optprop setprop persist.sys.skyroam.adb.enable 1`，按 `SIMO AT COMMAND RESULT:` + `OK` 判定成功；115200 已在参考主板上观测。 | 可参考但需要适配；命令原文、结束符和串口参数需要真机验证 | 不得把该 AT 原文直接写成 YME009 已确认协议。打开/清空/重叠 I/O 实现可参考。 |
| AT 失败及不确定状态处理 | 无 `Failed` 枚举；已发送但无法确认则 `Uncertain`；开启失败进入 `OpenFailed`，**不自动重发**；隔离后拒绝后续 AT。 | 可参考但需要适配 | YME009 需要把 AT 失败记为读取异常并保存 TestRecord（若已离开「等待插入」阶段）。建议区分 `NotSent` 与 `Uncertain`。自动重发策略应保持「不确定则不自动重发」，与参考工程一致。 |
| ADB Ready | AT 成功后 15 秒窗口内，同一 Transport ID 连续两次 `device` 观察才 Ready；超时需操作员重开或结束。监视轮询默认 1 秒。 | 可参考但需要适配；15 秒窗口和轮询间隔需要真机验证 | 预算从 AT 写入时刻起算。YME009 是否采用相同窗口、Ready 阶段是否也要求 ADB 列表唯一，需要设计时明确。超时已进入 Ready，应按 D09 保存读取异常记录，而不是只提示重开。 |
| Transport ID 定向读取 | `adb -t {id} …`，禁止调用方另选设备；找不到 Transport 有专门状态。检验版本和 CIT 读取都走这条路径。 | 可参考但需要适配 | 算法完整，搬到 SnVerify 时需要自行接入其进程执行器，不存在可直接引用的程序集。 |
| 八个 Android 属性 | 仅 `ro.build.display.id`、`persist.sys.yl.cit`（作为 CIT 七键之一）有读取实现和调用路径。其余六项未发现。参考工程另读 CIT 分项并会启动 FactoryAutoService。 | 两项为可参考但需要适配；六项为未发现实现；返回格式与空值语义需要真机验证 | 见第 5 节表格。禁止把 CIT 启动和「空结果就继续等」带进包装工位。禁止把读取失败写成 `0`。不要读取或比较老化次数。 |
| 设备读取异常 | 有较细的命令失败分类（超时、ADB 不可用、Transport 丢失、断开），但很多路径不保存记录，而是 `EnterAbnormal`。CIT 空值会继续等待。 | 可参考但需要适配 | 必须按 D09/D12 保存 TestRecord、`Result=FAIL`、用 `FailReason` 区分、不计入业务 FAIL。同次兼有读取失败和业务失败时主分类为读取异常。等待插入超时按 D11 不建记录。 |
| ADB 关闭 | 保存成功后发送 `at^optprop setprop persist.sys.skyroam.adb.enable 0`；失败不自动重试，可手动重试或跳过。关闭时同样要求唯一 CDC，否则 `NotSent`。 | 可参考但需要适配；关闭命令同样需要真机验证 | YME009 需要明确：读取异常保存之后是否仍关闭 ADB；关闭失败如何记录；跳过关闭时下一轮如何处理。异常路径目前经常不关 ADB，不宜原样照搬。 |
| 设备断开和资源释放 | 以成功的 `adb devices -l` 中 Transport 消失判定断开；串口按次打开关闭，无法确认则隔离；监视停止有显式路径；Ready 状态按轮清零。 | 可参考但需要适配 | 包装工位没有「必须先拔板再测下一台」的 SMT 间隔需求。异常退出后 ADB 可能仍开启，需要在设计中定义复位，而不是默认沿用 SMT 的人工继续/拔板流程。D11 的 30 秒等待要新做。 |

---

## 9. 分类汇总

### 9.1 可直接复用

本次 **不把整段设备接入标为可直接复用**。原因：识别条件、AT 原文、等待触发方式和记录策略都必须按 YME009 / PRD 适配；且两套工程不能直接共享程序集。

仅以下 **算法片段** 在源码中已经完整，产品常量确认后可以按原逻辑搬迁（仍要在 SnVerify 侧实现，不算引用现成库）：

- 目标数量门禁：0 / 1 / ≥2 的分类，以及 ≥2 时不选第一台、不发 AT。
- AT 交换三种状态：`NotSent` / `Succeeded` / `Uncertain`，以及「不确定则不自动重发」。
- 定向 ADB：`-t {TransportId}` 前缀，以及「调用方不得自行带设备选择参数」。
- 断开判定：只有成功的设备列表中找不到该 Transport ID 才算丢失；查询失败不算消失。

### 9.2 可参考但需要适配

- SetupAPI CDC 枚举与候选对象结构
- 连续两次 Unique 确认后再发 AT（需决定间隔）
- AT 串口打开、写读、超时、隔离、关闭实现
- ADB 列表监视、15 秒 Ready 窗口、连续两次同一 Transport ID
- `ro.build.display.id` 的定向 `getprop`、去末尾换行、失败分类
- `persist.sys.yl.cit` 的读取与 `[key]: [value]` 解析（只复用总结果键的读取/解析，不复用 CIT 业务判定和启服务）
- 失败分类思路（超时、不可用、Transport 不存在、断开），但保存与统计必须改
- 串口按次释放与隔离
- 每轮清零 Ready 状态，避免沿用上一轮确认

### 9.3 尚未发现（源码中没有找到的能力）

- `persist.sys.wifi.mac` 读取
- `sys.skyroam.osi.sn` 读取
- `sys.skyroam.imei1` / `sys.skyroam.imei2` 读取
- `persist.sys.yl.aging.reboot` / `persist.sys.yl.aging.reset` 读取
- 任何「老化次数」读取（与 V0.4 不再比较次数一致，但两项老化结果仍缺失）
- 等待插入 30 秒、超时提示且不创建 TestRecord 的流程
- 按 D09/D12 区分业务 FAIL 与读取异常、读取异常仍保存 TestRecord 且不计入业务 FAIL 的统计
- 分项读完全部八项后再统一判定（参考工程是先读版本，失败即中止）
- YME009 设备的 CDC 识别条件

### 9.4 源码中存在但当前生产流程未调用

- `SerialPortAtCommandClient`（`…/Infrastructure/Prepare/SerialPortAtCommandClient.cs`）：另有一套基于 `System.IO.Ports` 的 AT 客户端，生产 `CompositionRoot` 使用的是 `WindowsOverlappedAtCommandClient`。

### 9.5 需要真机验证

- YME009 实机的 CDC 识别条件（VID/PID、兼容 ID、父设备名、接口、驱动服务名等）
- YME009 是否接受与参考工程相同的 AT 原文、结束符、波特率及其他串口参数
- 连续两次 CDC 扫描不加间隔，是否足以抑制枚举抖动
- AT 开启后 15 秒内，目标设备能否稳定以同一 Transport ID 连续两次出现在 `adb devices -l` 中
- `getprop` 对八个属性的实际返回格式、空值、未定义键、非 0/1 取值
- 关闭 ADB 失败或异常路径未关闭时，下一轮重新插入/再次开启的真实表现
- 读超时、断开、多 CDC 热插拔在实机上的时序

---

## 10. 尚未获得源码证据的问题清单

1. YME009 目标设备的 CDC 识别条件是什么？（PRD 已禁止沿用 `VID_0E8D` / `M6x-66B`，源码也无法代答。）
2. YME009 是否使用与参考工程相同的 AT 开启/关闭 ADB 命令和响应格式？结束符是否为 CRLF？
3. 八个属性中未发现实现的六项，设备侧的读取命令、返回格式、空值和非法值分别是什么？
4. `persist.sys.yl.cit` 在包装时点是否已经写好最终值？是否可能仍为空？空值在业务上是「尚未完成」（业务 FAIL）还是读取异常？除 `0`/`1` 外的取值 PRD 也未确认。
5. 两项老化属性除 `0`/`1` 外的取值含义（PRD 已声明不写成通过或失败）。
6. 连续两次 CDC 确认是否需要固定间隔？参考工程没有，YME009 尚未另作产品决定。
7. ADB Ready 的 15 秒窗口、监视 1 秒间隔、属性读取 5 秒超时，对 YME009 是否足够？
8. 扫码触发与参考工程「确认批次后自动等板」如何衔接；等待期间取消、等待期间再次扫码，PRD 仍待确认。
9. 读取异常保存记录之后，是否仍必须尝试关闭 ADB？关闭失败如何记录？
10. 软件版本比较是否区分大小写、是否修剪首尾空白（PRD Q17 仍开放；参考工程是 Ordinal 且只去末尾换行）。
11. 现场是否存在参考工程源码以外的手工配置或驱动约束（本次镜像无现场机器、无 `.git`、未做真机联调）。

---

## 11. 审查合规说明

- 本次只读审查，未修改参考工程、目标工程、测试、配置或任何已有文档，包括 PRD。
- 未实施代码，未做架构重构，未设计接口、抽象类、继承关系或完整架构方案。
- 未将 SMT/CIT 业务流程作为 YME009 方案提出；报告中出现 CIT 启动、分项和等待逻辑，仅用于说明参考工程实际行为及必须排除的部分。
- 未修改 SnVerify 现有通用流程，也未因发现问题修复参考工程。
- 本报告是盒子上唯一新增文件：`/workspace/YME009-Device-Integration-Technical-Audit.md`。父代理可将其原样拷贝到 `D:\wpf_workspace\SnVerify\docs\YME009-Device-Integration-Technical-Audit.md`。
