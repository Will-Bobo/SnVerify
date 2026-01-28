# Phase 2.5 阶段 1 可执行 Prompt

> 本文档供 Cursor Agent 或人工执行阶段 1 时**复制进对话**使用。  
> 执行前请先阅读本文档中的「开发原则」与「必读文档」，再按「本阶段目标与交付物」实施。

---

## 一、开发原则（必须遵守）

执行本阶段时，**严格参照下列开发文档**，不得省略或越过。

### 1.1 TDD（测试驱动开发）

* **严格遵循 TDD 开发模式**：先写测试，再写实现。
* **规则与契约必须有测试覆盖**：SessionId 规则、命名校验、按 Session 导出、PASS/FAIL 去重逻辑等，均需有对应单元测试。
* **流程**：针对每个交付物（表结构、SessionId 生成、导出 API、命名校验接口）→ 先写 failing 测试 → 再写实现 → 通过后进入下一项。
* **参照文档**：`docs/03_Dev_Rules_TDD_and_AI.md`（TDD 原则、AI 协作规则、提交纪律）。

### 1.2 架构与分层

* **MVVM + Service 分层**：View 不含业务逻辑；ViewModel 不直接访问 IO/协议/线程；状态由 Service 维护。
* **业务规则可单元测试**：Domain/Service 层逻辑必须可测，外部依赖（DB、文件）通过接口或 Mock 隔离。
* **参照文档**：`docs/07_Technical_Architecture_and_Dev_Guide.md`（分层结构、状态 vs 事件、Snapshot 模型）。

### 1.3 架构红线

* Domain 层不得依赖 WPF、UI、硬件、MES。
* 不允许在 UI 层直接写业务逻辑。
* 不允许为“方便”跳过文档约束。
* **参照文档**：`docs/02_Architecture_Guardrails.md`。

### 1.4 AI 协作与可维护性

* 不新增文档未定义的功能；不重写架构分层；复杂逻辑拆成可测试单元。
* 新生成的 public/internal 方法需 XML 注释（summary/param/return）；新生成文件建议加 `author/remarks` 标识。
* **参照文档**：`docs/03_Dev_Rules_TDD_and_AI.md` 第三、四节。

---

## 二、必读文档与章节

执行阶段 1 前，**必须**在下列文档中确认范围与契约，避免越界或遗漏：

| 文档 | 必读章节 | 用途 |
|------|----------|------|
| `docs/Architecture/Phase2.5_Stage_Plan.md` | **§二 阶段 1**（2.1 目标、2.2 交付物、2.3 不做/后置、2.4 风险与依赖） | 本阶段边界与交付物 |
| `docs/Architecture/Phase2.5_Technical_Refactor_Checklist.md` | **§一 数据模型与数据库**、**§二 导出与日志** 中与「按 Session 导出」「PASS/FAIL 规则」相关的表述 | 数据契约与导出规则 |
| `docs/03_Dev_Rules_TDD_and_AI.md` | 全文 | TDD 与 AI 协作 |
| `docs/07_Technical_Architecture_and_Dev_Guide.md` | §2 分层、§3 核心架构原则、§5 数据与存储（在 Phase 2.5 下以 Order/Session/TestRecord 为准） | 架构约束 |
| `docs/02_Architecture_Guardrails.md` | 全文 | 红线条款 |

---

## 三、本阶段目标（一句话）

**用 Order / TestSession / TestRecord 替代 Batch 概念，落成表结构、SessionId 规则与「按 Session 导出」能力（PASS 原样、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条），并为后续阶段提供稳定数据契约。**

---

## 四、交付物清单（按 TDD 顺序建议）

1. **表结构**
   * Order、TestSession、TestRecord（TestRecord 含 SessionId 外键与业务所需唯一约束）。
   * SessionId 规则：`OrderId + "_" + yyyyMMdd_HHmmss`；应用层保证同 Order 同秒不重复；建议 DB 对 SessionId 建唯一索引。
   * **TDD**：先写「SessionId 生成规则」与「表/外键约束」相关测试，再实现迁移或建表。

2. **导出能力**
   * **按 Session 导出**：单 Session → xlsx 双 Sheet（PASS/FAIL）+ txt。
   * PASS：从库查出后**原样写入**，不去重。
   * FAIL：导出时按 `(StickerSN, DeviceSN)` 去重，**保留第一条**；库内保留完整历史。
   * **TDD**：先写「给定 Session 数据 → 导出文件内容与去重结果」的测试，再实现导出逻辑。

3. **命名与校验**
   * ProjectName/OrderName：禁止文件系统特殊字符，长度上限 64，不允许中文。
   * 本阶段提供**领域/服务内校验接口**（如 `IOrderNameValidator` 或等价），供「开始测试」时一次性校验；弹窗由阶段 3 挂接，本阶段不实现 UI 弹窗。
   * **TDD**：先写「合法/非法名称 → 校验结果」的测试，再实现校验逻辑。

---

## 五、本阶段不做 / 后置

* **按项目/按订单的聚合与导出入口**：放到阶段 2 或 3，由 B 做聚合并调用本阶段的「按 Session 导出」API。
* **UI、ProcessCoordinator**：仍可暂时沿用 Batch 术语与调用，在阶段 2 再切到 Order/Session。
* **MES**：本阶段不涉及。

---

## 六、验收标准（M1）

满足以下即可视为阶段 1 完成：

* 新表（Order、TestSession、TestRecord）可用，SessionId 规则落地且可被测试验证。
* 「按 Session 导出」可运行，且 PASS/FAIL 规则符合清单（PASS 原样、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条）。
* 命名/校验接口存在且已有对应单元测试。

---

## 七、执行时可直接复制的一段 Prompt（供 Agent 使用）

```
请实现 Phase 2.5 阶段 1 的目标，严格遵循以下约束：

【开发原则】
- 严格 TDD：先写测试，再写实现；SessionId 规则、导出逻辑、命名校验均需有单元测试。
- 遵守 docs/03_Dev_Rules_TDD_and_AI.md、docs/07_Technical_Architecture_and_Dev_Guide.md、docs/02_Architecture_Guardrails.md 中的架构与协作规则。

【范围与契约】
- 必读：docs/Architecture/Phase2.5_Stage_Plan.md §二（阶段 1）、docs/Architecture/Phase2.5_Technical_Refactor_Checklist.md §一与§二。
- 交付：Order/TestSession/TestRecord 表结构、SessionId = OrderId + "_" + yyyyMMdd_HHmmss、按 Session 导出（PASS 原样、FAIL 按 (StickerSN,DeviceSN) 去重保留第一条）、命名/校验领域接口。
- 不做：按项目/按订单的聚合与导出入口、UI/ProcessCoordinator 切 Order/Session、MES。

【验收】
- 新表可用；按 Session 导出可运行且 PASS/FAIL 规则符合清单；命名校验接口有测试。
```

---

**文档版本**  
* 依据：Phase2.5_Stage_Plan.md §二、Phase2.5_Technical_Refactor_Checklist.md §一/§二、03_Dev_Rules_TDD_and_AI、07_Technical_Architecture_and_Dev_Guide、02_Architecture_Guardrails。  
* 若阶段计划或清单有修订，请同步更新本文档「必读文档与章节」「交付物」「不做/后置」及 §七 的复制段。
