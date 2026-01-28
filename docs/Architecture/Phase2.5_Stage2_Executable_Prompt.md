# Phase 2.5 阶段 2 可执行 Prompt

> 本文档供 Cursor Agent 或人工执行阶段 2 时**复制进对话**使用。  
> 前置条件：阶段 1 已完成（Order/TestSession/TestRecord 表、SessionId 规则、按 Session 导出、命名校验接口均已落地且单元测试通过）。  
> 执行前请先阅读本文档中的「开发原则」与「必读文档」，再按「本阶段目标与交付物」实施。

---

## 一、开发原则（必须遵守）

执行本阶段时，**严格参照下列开发文档**，不得省略或越过。

### 1.1 TDD（测试驱动开发）

* **严格遵循 TDD 开发模式**：先写测试，再写实现。
* **规则与契约必须有测试覆盖**：Session 生命周期、Start/End 防抖与无效点击判断、自检互斥、导出聚合、MES 预留调用点与 MesMode 分支等，均需有对应单元测试或可验证的集成点。
* **流程**：针对每个交付物 → 先写 failing 测试或明确验收标准 → 再写实现 → 通过后进入下一项。
* **参照文档**：`docs/03_Dev_Rules_TDD_and_AI.md`（TDD 原则、AI 协作规则、提交纪律）。

### 1.2 架构与分层

* **MVVM + Service 分层**：View 不含业务逻辑；ViewModel 不直接访问 IO/协议/线程；状态由 Service 维护。
* **业务规则可单元测试**：Domain/Service 层逻辑必须可测，外部依赖（DB、文件、MES）通过接口或 Mock 隔离。
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

执行阶段 2 前，**必须**在下列文档中确认范围与契约，避免越界或遗漏：

| 文档 | 必读章节 | 用途 |
|------|----------|------|
| `docs/Architecture/Phase2.5_Stage_Plan.md` | **§三 阶段 2**（3.1 目标、3.2 交付物、3.3 不做/后置、3.4 风险与依赖） | 本阶段边界与交付物 |
| `docs/Architecture/Phase2.5_Technical_Refactor_Checklist.md` | **§三 服务层与编排**（Batch 退场、会话与记录服务、检验流程与 MES 插槽）、**§四 UI 与交互** 中与 Start/End、自检、导出相关的表述 | 编排契约与 MES 插槽 |
| `docs/Architecture/Phase2.5_Stage1_Executable_Prompt.md` | §三 目标、§四 交付物、§六 验收 | 阶段 1 已交付前提（按 Session 导出 API、SessionId 规则、表结构） |
| `docs/Architecture/MES_Plugin_Gate_Design_Freeze.md` | §10 Phase 2.5 补充决策、§11 杰科协议待确认点（本阶段不实现） | MES 预留形态与不做边界 |
| `docs/03_Dev_Rules_TDD_and_AI.md` | 全文 | TDD 与 AI 协作 |
| `docs/07_Technical_Architecture_and_Dev_Guide.md` | §2 分层、§3 核心架构原则 | 架构约束 |
| `docs/02_Architecture_Guardrails.md` | 全文 | 红线条款 |

---

## 三、本阶段目标（一句话）

**完成 Batch 退场，统一为 Order/Session/TestRecord；落成 Session 生命周期、Start/End 防抖与状态栏提示、自检期间不允许 Start/End 且禁用扫码与自检按钮；由 B 做导出聚合并暴露给 ViewModel；在编排内预留 MES 调用点与 MesMode 分支（接口与枚举在本阶段定义，实现用 null/NoOp，阶段 4 再实现）。**

---

## 四、交付物清单（按 TDD/实施顺序建议）

1. **Batch 退场**
   * 全面移除 Batch 概念，ProcessCoordinator、Storage、ViewModel、UI 文案均以 OrderId / SessionId 为入口。
   * **TDD/验收**：原有 Batch 路径的单元测试改为基于 SessionId/OrderId；或新增 Session 路径测试并逐步替换调用方。

2. **Session 生命周期服务**
   * 创建 Session、结束 Session、当前 Session 查询；与 Start/End 按钮逻辑挂接。
   * SessionId 只通过 `SessionIdGenerator.Format(OrderId, DateTime)` 生成，禁止手写或拼接。
   * **TDD**：先写「创建/结束/当前 Session」的测试或集成验证点，再实现服务。

3. **ProcessCoordinator 以 SessionId/OrderId 为入口**
   * 内部在「每笔 SN 前」留 Pre-Gate 调用点，在「结果落库后」留 Post-Report 调用点；根据 MesMode（Disabled/Enabled）决定是否调用、是否阻断（Phase 2.5 无 Strict，仅分支预留）。
   * **TDD**：验证入口为 SessionId/OrderId，且 Pre/Post 调用点存在且可由 NoOp/null 占位。

4. **Start/End 防抖与无效点击**
   * 结合时间窗口 + 是否生成 TestRecord 判断重复/无效点击；状态栏提示「本次操作无效 / 已忽略」，不写入错误提示区。
   * **TDD**：覆盖重复 Start/End、未生成 TestRecord 即 End 等边界，验证状态栏语义。

5. **自检与主流程互斥**
   * 自检期间**不允许 Start/End**；禁用自检按钮直至空闲；**禁止扫描 SN**（与 ScanInputService 或 UI 禁用扫码框一致）；**禁用 Start/End 按钮**。
   * **TDD**：覆盖自检期间 Start/End/扫码 均被拒绝或禁用。

6. **导出聚合服务（B 做）**
   * 按 OrderId/ProjectId 查 Session 列表，逐 Session 调用阶段 1 的「按 Session 导出」API，并暴露给 ViewModel；覆盖确认逻辑可由调用方（C）或本服务参数控制。
   * **TDD**：验证「按 OrderId/ProjectId 聚合 → 逐 Session 调用 ExportBySessionAsync」逻辑正确。

7. **MES 预留**
   * 在阶段 1 末尾或本阶段开头定义 `IMesPreCheck` / `IMesResultReporter` / `MesContext` / `TestResultContext` / `MesCapabilities` / `MesMode`；ProcessCoordinator 内以 **NoOp 或 null** 调用 Pre-Gate / Post-Report，保证无 MES 时行为与现有一致。
   * **TDD**：验证调用点存在、MesMode 分支可走，且无 MES 时行为不变。

---

## 五、本阶段不做 / 后置

* **真实 MES 协议与 Stub/NoOp 的可配置注入**：留在阶段 4。
* **校验弹窗的 UI 挂接**、**导出按钮与「选维度→选对象→执行」的界面交互**：留给阶段 3（C）。

---

## 六、验收标准（M2）

满足以下即可视为阶段 2 完成：

* **Batch 退场**：ProcessCoordinator、Storage、ViewModel 以 OrderId/SessionId 为入口，无 Batch 术语残留（或仅兼容层显式标记为废弃）。
* **Start/End 防抖与状态栏提示**：重复/无效点击在状态栏有提示，逻辑有测试或可演示验证。
* **Session 生命周期正确**：创建/结束/当前 Session 与 Start/End 按钮一致，有测试或集成验证。
* **自检与扫码互斥**：自检期间禁用 Start/End、扫码、自检按钮，行为可验证。
* **导出聚合由 B 提供**：按 OrderId/ProjectId 聚合 Session 并调用阶段 1 导出 API，接口可被 ViewModel 调用。
* **ProcessCoordinator 内可见 Pre/Post 调用点与 MesMode 分支**：接口与枚举已定义，编排内以 null/NoOp 调用，无 MES 时行为与现有一致。

---

## 七、执行时可直接复制的一段 Prompt（供 Agent 使用）

```
请实现 Phase 2.5 阶段 2 的目标，严格遵循以下约束：

【开发原则】
- 严格 TDD：先写测试再写实现；Session 生命周期、Start/End 防抖、自检互斥、导出聚合、MES 预留均需有可验证的测试或验收点。
- 遵守 docs/03_Dev_Rules_TDD_and_AI.md、docs/07_Technical_Architecture_and_Dev_Guide.md、docs/02_Architecture_Guardrails.md 中的架构与协作规则。

【范围与契约】
- 必读：docs/Architecture/Phase2.5_Stage_Plan.md §三（阶段 2）、docs/Architecture/Phase2.5_Technical_Refactor_Checklist.md §三与§四（服务层、Start/End/自检/导出）。
- 前置：阶段 1 已完成（Order/TestSession/TestRecord、SessionId、ExportBySessionAsync、IOrderNameValidator 已存在）。
- 交付：Batch 退场（OrderId/SessionId 为入口）；Session 生命周期；ProcessCoordinator Pre/Post 与 MesMode 预留；Start/End 防抖与状态栏提示；自检期间禁止 Start/End 与扫码；导出聚合服务由 B 做并暴露给 ViewModel；MES 接口与枚举定义、编排内 null/NoOp 调用。
- 不做：真实 MES 实现与可配置注入、校验弹窗 UI、导出「选维度→选对象→执行」的界面交互（留阶段 3）。

【验收】
- Batch 退场；Start/End 防抖与状态栏提示；Session 生命周期正确；自检与扫码互斥；导出聚合由 B 提供；ProcessCoordinator 内可见 Pre/Post 与 MesMode 分支。
```

---

**文档版本**  
* 依据：Phase2.5_Stage_Plan.md §三、Phase2.5_Technical_Refactor_Checklist.md §三/§四、MES_Plugin_Gate_Design_Freeze.md §10、Stage1_Executable_Prompt、03/07/02 开发文档。  
* 若阶段计划或清单有修订，请同步更新本文档「必读文档与章节」「交付物」「不做/后置」及 §七 的复制段。
