# VerificationParameter 会话化设计评估（评审结论版）

> 实施状态：已按本结论完成第一阶段代码改造（SessionId=int FK，参数按 Session 读取/保存，StartBatch 先建 Session 再存参数）。编译通过，单测通过。

## 一、最终推荐的数据模型

系统核心结构：

```text
Order
  └─ TestSession
        ├─ VerificationParameter
        └─ TestRecord
```

关系说明：

| 表 | 作用 |
|---|---|
| `Order` | 订单 |
| `TestSession` | 一次测试批次（批次语义） |
| `VerificationParameter` | 该批次的参数快照 |
| `TestRecord` | 每个设备的测试记录 |

---

## 二、TestSession（保持当前设计）

当前 `TestSession` 结构与评审结论一致，保留：

- `Id`
- `SessionName`
- `OrderId`
- `StartTime`
- `EndTime`
- `Status`
- `VerificationType`
- `ExpectedVersion`

语义说明：

- Session 是批次；
- 生命周期：`Create -> Running -> Completed`。

---

## 三、VerificationParameter（推荐最终版本）

目标：从“项目级参数源”调整为“Session 级参数快照”，并具备后续参数扩展能力。

推荐模型：

```csharp
public class VerificationParameter
{
    public int Id { get; set; }

    /// <summary>
    /// Session 外键（TestSession.Id）
    /// </summary>
    public int SessionId { get; set; }

    /// <summary>
    /// Android版本
    /// </summary>
    public string ExpectedAndroidVersion { get; set; }

    /// <summary>
    /// 主控板版本
    /// </summary>
    public string ExpectedBoardVersion { get; set; }

    /// <summary>
    /// 充电板版本
    /// </summary>
    public string ExpectedChargeBoardVersion { get; set; }

    /// <summary>
    /// 参数创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
```

数据库约束建议：

```text
VerificationParameter
    UNIQUE(SessionId)
```

含义：

- 一个 Session 仅允许一组参数快照。

---

## 四、未来扩展方式

该设计支持参数平滑扩展：

- 新增参数时，仅需对 `VerificationParameter` 增列（`ALTER TABLE ... ADD COLUMN ...`）；
- `TestSession` 结构无需改变。

示例扩展字段：

- `ExpectedWifiVersion`
- `ExpectedMCUVersion`
- `ExpectedBluetoothVersion`
- `ExpectedBatteryVersion`

---

## 五、为什么参数独立表优于塞入 TestSession

若把越来越多的参数字段堆进 `TestSession`，会形成 Fat Entity。

参数独立表优势：

| 优点 | 说明 |
|---|---|
| 结构清晰 | `TestSession` 负责流程，`VerificationParameter` 负责参数快照 |
| 参数独立 | 参数管理与流程状态解耦 |
| 可演进性 | 参数字段可渐进扩展，不频繁扰动 Session 核心结构 |
| 领域语义更清楚 | Session + Parameter 组合更符合当前评审目标 |

---

## 六、ProcessCoordinator 查询方式（最终版）

最终查询主链应收敛为：

```text
SessionId
   ↓
VerificationParameter
```

示例：

```csharp
var param = await _verificationParameterRepository
                .GetBySessionId(sessionId);
```

说明：不再依赖 `Session -> Order -> Product -> ProjectId` 间接映射参数。

---

## 七、StartBatchAsync 正确流程（关键）

流程顺序应调整为：

```text
StartBatch
   ↓
CreateSession
   ↓
SaveVerificationParameter
   ↓
StartTesting
```

伪代码：

```csharp
var session = await _sessionService.CreateSession(orderId);

await _verificationParameterService.SaveAsync(
    session.Id,
    androidVersion,
    boardVersion,
    chargeBoardVersion
);

_currentSessionId = session.Id;
```

评估说明（结合现状代码）：

- 当前实现是“先保存参数再建 Session”，与会话化目标不一致；
- 按本结论执行时，需要改保存时序。

---

## 八、TestRecord 仍需保留 Snapshot

即便参数会演进，`TestRecord` 仍必须记录当时期望值/实际值快照。

意义：

- `VerificationParameter` 改写不会影响历史记录可信度；
- 历史追溯以 `TestRecord` 为审计事实源，参数表作为批次配置源。

---

## 九、与当前代码差异评估（实施前风险）

在“仅评估、不改代码”前提下，需明确以下差异：

1. **模型差异**
   - 当前 `VerificationParameter` 以 `ProjectId` 为键；
   - 目标改为 `SessionId` 唯一。

2. **时序差异**
   - 当前 `MainViewModel.StartBatchAsync` 先存参数后建 Session；
   - 目标是先建 Session 再存参数。

3. **查询差异**
   - 当前 `ProcessCoordinator` 按 `productName` 查参数；
   - 目标是按 `sessionId` 直接查参数。

4. **迁移差异**
   - 历史项目级参数无法精确映射到历史 Session 快照；
   - 需要设计迁移/兼容策略（例如仅对新 Session 生效，或短期回退读取策略）。

---

## 十、实施前待拍板项

1. `SessionId` 采用 `TestSession.Id`（int FK）是否最终确定（本评审结论建议采用）。
2. 历史 `VerificationParameter(ProjectId)` 数据如何处理：
   - 归档后不兼容读取；
   - 或短期兼容读取（过渡期）。
3. 是否同意将“KM001 回填需求”与该架构改造分期推进，避免一次变更多目标耦合。


