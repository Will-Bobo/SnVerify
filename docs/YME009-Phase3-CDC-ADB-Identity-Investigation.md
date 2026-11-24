# YME009 Phase 3：CDC 接口切换与 ADB 身份关联调查

调查日期：2026-11-23。本文只记录调查结论，不修改业务代码、测试、PRD 或 Technical Design V1，也不改变 Phase 3 验收标准。

## 1. 调查范围和依据

调查两个问题：

1. AT Enable 前后 CDC 接口发生变化的原因及其对关闭 ADB 的影响。
2. CDC 设备与 ADB Transport 之间能否建立不依赖“当前只有一台设备”的一对一关联。

依据分为三类：

| 类别 | 材料 |
|---|---|
| 当前实现 | `SnVerify` 工作区中的 YME009 Phase 3 源码 |
| 已确认设计 | `docs/YME009-Technical-Design.md` V1，重点是 §2.4.3、§2.5.1、§2.6.2、§2.7.2、§2.7.3 |
| 参考工程事实 | `docs/YME009-Device-Integration-Technical-Audit.md`，以及该报告已经指向的 YunkeProductionTester 源码。这些事实只说明参考工程做了什么，不是 YME009 已确认协议 |
| 真机日志 | 本次调查任务给出的最近一次真机结果。工作区中没有保存这次测试的原始日志文件，本文不补写未提供的字段 |

本次真机结果，按任务原文采用：

- Enable 前连续三次为同一 CDC 接口：VID `0E8D`、PID `2012`、MI `01`、COM `COM38`，门禁通过。
- Enable 命令得到分行的 `SIMO AT COMMAND RESULT:` 与 `OK`，结果 `Succeeded`，耗时约 65ms。
- 第一次 `adb devices -l` 设备数为 0；第二次出现 `MTK0002610190813190 device product:M6x-66B model:M6x_66B device:M6x-66B transport_id:2`。Ready 成功，耗时约 1403ms。
- `Yme009UnprovenIdentityAssociator` 返回未证明，没有交付 Transport ID，没有进入业务检测。
- Disable 重新发现的接口为 VID `0E8D`、PID `2006`、MI `02`，实例身份与 Enable 前不同。程序因身份不匹配跳过关闭：未打开串口，未发送 Disable，结果 `NotSent`。资源清理完成。

## 2. 当前代码执行链路

一次扫码进入 `Yme009RoundCoordinator.RunAsync`。设备接入由 `Yme009DeviceAccessCoordinator.AccessAsync` 顺序执行：

1. `ObserveUntilConfirmedAsync` 调用 `SetupApiYme009CdcDiscovery.DiscoverAsync`。
2. `Yme009CdcAcmFilter.TryMatch` 筛选候选。`Yme009CdcUniquenessGate.Apply` 做连续确认和发送前复核。
3. 门禁通过后，`Yme009DeviceConnection.Temporary` 保存这次确认到的 `IdentityKey`。这里不保存 COM。
4. `Yme009AtAdbSwitch.EnableAsync` 再次发现 CDC。`TrySelectCurrentPort` 要求当前恰好一个候选，且 `IdentityKey` 与门禁身份按序号完全相等，然后打开当次 COM 并写入 Enable。
5. `WaitUntilCommunicableAsync` 通过 `Yme009ProcessAdbDeviceQuery.QueryAsync` 执行 `adb devices -l`。`Yme009AdbDeviceListParser.Parse` 只保留序列号列、状态和 `transport_id`。
6. 出现状态为 `device` 的设备后，调用 `Yme009UnprovenIdentityAssociator.AssociateAsync`。生产实现固定返回 `Unproven`。`TryPromote` 失败，停止原因为 `AssociationFailed`。
7. 轮次协调仍按关闭策略调用 `TryCloseAsync`。关联未成功时上下文阶段仍是 `Temporary`，关闭使用门禁时保存的接口实例 ID。
8. `DisableAsync` 再发现一次 CDC。身份字符串不一致时，`TrySelectCurrentPort` 返回失败，`ExchangeAsync` 返回 `NotSent`，关闭处置为 `Skipped`。
9. `Release` 作废上下文。关闭被跳过不阻止清理。

对应方法：

| 步骤 | 位置 |
|---|---|
| 轮次 | `SnVerify/Services/Yme009/Yme009RoundCoordinator.cs`，`Yme009RoundCoordinator.RunAsync` |
| 接入 | `SnVerify/Services/Yme009/Yme009DeviceAccessCoordinator.cs`，`AccessAsync`、`TryCloseAsync`、`Release` |
| CDC 枚举 | `SnVerify/Infrastructure/Yme009/SetupApiYme009CdcDiscovery.cs`，`DiscoverAsync`、`ReadInstanceId` |
| CDC 筛选 | `SnVerify/Domain/Product/Yme009CdcRecognition.cs`，`Yme009CdcAcmFilter.TryMatch` |
| 门禁 | `SnVerify/Domain/Product/Yme009CdcUniquenessGate.cs`，`Apply` |
| AT | `SnVerify/Services/Yme009/Yme009AtAdbSwitch.cs`，`EnableAsync`、`DisableAsync`、`TrySelectCurrentPort` |
| ADB 列表 | `SnVerify/Infrastructure/Yme009/Yme009ProcessAdbDeviceQuery.cs`，`QueryAsync` |
| 解析 | `SnVerify/Domain/Product/Yme009AdbObservation.cs`，`Yme009AdbDeviceListParser.Parse` |
| 关联 | `SnVerify/Services/Yme009/Yme009DevicePorts.cs`，`Yme009UnprovenIdentityAssociator.AssociateAsync` |
| 上下文 | `SnVerify/Domain/Product/Yme009DeviceConnection.cs`，`Temporary`、`TryPromote`、`BuildDirectedArguments` |
| 关闭条件 | `SnVerify/Domain/Product/Yme009AdbClosePolicy.cs`，`ShouldAttemptClose` |
| 生产装配 | `SnVerify/Infrastructure/ServiceFactory.cs` 注入 `Yme009UnprovenIdentityAssociator` |

## 3. CDC 接口变化分析

### 3.1 当前如何识别两个接口

`SetupApiYme009CdcDiscovery.DiscoverAsync` 用 Ports 类 GUID `4d36e978-e325-11ce-bfc1-08002be10318` 和 `DIGCF_PRESENT` 枚举当前在线设备。每个设备只读取三样数据：

- `SetupDiGetDeviceInstanceIdW` 的整段设备实例 ID；
- 注册表属性 Compatible IDs；
- 设备注册表项 `PortName`。

`Yme009CdcAcmFilter.TryMatch` 接受候选的条件是：Compatible IDs 中同一条同时含 `Class_02`、`SubClass_02`、`Prot_01`；实例 ID 非空；`PortName` 形如 `COMn`。通过后，`Yme009CdcCandidate.IdentityKey` 就是 `instanceId.Trim()`，`PortName` 是 COM。

源码没有单独解析 VID、PID 或 MI，也没有 VID 或 PID 白名单。`PID_2012` / `MI_01` 与 `PID_2006` / `MI_02` 只是出现在 Windows 实例 ID 字符串中的片段。只要两条实例 ID 都满足 CDC ACM 和 COM 条件，筛选器会把它们都当成合法 CDC 候选。比较“是不是同一台”时，`Yme009CdcUniquenessGate` 和 `TrySelectCurrentPort` 使用整段实例 ID 的序号相等，不比较 VID，也不提取 PID 或 MI。

因此这次日志中的两个接口会被识别成两个不同的 `IdentityKey`。相同的 VID 不参与判断，也不能据此认定它们是同一物理设备。

### 3.2 代码把什么当成设备身份

`Yme009CdcRecognition.cs` 的注释写明：同一台设备的比较键是接口实例 ID，不是 COM 口。`Yme009DeviceConnection.Temporary` 保存的也是这个字符串。

这是当前实现选择的比较键。Technical Design §2.5.1 和 §2.7.2 仍把“哪一个字段代表同一台 CDC 设备”列为待验证，并明确不能预先把 COM、VID/PID 或父设备名认定为设备身份。§2.7.3 也未决定身份字段组合。

接口实例 ID 标识的是当前这次 USB 枚举中的一个接口节点。PID、MI 或实例后缀变化后，整段字符串就不再相等。当前程序没有另一套物理设备身份。它把接口实例 ID 用作本轮目标身份，这个选择尚未被设计或真机证明为物理设备身份。

### 3.3 Enable 后代码有没有处理接口切换

设计 §2.6.2 已经要求：开启 ADB 可能使 USB 重新枚举；关闭必须重新发现 CDC 并打开当次 COM；不得复用开启时的 COM 或串口句柄。`Yme009AtAdbSwitch.ExchangeAsync` 对 Enable 和 Disable 都执行了重新发现。

同一节同时要求：用本轮上下文中的设备身份确认候选属于本轮目标；仅凭当前只有一个 CDC 候选，而身份对不上，不得把它当成目标。`TrySelectCurrentPort` 实现的就是这条规则。发现结果必须恰好一个候选，并且 `IdentityKey` 与传入身份序号相等，否则不打开串口、返回 `NotSent`。

代码没有以下处理：

- 不读取父设备实例 ID、Container ID、USB 序列号或位置路径；
- 不解析实例 ID 中的 VID、PID、MI；
- 不把 Enable 前的 `PID_2012&MI_01` 映射到 Enable 后的 `PID_2006&MI_02`；
- 不在接口 ID 变化后改用新的 COM 发送 Disable。

所以，重新发现只解决“COM 可能变化”。它没有解决“接口实例本身变化后如何仍识别为同一物理设备”。

### 3.4 这次变化是否使 Disable 必然跳过

对这次日志，结论可以直接由代码得到：关闭传入的是 Enable 前那条实例 ID；重新发现得到的是另一条实例 ID。`TrySelectCurrentPort` 在第 187–190 行判定不匹配并返回，随后不会执行 `Open` 和 `Write`。这与“未打开串口、未发送 Disable、结果 NotSent”一致。

只要关闭时唯一的 CDC 候选实例 ID 与门禁保存的实例 ID 不同，当前策略就会跳过。若旧接口仍在、新接口也在，候选数不是 1，同样会跳过，原因变成“当前不是唯一设备”。这两种跳过都是当前规则的确定结果。

不能由此写成“每台 YME009 每次 Enable 都必然切换接口”。日志只证明这一次从记录到的 `2012/01` 接口变成了记录到的 `2006/02` 接口。设备是否始终这样切换，需要设备团队确认。

### 3.5 能否判断两个 CDC 接口属于同一物理设备

当前 SnVerify 源码不能。它没有保存可以跨 PID、MI 比较的父节点、容器或序列号。

参考工程 `CdcAcmCandidateMatcher` 会保留 `InterfaceInstanceId`、`ParentInstanceId` 和 `ParentProduct`，筛选要求 VID `0E8D` 且父产品名等于 `M6x-66B`。`AtPreparationTests.Should_MatchBothSamples_When_PidAndInterfaceDiffer` 用两条人造实例分别测试：

- `USB\VID_0E8D&PID_2006&MI_02\7&96D7C26&0&0002`
- `USB\VID_0E8D&PID_2012&MI_01\7&17614D17&0&0001`

这个测试只证明参考筛选器可以分别接受两种 PID 和 MI。两条样例的后缀不同，测试也没有断言它们是同一块物理设备。`SmtAtSession.DisableCoreAsync` 关闭时只检查候选数量是否为 1，不比较开启时的接口实例或父实例。Technical Design §2.7.1 已写明：这不能证明重新枚举前后是同一台物理设备。YME009 也不能改用“关闭时只剩一个 CDC 就发送”。

可能用于将来比较的字段，例如父实例 ID、Container ID、USB `iSerial`，本次代码和日志都没有采集。没有这些字段之前，不存在已经证实的同一物理设备判断。

### 3.6 这次变化是不是预期的 USB 枚举切换

已由设计写明的只有：开启 ADB 后 USB 可能重新枚举，COM 不得复用（§2.6.2、§2.7.2）。设计同时把“YME009 真机是否重新枚举，以及哪组 USB 字段在重新枚举后仍指向同一台设备”列为未核实。

这次日志证明：Enable 成功之后，关闭阶段看到的 CDC 接口实例与 Enable 前不同，PID 与 MI 片段也不同，并且随后出现了一条 ADB `device` 记录。时间顺序与“开启 ADB 后 USB 配置发生了变化”相容。

源码和现有文档没有写明 YME009 会从 `PID_2012&MI_01` 切换到 `PID_2006&MI_02`，也没有写明后一个接口就是前一个接口的替代 AT 口。因此这次具体切换仍是待设备团队确认的现象，不能写成已确认的设备协议。

## 4. CDC 与 ADB 身份字段对照

| 字段 | 当前来源 | 代码是否保存 | 身份性质 | 本次日志中的值 |
|---|---|---|---|---|
| CDC 接口实例 ID | `SetupDiGetDeviceInstanceIdW`，保存为 `IdentityKey` | 是，临时上下文的唯一 CDC 身份 | 当前 USB 接口节点。PID、MI 或实例后缀变化后失效 | Enable 前含 `VID_0E8D`、`PID_2012`、`MI_01`；Disable 时含 `VID_0E8D`、`PID_2006`、`MI_02`。任务说明两条完整实例身份不同 |
| COM | 设备注册表 `PortName` | 否。只在一次 AT 交换内使用 | 当前枚举下的通信地址 | Enable 前为 `COM38`。关闭因身份不匹配没有打开串口；任务未提供新接口的 COM |
| Compatible IDs | SetupAPI 注册表属性 | 否。只用于 ACM 筛选 | 接口类别，不是单台设备身份 | 日志未给出原文。两个接口都能进入候选，说明它们都通过了当前 ACM 与 COM 筛选 |
| VID、PID、MI | 没有独立字段。只可能作为实例 ID 的文本片段存在 | 否 | VID 是厂商编号，不能单独证明同一台设备。PID 与 MI 会随接口或配置变化 | 见上 |
| 父实例 ID、Container ID、USB 序列号 | SnVerify 的 YME009 发现没有读取 | 否 | 有可能成为跨接口的硬件身份，但是否稳定尚未取证 | 无 |
| ADB 第一列 | `Yme009AdbDeviceListParser` 的 `Serial` | 关联成功后可写入 `SerialForLog`。本次关联未成功，所以未进入正式上下文 | 本次 ADB 列表中的设备序列号列。它可能是设备稳定序列号，也可能只是 ADB 当前报告的序列号；源码没有证明它等于 USB `iSerial` 或扫码 SN | `MTK0002610190813190` |
| ADB 状态 | 第二列，`State` | 是，随本次查询结果存在 | 会话状态。只有精确等于 `device` 才算可通信 | 第二次查询为 `device` |
| `transport_id` | `transport_id:` 后面的值 | 只有关联证明后才写入正式上下文的 `TransportId` | 当前 ADB 传输编号。断开或重连后可以重新分配，不是物理设备永久身份 | `2` |
| `product`、`model`、`device` | 出现在 `adb devices -l` 原文中 | 否。解析器从第三列起只查找 `transport_id:` | 产品型号文本，不是单台设备身份 | `M6x-66B`、`M6x_66B`、`M6x-66B` |
| 扫码 SN | 轮次日志范围 `Yme009RoundLogScope` | 不传入 `AssociateAsync` | 包装工位扫描值。本次关联接口只有 CDC `IdentityKey` 和可通信 ADB 列表 | 本次任务没有给出该轮扫码 SN |

`AssociateAsync` 的签名是 CDC 身份字符串加可通信 ADB 设备列表。生产实现忽略这两个参数，直接返回 `Yme009AssociationResult.Unproven()`。`TryPromote` 要求 `IsProven` 且 Transport ID 非空，否则保持 `Temporary`。`AllowsBusinessUse` 只有正式阶段、未作废且 Transport ID 非空时才为真。`BuildDirectedArguments` 只会生成 `-t {TransportId}`，并拒绝调用方再带 `-s`、`-t`、`-d`、`-e`、`--one-device`。

正式上下文的可选 `SerialForLog` 按 §2.5.1 只用于日志，不能单独作为 CDC 关联证据，也不能作为定向执行依据。

### 4.1 现有工程里有没有可复用的关联实现

SnVerify 中没有第二种身份关联器。生产路径固定使用 `Yme009UnprovenIdentityAssociator`。

参考工程的 Ready 逻辑按 Transport ID 连续观察 `device`，不读取 CDC 接口或父设备。§2.4.3 和 §2.7.1 已确认：参考工程没有把 CDC 目标映射到 ADB Transport 的实现。`AdbTarget` 的 Serial 只进日志。因此不能把参考工程当成关联问题已经解决的证据，也不能把“列表里只有一台”或“某个 Transport ID 连续为 device”移植成 YME009 的证明。

本次 Ready 成功只证明第二次查询中有一台状态为 `device` 的 ADB 设备。它没有证明这台 ADB 设备就是通过 CDC 门禁的那一台。当前关联器即使看到设备数为 1，也按规定返回未证明。

### 4.2 能否用 SN 或 USB 序列号关联

当前代码不能。CDC 侧没有独立序列号。ADB 侧的 `MTK0002610190813190` 没有与任何 CDC 字段比较。扫码 SN 也没有进入关联函数。

一次真机里同时出现 ADB 序列号和 CDC 接口变化，只能说明两侧当时各有一个标识。它不能证明二者相等，也不能证明 ADB 序列号在 PID 切换后仍与同一个 USB 节点对应。

不依赖时间先后、也不依赖“只有一台 ADB 设备”的方法，必须有一个两侧都能读到、并且在目标设备之间唯一的稳定值。当前源码和这次日志都还没有提供这个值。在设备团队确认之前，不存在可以安全实现的关联算法。

## 5. ADB Disable 当前行为分析

关闭策略拒绝继续使用 Enable 前的 COM，是因为 §2.6.2 认为 USB 重新枚举后旧 COM 和旧串口句柄可能失效。`Yme009DeviceConnection` 因此不保存 COM。

关闭策略同时拒绝在身份对不上时发送命令，是因为重新发现可能看到另一条 CDC。§2.6.2 第 1021 行明确：只有一个候选不足以证明它是本轮目标。`Yme009AdbClosePolicy.ShouldAttemptClose` 只决定“要不要进入关闭尝试”；真正是否写入命令由 `TrySelectCurrentPort` 的唯一性和身份相等决定。AT 已确认且临时身份还在时，策略会进入关闭，但匹配失败后必须跳过发送。

这次日志正是这条路径：关联失败后仍用临时上下文进入关闭；新的接口实例不等于旧实例；结果 `NotSent` / `Skipped`；随后 `Release` 清理。跳过没有覆盖原来的接入结果“身份关联未证明”。

在还不能证明新接口属于同一物理设备时，保持跳过是当前设计和当前代码共同要求的行为。删除身份比较，或看到唯一 CDC 就发送 Disable，会把命令发到一个尚未证明的接口上。本文不采用这种做法。

若要在接口重新枚举后仍能关闭，需要先把“物理设备身份”和“当前 CDC 接口身份”分开：

- 物理设备身份必须在 PID、MI 变化后仍然可比较，并且能排除电脑上的其他设备。
- 当前 CDC 接口身份只负责找到这一次应该打开的 COM。
- 关闭时先用物理设备身份证明候选属于本轮，再要求属于该物理设备的目标 CDC 唯一，然后使用它的当前 COM。

这条路径现在缺少已被真机证明的物理设备字段。字段未证明前，继续跳过 Disable 比放宽匹配更符合 §2.6.2。

## 6. 证据、推断与未证实事项

### 6.1 已有证据

- 当前筛选不识别具体 PID 或 MI。两条不同实例 ID 只要都是 CDC ACM 且有 COM，就会成为两个候选。
- 同一设备比较使用整段接口实例 ID 的序号相等。COM 不参与比较，也不留在上下文中。
- 这次真机在 Enable 前三次确认了同一条含 `PID_2012&MI_01` 的接口，Enable 得到规定的成功响应。
- Ready 第二次查询解析出一台 `device`，Transport ID 为 `2`，ADB 第一列为 `MTK0002610190813190`。解析器不保存 product、model、device。
- 生产关联器不读取这些值，固定返回未证明，所以没有正式上下文，也没有进入业务检测。
- Disable 重新发现到另一条含 `PID_2006&MI_02` 的接口实例。当前相等比较因此不会打开串口。
- 参考工程可以分别接受两种 PID/MI 样例，但关闭时不比较开启时的实例，也不能作为同一物理设备的证明。
- SnVerify 与参考工程都没有已实现的 CDC 到 ADB Transport 关联。

### 6.2 合理推断

以下推断与日志时间顺序和代码路径相符，但源码没有把它们定义成设备协议：

- Enable 成功后约 1.4 秒出现 ADB `device`，同时关闭阶段的 CDC 接口实例已经改变。这更像开启 ADB 触发了 USB 配置或接口变化，而不是关闭代码随机选错了一条旧数据。
- 在这种接口替换发生时，当前 Disable 会稳定地跳过，ADB 会保持 Enable 之前写入的开启状态。这是代码对“身份不等”的结果，不是日志丢失。
- ADB 第一列使用 `MTK` 前缀，存在它来自设备序列号的可能。参考工程测试夹具里也出现过另一条不同数字的 `MTK...` 实例后缀，但这只是测试数据形态，不能证明本次 `MTK0002610190813190` 就是这条 CDC 的 USB 序列号。

### 6.3 尚未证实

- `PID_2012&MI_01` 切换到 `PID_2006&MI_02` 是不是 YME009 的预期 USB 模式切换。
- 两个接口是不是同一个复合设备上的前后两个配置。
- 父实例 ID、Container ID 或 USB `iSerial` 在这次切换后是否保持不变，以及是否在多台设备之间唯一。
- `MTK0002610190813190` 是否等于 USB 序列号、设备产测序列号或包装扫码 SN。
- Disable 应该写回旧接口，还是写到新的 `PID_2006&MI_02` 接口；新接口是否接受同一条 AT 关闭命令。
- Transport ID `2` 在断开、重连或下一台设备插入后会不会被复用。§2.7.2 已将此列为待验证。
- 本次跳过关闭后，设备实际是否仍保持 ADB 开启，以及对下一轮 CDC 识别和再次 Enable 的影响。

## 7. 需要设备团队确认的问题

1. Enable `persist.sys.skyroam.adb.enable 1` 后，设备是否按规定从 `VID_0E8D&PID_2012&MI_01` 重新枚举为 `VID_0E8D&PID_2006&MI_02`？旧接口是否一定会消失？
2. 这两个接口是否一定属于同一块物理板？请给出能在两台设备同时连接时仍唯一的硬件标识，并说明它在切换前后分别出现在哪个 Windows 节点。
3. 父设备实例 ID、Container ID、USB 序列号中，哪些在 PID 和 MI 变化后保持不变？
4. `adb devices -l` 第一列 `MTK0002610190813190` 的生成规则是什么？它是否等于 USB `iSerial`，是否等于包装工位扫描的 SN，是否会在重新枚举后变化？
5. ADB 已开启时，关闭命令必须发送到哪个 CDC 接口？`PID_2006&MI_02` 是否仍是 AT 命令口，波特率、结束符和成功响应是否与 Enable 前相同？
6. 若软件因不能证明身份而跳过 Disable，设备会保持 ADB 开启到何时？重新插拔、重启或再次发送 Enable 的预期状态是什么？
7. `transport_id` 是否只在当前 ADB server 会话内有效？设备重连后是否可能把同一个编号分配给另一台设备？

在这些问题有证据之前，不增加匹配算法，也不放宽关闭比较。

## 8. 可选技术方案及利弊

以下方案都保留“不能证明就不发送 Disable、不交付 Transport ID”。它们不是本次实施项。

| 方案 | 做法 | 安全性 | 可靠性 | 复杂度 | 对现有设计的影响 |
|---|---|---|---|---|---|
| A. 维持现状 | 接口实例 ID 不等就跳过 Disable；关联继续返回未证明 | 高。不会向未证明的 CDC 或 ADB 设备发送后续命令 | 关闭成功率低。只要接口实例变化，关闭命令不会发出 | 无 | 与 §2.6.2、§2.7.2 一致。ADB 可能保持开启 |
| B. 先取证，再决定物理身份字段 | 同一次插拔中记录 Enable 前、Ready 后、Disable 前的完整接口实例、父实例、Container ID、USB 序列号和 `adb devices -l` 原文 | 高。取证阶段不改变发送条件 | 能否关联取决于这些字段是否真的稳定且唯一 | 低到中。先做只读采集 | 不提前选定 §2.7.3 禁止先行决定的字段 |
| C. 物理身份与当前接口分开 | 设备团队确认稳定字段后，用该字段证明同一物理设备；关闭仍要求该设备当前目标 CDC 唯一，再使用它的新 COM | 中高。证明依据是稳定硬件字段，不是“只剩一个口” | 取决于字段在重新枚举和多设备下都有效 | 中。要扩展发现结果和关闭匹配，但不改变 AT 原文 | 符合 §2.5.1 对身份与 COM 分离的要求。必须先修订已确认的身份字段，不能直接编码 |
| D. 通过每台可通信 ADB 读取设备侧标识，再与 CDC 侧稳定字段比较 | 对每台状态为 `device` 的 Transport 定向读取设备团队指定的标识，与 CDC 父节点或 USB 序列号比较；只有唯一匹配才锁定该 Transport，并用匹配后的当前 CDC 接口关闭 | 高，前提是标识本身唯一且读取失败时保持未证明 | 不依赖设备数量为 1，也不把 Transport ID 当永久身份 | 高。关联失败时仍然没有关闭用的新接口绑定 | 符合“Ready 与关联分开、不能证明不交付”的设计。读取命令和标识名必须由设备团队给出 |
| E. 关闭时只要 CDC 数量为 1 就发送 | 参考工程 `DisableCoreAsync` 的数量判断 | 低。另一台恰好唯一的 CDC 也会收到关闭命令 | 表面能覆盖这次单台切换，但不能在多设备时证明目标 | 低 | 与 §2.6.2“仅凭一个候选不得断言是目标”冲突。不采用 |

方案 C 和 D 可以同时成立：C 解决关闭时的 CDC 接口替换，D 解决 CDC 与 ADB Transport 的关联。二者都依赖 B 先取得稳定字段。方案 E 不进入候选实施。

## 9. 推荐下一步验证顺序

1. 把第 7 节的问题交给设备团队，先确认 `2012/01` 到 `2006/02` 是否为规定切换，以及 Disable 应该落在哪个接口。
2. 做一次不改变匹配规则的只读取证：同一块板在 Enable 前、ADB Ready 后、Disable 前各记录完整接口实例 ID、父实例 ID、Container ID、USB 序列号、COM 和完整 `adb devices -l`。本次调查不实现这次采集。
3. 用两台设备同时连接重复取证，排除“字段存在但只是因为当时只有一台”的假关联。
4. 只有某个字段在同一块板上跨 PID/MI 保持不变，并且在两台设备之间不同，才评审方案 C 或 D。评审前不写关联器，不改 `TrySelectCurrentPort`。
5. 关联和关闭都有真机证据之后，再重新判断 Phase 3 能否验收。在此之前不开始 Phase 4。

## 10. 对 Phase 3 当前完成度的判断

Phase 3 的代码链路已经能在真机上完成 CDC 连续确认、AT Enable、ADB Ready、按未证明拒绝正式上下文、按身份不匹配跳过 Disable，以及资源清理。这次日志把此前缺少的 Enable 响应、Ready 分类和 Disable 未发送原因补成了可核对的证据。

Phase 3 仍未达到正常接入验收：

- 身份关联没有证明 CDC 与 Transport ID `2` 是同一台设备，正式连接上下文没有交付。
- 当前接口实例比较不能跨越这次观察到的 PID/MI 变化，关闭命令没有发出。设备上的 ADB 是否仍开启，尚未在本次调查中复测。
- §2.7.2 列出的身份字段和关联方法仍然待验证。本次真机结果证实了这个缺口，没有提供可以替代未证明关联器的算法。

因此 Phase 3 仍是“接入实现已运行，真机验收未完成”。本次调查不宣告 Phase 3 通过，也不进入 Phase 4。
