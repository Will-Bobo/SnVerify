# YME009 SN PASS 去重实施影响审查

**审查依据：** `docs/YME009-SN-Pass-Dedup-And-Expected-Data-PRD.md` V0.5  
**审查方式：** 只读核对当前工作区源码  
**范围：** 仅需求 A。不实施需求 B，不设计 `Yme009ExpectedDataSet` / `Yme009ExpectedDevice`，不关闭 OQ-007。  
**日期：** 2026-11-24

本文区分三类表述：源码事实、由源码推出的实施判断、源码不能确定的待核实事项。

## 1. 当前调用链

YME009 扫码不进入 Phase3 或 Legacy 规则链。

1. `MainViewModel.EnterIndependentProductRoundAsync` 确认当前是活动 Session。
2. `GetCurrentSessionId()` 取得业务 SessionName。
3. `StorageService.GetInternalSessionIdBySessionNameAsync` 取得 `TestSession.Id`。
4. 构造 `Yme009InspectionRequest(sessionId, ExpectedAndroidVersion)`。该类只有内部 SessionId 和目标软件版本。
5. `Yme009RoundCoordinator.InspectAsync` 读取属性，调用 `Yme009InspectionJudge.Judge`，再 `Yme009TestRecordMapper.Create` 和 `StorageService.SaveTestRecordAsync`。

`Judge` 的注释写明不访问数据库。当前综合判定只包含五项：设备 SN、软件版本、产测、重启老化、恢复出厂老化。没有历史 PASS 查询。

## 2. 历史 PASS 查询能否复用

可以复用 `StorageService.IsStickerSnPassedInBatchAsync`。不能只看方法名。其 SQL 是：

```sql
SELECT COUNT(1)
FROM TestRecord r
INNER JOIN TestSession s ON r.SessionId = s.Id
INNER JOIN "Order" o ON s.OrderId = o.Id
INNER JOIN Product p ON o.ProductId = p.Id
WHERE o.OrderName = @OrderId
  AND p.ProductName = @ProjectName
  AND r.StickerSN = @StickerSN
  AND r.Result = 'PASS'
```

源码事实：

- 同时过滤 `Product.ProductName` 和 `Order.OrderName`。参数名叫 `orderId`，比较的是订单名。
- 只接受 `Result = 'PASS'`。FAIL、TIMEOUT 和其他结果不会命中。
- SQL 没有 `SessionId` 排除条件，因此该订单下全部 Session，包含当前 Session，都在范围内。
- 不跨项目名，不跨订单名，也不是全库。
- 比较的是 `TestRecord.StickerSN`，应对传入扫码 SN，而不是设备 SN。
- 一次调用只执行这一条语句。命令放在 `using` 中。连接是 `StorageService` 的长期连接，由 `_lockObject` 串行使用，不是每次新建后泄漏。
- 异常会写日志并重新抛出。源码没有设置 `CommandTimeout`。本机 SQLite 没有单独的查询超时类型。超时是否会出现，当前源码不能证实，标为待核实。
- 已有索引 `idx_testrecord_stickersn_result`。这条 SQL 还要连接 Session、Order、Product。源码不能证明该索引不足或错误，本次不把它写成缺陷。

`IsStickerSnPassedInOrderAsync` 不连接 Product。它不是本需求的查询。

调用前必须先用 `Yme009TextComparison.Equals` 确认扫码 SN 与设备 SN 一致。SQL 本身不做 Trim。传入值应使用比较前已经 Trim 的扫码 SN。不一致时不得调用该查询。

`StorageServiceOrderScopeTests.IsStickerSnPassedInBatchAsync_UsesProjectNameAndOrderNameAsBatchKey` 已覆盖不同项目名、相同订单名的隔离。它没有覆盖同一 Session 内已有 PASS、FAIL 不命中、TIMEOUT 不命中。这些要补测试，不是 SQL 缺少条件。

## 3. 项目名和订单名

`Yme009InspectionRequest` 没有这两项。

`GetProductNameBySessionNameAsync` 已存在。路径是 `TestSession.SessionName` → `Order` → `Product.ProductName`。它使用业务 SessionName，不使用内部 Id。

当前存储接口没有一个按 Session 直接返回 `Order.OrderName` 的专用方法。`GetSessionBySessionNameAsync` 能返回 `OrderId`，订单名还要再查 `Order`。`GetAllOrdersAsync` 可以在内存中按 Id 查找，但那是全表读取。

实施判断：在 `InspectAsync` 中按已有 SessionName 取项目名，并增加一条与之对称的订单名查询，或一次查询同时返回两者。不要把 `ProductCode` 当作项目名。`GetProductCodeBySessionIdAsync` 返回的是 `YME009` 这类产品类型，不能用于去重。

## 4. 综合判定与主失败原因

`Yme009InspectionJudge.SelectPrimary` 当前顺序是：任一传输失败 → 协议无效 → 必要值缺失 → SN 不一致 → 版本不一致 → 产测失败 → 重启老化失败 → 恢复出厂失败。一次调用会给五项都写出分项结论，主失败码只留一个。

V0.5 的优先级不同：SN 不一致，然后是历史 PASS 去重失败，然后是已启用 Excel 时的预期数据校验失败，最后才是原有五项的现有顺序。因此不能把去重塞进 `SelectPrimary` 的最前面就结束，也不能在命中后跳过 `Judge`。

最小改动：

- `Judge` 继续不访问数据库，仍负责五项分项结论。
- `InspectAsync` 在属性解析之后做 SN 门禁。一致才查询；不一致不查询，去重不产生 PASS 或 FAIL。
- 查询命中、未命中、抛出异常分成三个结果，不合并。
- 在 `Judge` 之后用一个纯函数按 V0.5 优先级覆盖 `Result` 和 `FailReason`。五项分项结论保持 `Judge` 的输出。
- 需求 B 本次不实现。优先级函数可以留下「身份校验失败」这一档的输入，但没有 Excel 结果时跳过该档。

命中时 `FailReason` 使用已有常量 `RuleFailReasonCodes.SnDuplicate`，值是 `SN_DUPLICATE`。`FailReasonTextResolver` 已把它解析为「设备SN已经检验」。

风险：`Yme009ResultClassifier` 只把 `Yme009FailReasonCodes.IsBusiness` 和 `IsReadException` 中的码分成业务失败或读取异常。`SN_DUPLICATE` 不在其中。`Result = FAIL` 且失败码为 `SN_DUPLICATE` 时，分类结果是 `Unregistered`。正式 FAIL 界面仍会显示，因为投影先看 `Result` 是不是 `PASS` 或 `FAIL`。统计桶是否应把这种记录算作业务失败，源码没有现成规则，标为待确认。不因此新增 `Result` 枚举。

查询异常也不能写成 `SN_DUPLICATE`。现有 YME009 失败码没有数据库查询失败。`Result` 只有 `PASS` 和 `FAIL` 两个由判定器产生的取值。导出器另外认识历史记录中的 `TIMEOUT`，但 YME009 判定器不会产生它。为了满足「最终不允许 PASS」，实施判断是使用已有的 `FAIL`。查询异常专用的 `FailReason` 字符串尚无现成常量；新增字符串会落入 `Unregistered`。具体码值标为待确认，不能在本次审查里选定。

当前 `SelectPrimary` 把读取失败排在 SN 不一致之前。V0.5 把 SN 不一致放在第一优先级。设备 SN 缺失时，现有比较会得到不一致，同时现有主失败码可能是必要值缺失。覆盖函数应按 V0.5 把 SN 不一致放在前面。这是优先级差异，不是建议停止其他分项判定。

## 5. 保存与异常

`SaveTestRecordAsync` 始终 `INSERT` 一条新 `TestRecord`。它不按 SN 更新旧行。因此不会修改历史 PASS，也不会覆盖当前 Session 已有记录。`TestRecord(StickerSN, Result)` 只有普通索引，没有唯一约束。

保存使用单条事务。异常时回滚并重新抛出。`InspectAsync` 捕获保存异常后返回 `Decision` 仍在、`RecordSaved = false`、备注「记录未保存」。记录主键无效时同样按未保存处理。

查询异常发生在保存之前。只要内部 SessionId 有效，现有保存方法仍可尝试插入。若异常已经说明数据库不可用，随后的保存可能再次失败。V0.5 只要求尝试保存，并在失败时明确报告，不保证保存成功。

报告缺口：`Yme009WorkstationSnapshot.FromResult` 在正式 PASS/FAIL 且未保存时把 `SaveStatusText` 设为「保存失败」。`MainWindow.xaml` 没有绑定 `Yme009SaveStatusText`。包含「记录未保存」的 `Yme009RoundSummary` 绑定在 `ShowLegacyAux` 上，而 YME009 工位打开时该区域隐藏。因此当前 YME009 界面不能靠这两处向操作员显示保存失败。实施时需要在 YME009 可见区域显示已有的「保存失败」或同等语句。这是接入现有工位，不是新建一种结果枚举。

查询异常若没有形成 `Result = FAIL` 的正式判定，投影会走「检测未完成」分支，同样不保证操作员看到保存失败。所以查询异常路径仍应给出正式的非 PASS 判定，再进入现有保存和投影。

## 6. UI

YME009 工位已经有五张卡片：SN 一致性、软件版本一致性、生产测试、重启老化、恢复出厂设置老化。每张卡片有分项判定和分项原因。

综合结果在工位结果卡上显示 PASS 或 FAIL。主失败文案绑定 `Yme009PrimaryReason`，来源是 `FailReasonTextResolver.Resolve`。Legacy/Phase3 的 `FailReason` 区域在 YME009 工位显示时关闭。

最小改动：不增加第六张卡片。命中时把 `FailReason` 设为 `SN_DUPLICATE`，现有主失败文案绑定就会显示「设备SN已经检验」。多项失败时仍只把这一个主失败码送进该文案。五项卡片继续显示各自的分项结论；它们是现有检测项，不是新增的失败项列表。本次不建议删除分项原因。

内部名称「SN重复检测」不必做成卡片标题。源码里没有这个标题的现成显示位。

## 7. 预计涉及的文件

必需：

- `SnVerify/Services/Yme009/Yme009RoundCoordinator.cs`：门禁、查询、三条路径、保存失败继续上抛给投影。
- 新增一个不访问数据库的主失败选择函数，放在 `SnVerify/Domain/Product` 中现有判定旁边。
- `SnVerify/MainWindow.xaml`：让 YME009 可见区域能显示已有的保存失败状态。
- 存储查询的测试，以及 YME009 轮次或判定测试。

可能需要：

- `IStorageService` / `StorageService`：补一次按 Session 读取 `Order.OrderName` 的查询。现有项目名查询可以不动。
- `Yme009TestRecordMapper`：在创建记录前接收覆盖后的 `Result` 和 `FailReason`。
- `Yme009ResultClassifier`：仅当确认 `SN_DUPLICATE` 或查询异常码必须进入业务失败桶时才改。当前未确认。
- `Yme009WorkstationProjection`：仅当保存失败不能只靠现有 `SaveStatusText` 表达时才改。

不改：

- KM001、KM008、SOLTAG25 的规则链和界面。
- Excel 导入、预期数据表和 OQ-007。
- `Product`、`Order`、`TestSession`、`TestRecord` 的表结构。

## 8. 需要补充的测试

现有 `Yme009InspectionJudgeTests` 覆盖五项独立判定，不覆盖历史 PASS。`StorageServiceOrderScopeTests` 只覆盖批次键的项目名隔离。

需求 A 需要新增或扩展：

- SN 一致且无历史 PASS 时，「SN重复检测」通过。
- SN 一致且相同项目名、相同订单名存在历史 PASS 时失败，失败码为 `SN_DUPLICATE`，综合结果为 FAIL。
- 历史 FAIL 不命中。
- 历史 TIMEOUT 不命中。
- 其他项目名的 PASS 不命中。
- 其他订单名的 PASS 不命中。
- 当前 Session 中已有的 PASS 能命中。
- 扫码 SN 与设备 SN 不一致时不调用历史查询，去重不产生 PASS 或 FAIL，综合结果为 FAIL，主失败原因是现有 SN 不一致码。
- 命中后五项分项结论仍然生成。
- 多项失败时 `FailReason` 按 V0.5 优先级只留一个。
- 查询抛出异常时不产生 PASS，五项仍判定，并尝试保存。
- 保存抛出异常时结果不是已保存，界面或返回文本明确是保存失败。
- 没有 Excel 数据集时仍然执行去重查询。

## 9. 技术风险与待确认

- 查询异常的 `FailReason` 具体字符串尚未确定。不能使用 `SN_DUPLICATE`，也不能新造 `Result` 枚举。
- `SN_DUPLICATE` 进入 YME009 记录后，统计桶目前会落到 `Unregistered`。是否改为业务失败桶，尚未确认。
- 设备 SN 缺失同时被现有逻辑视为不一致和必要值缺失。V0.5 把 SN 不一致放在主失败原因第一位。分项卡片仍可保留读取失败结论。
- 本机 SQLite 是否会出现可区分的查询超时，源码没有超时设置，不能证实。
- 保存失败文本已经计算出来，但 YME009 当前可见界面没有把它绑上。这是实施时必须补上的显示缺口。
- 需求 B 的第三优先级只预留位置。没有启用 Excel 时，不得因为缺少预期数据而失败，也不得跳过去重。

## 10. 建议实施顺序

1. 补订单名读取，并确认项目名来自 `Product.ProductName`。
2. 在轮次协调器加入 SN 门禁和 `IsStickerSnPassedInBatchAsync`，保持 `Judge` 不访问数据库。
3. 加入主失败优先级函数，并让命中路径使用 `SN_DUPLICATE`。
4. 把查询异常固定为非 PASS，尝试保存，保存失败进入 YME009 可见区域。
5. 按第 8 节补测试。先红后绿。
6. 需求 B 仍单独实施，不放进上述步骤。

## 11. 本次没有修改的内容

本审查没有修改业务代码、测试、数据库、配置，也没有修改 KM001、KM008、SOLTAG25。除本审查文件和 V0.5 PRD 外，没有修改其他文档。没有执行会改变业务数据的操作。
