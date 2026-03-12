# Phase3 Product 与 VerificationParameter 评审结论（Session 批次语义）

> 状态：本文件已与最新评审结论对齐，且代码已按该结论落地实现。  
> 批次语义统一为 **Session**，`VerificationParameter` 目标语义为 **Session 参数快照**。

## 一、最终结论（摘要）

1. **批次 = TestSession**，不是 Order。
2. `VerificationParameter` 由“项目级参数源”演进为“**Session 级参数快照**”。
3. 采用现有表演进（不新建业务新表），以 `SessionId`（`TestSession.Id`）作为参数关联主键语义。
4. 参数查询主链路收敛为：`SessionId -> VerificationParameter`。
5. `TestRecord` 继续保留 expected/actual 快照，作为历史审计事实。

---

## 二、目标数据关系

```text
Order
  └─ TestSession
        ├─ VerificationParameter
        └─ TestRecord
```

| 实体 | 职责 |
|---|---|
| `Order` | 订单 |
| `TestSession` | 一次测试批次（流程主实体） |
| `VerificationParameter` | 批次参数快照（按 Session 绑定） |
| `TestRecord` | 单设备测试结果与审计快照 |

---

## 三、VerificationParameter 目标模型（评审版）

建议字段：

- `Id`
- `SessionId`（`int`，FK -> `TestSession.Id`）
- `ExpectedAndroidVersion`
- `ExpectedBoardVersion`
- `ExpectedChargeBoardVersion`
- `CreatedAt`

建议约束：

- `UNIQUE(SessionId)`（一个 Session 一组参数快照）

---

## 四、流程语义调整（与现状差异）

### 4.1 StartBatch 关键顺序（目标）

```text
StartBatch
  -> CreateSession
  -> SaveVerificationParameter(SessionId,...)
  -> StartTesting
```

说明：

- 当前代码存在“先保存参数、后创建 Session”的路径；
- 会话化改造后需改为“先有 Session，再落参数快照”。

### 4.2 ProcessCoordinator 参数读取（目标）

由现状：

- `Session -> Order -> Product -> productName -> GetParameter(productName)`

收敛为：

- `GetParameterBySessionId(sessionId)`

---

## 五、为什么这么改（设计理由）

1. **批次语义一致**
   - 参数与执行批次同生命周期绑定，避免项目维度覆盖问题。

2. **职责清晰**
   - Session 负责流程；
   - Parameter 负责该批次参数快照；
   - TestRecord 负责结果审计快照。

3. **扩展友好**
   - 后续新增参数字段仅扩展 `VerificationParameter`，不污染 `TestSession` 主实体。

---

## 六、实施前已知风险（评审提醒）

1. 历史 `VerificationParameter(ProjectId)` 数据无法严格映射到历史 Session 快照。
2. 需要同步改动模型、存储、参数服务、流程时序与测试。
3. 属于架构改造，不应与“UI 默认值回填”需求耦合实施。

---

## 七、关联文档

- 详细实施评估请见：`docs/phase3/Phase3_VerificationParameter_Session_Based_Assessment.md`
- 本文定位：评审结论摘要与架构方向对齐说明。
