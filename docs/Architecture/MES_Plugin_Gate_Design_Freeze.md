# MES 插件接入与 Gate 机制设计说明（冻结版）

## 1. 文档目的（Why this document exists）

本文件用于**冻结 MES 接入的总体设计哲学与技术边界**，明确：

* MES 在 SnVerify 系统中的**合法职责范围**
* MES 插件的**接入位置、扩展方式与禁止行为**
* 防止未来 MES 接入时**侵入检验逻辑、破坏系统稳定性**

本文件是：

* 架构级冻结文档
* 新 MES 厂商接入前的**必读文档**
* Cursor Agent 生成 / 重构代码时的**最高优先级约束之一**

---

## 2. 核心设计哲学（Design Philosophy）

> **MES 是外部约束系统，而不是业务裁决系统。**

系统遵循以下不可违反的原则：

1. MES **可以决定流程是否允许继续**
2. MES **不能参与本站 PASS / FAIL 的业务判断**
3. 核心检验规则必须在**无 MES 的情况下也可独立运行**
4. MES 的不稳定性**不得传导为系统不确定性**

一句话总结：

> **MES 只能“卡门”，不能“判案”。**

---

## 3. 正确的系统结构位置（Where MES is allowed to exist）

### 3.1 冻结的核心检验链路（不可侵入）

```text
Scan SN
 → Read Device SN
 → Verify (本站业务规则)
 → Result (PASS / FAIL)
```

以上链路：

* 不允许 MES 插件插入
* 不允许 MES 返回规则性判断
* 不允许 MES 改写结果

---

### 3.2 合法扩展点：Gate（闸口机制）

```text
[ MES Pre-Gate ]   ← 可插拔
        ↓
Scan SN
 → Read Device SN
 → Verify
 → Result
        ↓
[ MES Post-Report ] ← 可插拔
```

MES **只能存在于 Gate 层**：

* **Pre-Gate**：是否允许进入本站检验
* **Post-Report**：向 MES 上报本站结果

---

### 3.3 性质定义（非常关键）

Post-Report（MES 上报）失败
→ 系统健康态异常
→ 不是业务失败
→ 绝不影响 PASS / FAIL
2️⃣ UI 行为原则
✅ 允许 UI 提示
❌ 禁止阻断流程
❌ 禁止弹窗 / FAIL 化
3️⃣ 提示等级（弱提示）
推荐位置：
状态栏小字（优先）
或日志区 WARN
文案必须显式说明：
MES 上报失败（不影响当前测试结果）
4️⃣ 架构约束
MES 不能直接引用 View / ViewModel
只能通过：
event
状态对象（Snapshot）
或 Health 通知

---

## 4. Pre-Gate（前置闸口）机制设计

### 4.1 设计目的

用于支持以下典型场景：

* 前站未通过，不允许本站测试
* MES 要求必须先校验历史流程
* MES 短暂不可用，但工厂允许降级运行

---

### 4.2 接口定义（冻结）

```csharp
public interface IMesPreCheck
{
    Task<MesPreCheckResult> CheckAsync(MesContext context);
}
```

返回结果必须是**极简三态**：

```csharp
public enum MesPreCheckDecision
{
    Allow,          // 允许进入本站流程
    Reject,         // 明确禁止进入本站流程
    DegradedAllow   // MES 不可用，但允许继续（降级放行）
}
```

---

### 4.3 明确禁止的行为

Pre-Gate **不得**：

* 返回 PASS / FAIL
* 返回具体测试规则
* 修改 SN、设备信息
* 读取或写入本站 TestRecord

Pre-Gate 的唯一职责：

> **回答“能不能开始”，而不是“怎么判断”。**

---

## 5. Post-Report（结果上报）机制设计

### 5.1 接口定义（冻结）

```csharp
public interface IMesResultReporter
{
    Task ReportTestResultAsync(TestResultContext context);
}
```

设计约束：

* 上报失败不得影响本站最终结果
* 所有异常只记录日志，不回滚、不重试业务流程

---

## 6. Capability-based 插件机制（防爆雷设计）

### 6.1 MES 能力声明（冻结）

```csharp
public class MesCapabilities
{
    public bool SupportsPreCheck { get; init; }
    public bool RequiresPreCheck { get; init; }
    public bool SupportsResultReport { get; init; }
}
```

---

### 6.2 启动期能力校验（必须）

```csharp
if (mes.Capabilities.RequiresPreCheck && !mes.Capabilities.SupportsPreCheck)
{
    BlockStartup("MES requires pre-check but not supported");
}
```

目的：

* 防止“半接入 MES”进入生产
* 避免运行期才暴露架构问题

---

## 7. ProcessCoordinator 中的集成方式（推荐实现）

```csharp
public async Task StartVerificationAsync(string sn)
{
    // 1. MES 前置闸口（可选）
    if (_mesPreCheck != null)
    {
        var result = await _mesPreCheck.CheckAsync(context);
        if (result.Decision == Reject)
        {
            FailFast(result.Reason);
            return;
        }
    }

    // 2. 冻结的核心检验流程（不可改）
    await _verificationFlow.StartAsync(sn);
}
```

---

## 8. 典型 MES 接入模式示例

### 模式 A：只上报结果（最常见）

* SupportsPreCheck = false
* SupportsResultReport = true

### 模式 B：强前置校验

* RequiresPreCheck = true
* SupportsPreCheck = true
* SupportsResultReport = true

### 模式 C：MES 不可靠，允许降级

* PreCheck 返回 DegradedAllow
* UI 标记“MES 降级运行”
* 日志完整记录

---

## 9. 冻结声明（Freeze Statement）

以下内容在当前版本周期内视为**架构冻结规则**：

* MES 不得侵入 Verify 逻辑
* MES 只能通过 Gate 影响流程
* Pre-Gate / Post-Report 接口形式
* Capability-based 插件机制

任何突破上述边界的需求，必须：

* 明确说明破坏点
* 明确长期维护成本
* 明确是否进入下一冻结版本

---

**建议文件路径：**

```text
/docs/architecture/MES_Plugin_Gate_Design_Freeze.md
```

该文件应作为：

* 新 MES 接入评审的第一检查项
* Cursor Agent 重构时的强约束输入

---

## 10. Phase 2.5 补充决策（已拍板）

以下为 Phase 2.5 抽象层构建时的明确决策，后续实现必须遵循。

### 10.1 Phase 2.5 抽象层范围

* 只做：接口 + 上下文 + Capabilities + Stub/NoOp + 现有实现收口为 ResultReporter 的 Adapter。
* 不做：杰科真实协议实现。杰科接入放在下阶段，本阶段仅为「接具体协议」预留抽象。

### 10.2 MES 开关策略（MesMode）

**设计原则**：无 MES 时，检验程序可独立运行，不受任何影响。

| 模式 | 含义 | 行为 |
|------|------|------|
| **Disabled** | 完全不启用 MES | 不调用 PreCheck / Post-Report，逻辑上视为无 MES |
| **Enabled** | 启用 MES，但失败不阻断 | PreCheck Reject 可配置为不阻断 / 降级；Post-Report 失败仅日志与 UI 提示，不阻断流程 |
| **Strict** | MES FAIL 阻断流程 | PreCheck Reject 或 MES 异常时，本条检验不继续 |

**Phase 2.5 约束**：

* 默认：**Disabled** 或 **Enabled**（二选一，由产品定默认值）。
* **不允许提供 Strict**，避免本阶段因 MES 不稳定导致产线停线。Strict 留待下阶段或后续版本，在稳定性验证后再开放。

### 10.3 Pre-Gate 调用粒度

* **每笔 SN 前调用一次 PreCheck**。
* 原因：逻辑上可能需要对每个设备判断「MES 流程是否卡门」，故不采用「Session 开始前调一次」。
* 实现要点：在 `StartVerificationAsync(sn)` 内、进入「Read Device SN → Verify」之前，若 MesMode ≠ Disabled 且插件支持 PreCheck，则先 `await PreCheck.CheckAsync(context)`；若返回 Reject 且当前为 Strict（Phase 2.5 无 Strict，此处为将来预留），则直接返回并提示。

### 10.4 Post-Report 失败时的 UI 提示

* **需要**在界面上提示「MES 上报失败」，但不影响本站 PASS/FAIL 结果。
* 实现要点：在抽象层预留「上报失败」事件或回调，由 UI（如状态栏或固定小字区）订阅并展示简短文案；不由 MES 层直接依赖 View。

---

## 11. 杰科协议待确认点（下阶段对接再用）

本阶段**不讨论、不实现**杰科具体协议，以下仅作记录，便于接杰科时逐项澄清。依据文档：`杰科产测服务器接口协议_MES_20240111.pdf`、`杰科MES接口在测试流程中的使用节点.pdf`。

1. **Pre-Gate 在杰科中的对应**
   * 杰科是否存在「前站未过/未绑定则不允许测」类接口？
   * 若有，是按订单/项目查一次，还是按 SN 查一次？
   * 若有，调用时机是「开始 Session 时」还是「每扫一条 SN 之前」？
   * → 用于确定 `MesContext` 字段及 Pre-Gate 在 SN 前的入参。

2. **结果上报的协议形态**
   * 杰科要求的 URL、方法、请求体/参数格式（例如 order_id、sn、result、test_time 等）。
   * 是否存在「按 Session 汇总上报」与「按单条 SN 上报」两种形态；若有，抽象层是仅支持「单条上报」还是预留「按 Session 批量上报」。

3. **「使用节点」在流程中的位置**
   * 创单/开始测试、每个 SN 结果、结束测试/结束订单，分别对应杰科哪些接口与调用顺序？
   * → 用于判断除 Pre-Gate + Post-Report 外，是否需预留「Session 开始/结束」等扩展点。

4. **鉴权与配置**
   * 是否需要 token、站编号、线体编号等；这些通过 `MesContext` / 插件构造参数注入，还是通过单独配置模块读取。
