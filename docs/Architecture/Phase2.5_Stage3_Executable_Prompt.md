# Phase 2.5 阶段 3 可执行 Prompt

> 本文档供 Cursor Agent 或人工执行阶段 3（UI 行为与布局，C）时**复制进对话**使用。  
> **前置条件**：阶段 1、阶段 2 已完成（Order/TestSession/TestRecord、Session 生命周期、导出聚合、MES 预留等已落地，单元测试已通过人工 review）；Step 2 自检已执行，规则 3、5 已同意留后续阶段处理。  
> 执行前请先阅读本文档中的「开发原则」与「必读文档」，再按「本阶段目标与交付物」实施。

---

## 一、开发原则（必须遵守）

执行本阶段时，**严格参照下列开发文档**，不得省略或越过。

### 1.1 TDD（测试驱动开发）

* **严格遵循 TDD 开发模式**：先写测试或明确验收标准，再写实现。
* **UI 与交互逻辑须可验证**：导出流程、校验弹窗挂接、状态栏语义、自检期间按钮/扫码禁用等，均需有单元测试、集成验证或可演示验收。
* **流程**：针对每个交付物（C1 优先）→ 明确验收标准或写测试 → 再写实现 → 通过后进入下一项。
* **参照文档**：`docs/03_Dev_Rules_TDD_and_AI.md`（TDD 原则、AI 协作规则、提交纪律）。

### 1.2 架构与分层

* **MVVM + Service 分层**：View 不含业务逻辑；ViewModel 不直接访问 IO/协议/线程；状态由 Service 维护；C 只做「导出按钮 → 选维度 → 选对象 → 调 B 的导出服务 + 覆盖确认弹窗」。
* **参照文档**：`docs/07_Technical_Architecture_and_Dev_Guide.md`（分层结构、状态 vs 事件、Snapshot 模型）。

### 1.3 架构红线

* Domain 层不得依赖 WPF、UI、硬件、MES。
* 不允许在 UI 层直接写业务逻辑。
* 不允许为“方便”跳过文档约束。
* **参照文档**：`docs/02_Architecture_Guardrails.md`。

### 1.4 AI 协作与可维护性

* 不新增文档未定义的功能；不重写架构分层。
* 新生成的 public/internal 方法需 XML 注释（summary/param/return）；新生成文件建议加 `author/remarks` 标识。
* **参照文档**：`docs/03_Dev_Rules_TDD_and_AI.md` 第三、四节。

---

## 二、必读文档与章节

执行阶段 3 前，**必须**在下列文档中确认范围与契约，避免越界或遗漏：

| 文档 | 必读章节 | 用途 |
|------|----------|------|
| `docs/Architecture/Phase2.5_Stage_Plan.md` | **§四 阶段 3**（4.1 目标、4.2 交付物、4.3 不做/后置、4.4 C1/C2 拆分、4.5 风险与依赖） | 本阶段边界与交付物 |
| `docs/Architecture/Phase2.5_Technical_Refactor_Checklist.md` | **§二 导出与日志**（2.1 导出粒度与 UI 流程）、**§四 UI 与交互**（4.1 校验与错误提示、4.2 按钮与状态、4.3 日志与记录展示） | 导出流程、校验弹窗、状态栏、日志 3k、重复提示 |
| `docs/Architecture/Phase2.5_Stage2_Executable_Prompt.md` | §三 目标、§四 交付物、§六 验收 | 阶段 2 已交付前提（Session 生命周期、导出聚合、自检互斥等） |
| `docs/Architecture/Phase2.5_Step2_SelfCheck_Result.md` | 自检结论汇总、TODO 归属 | Step 2 自检结论与规则 3、5 后续处理约定 |
| `docs/03_Dev_Rules_TDD_and_AI.md` | 全文 | TDD 与 AI 协作 |
| `docs/07_Technical_Architecture_and_Dev_Guide.md` | §2 分层、§3 核心架构原则 | 架构约束 |
| `docs/02_Architecture_Guardrails.md` | 全文 | 红线条款 |

---

## 三、本阶段目标（一句话）

**完成 UI 行为与布局（C）：文案与绑定改为「本次测试」「当前订单」（不出现 SessionId/Session）；导出流程「选维度 → 选对象 → 执行」+ 覆盖确认弹窗；校验弹窗挂接；状态栏无效操作提示与 MES 上报失败预留；自检期间扫码/Start/End 禁用与 B 一致；日志/记录区最近 3k 条、重复「设备 SN 已存在」UI 只一条；PASS/FAIL 拟物化、日志默认折叠、状态栏语义收敛。**

---

## 四、交付物清单（建议先 C1 后 C2）

### C1：功能与语义（建议先做）

1. **主界面文案与绑定**
   * 订单/测试相关控件与绑定：对外**允许**「本次测试」「当前订单」等说法，**仅不出现**「SessionId」「Session」字样。
   * 若阶段 2 已切 Session，则 ViewModel/绑定以 OrderId、当前 Session 语义为准；若仍为 Batch 过渡，则本阶段可先改文案与占位，待 B 完全退场后对齐。

2. **导出流程与覆盖确认弹窗**
   * 导出按钮 →「选维度（按项目 / 按订单）」→「选对象（具体项目或订单）」→ 执行；调用 B 提供的导出聚合服务（`IExportAggregationService`），C 不实现聚合逻辑。
   * 若目标文件已存在：**弹窗确认覆盖或取消**，不静默覆盖。

3. **校验弹窗挂接**
   * 开始测试时一次性校验（Project/Order 命名等）：不通过则**弹窗**提示，不创建 Session；挂接 `IOrderNameValidator` 或等价校验接口。

4. **状态栏与无效操作提示**
   * 状态栏展示：当前订单/本次测试、Processing、最近结果等。
   * 无效操作（如重复 Start/End、未生成 TestRecord 即 End）：在**状态栏**提示「本次操作无效 / 已忽略」（规则 3 若在本阶段补齐则实现，否则预留展示位）。
   * **MES 上报失败**：本阶段可**预留展示位或占位文案**（如“MES 上报失败”）；真实事件订阅留阶段 4。

5. **自检期间 UI 与 B 一致**
   * 自检期间：扫码框禁用；**Start/End 按钮禁用**；自检按钮禁用（B 中已禁，本阶段保证 UI 与之一致）。

6. **日志/记录区与重复提示**
   * 日志区：默认折叠，展开后**最近 3000 条**；不影响测试流程；日志信息正确绑定 Session/Order/Project（若已切 Session）。
   * 重复扫描「设备 SN 已存在」：**UI 错误提示区仅展示一条**（刷新即可）；日志每次保留。

### C2：拟物化与布局（可后做）

7. **PASS/FAIL 拟物化**
   * 检验区：PASS/FAIL 拟物化突出；错误区仅展示检验错误。

8. **折叠与状态栏收敛**
   * 日志区域默认折叠；状态栏语义收敛（当前 Order、Processing、最近结果等）。

---

## 五、本阶段不做 / 后置

* **MES 上报失败的真实事件订阅与文案**：可在阶段 4 接好后，再在 C 中接 UI 展示；若本阶段先做，可占位“MES 上报失败”或预留展示位。
* **B 侧未完成的 Batch 退场 / Start/End 防抖（规则 3）**：若阶段 2 已同意留后续，则本阶段可先预留状态栏无效操作提示占位，待 B 补齐后再绑定。

---

## 六、验收标准（M3）

满足以下即可视为阶段 3 完成：

* **导出流程**：「选维度 → 选对象 → 执行」可用；覆盖时弹窗确认或取消；调用 B 的导出聚合服务。
* **主界面**：订单/测试相关控件与绑定使用「本次测试」「当前订单」等文案，不出现 SessionId/Session。
* **校验弹窗**：开始测试时校验挂接，不通过弹窗提示且不创建 Session。
* **状态栏**：展示当前 Order、Processing、最近结果等；无效操作/MES 上报失败可预留展示位或占位文案。
* **自检期间**：扫码框、Start/End 按钮、自检按钮禁用与 B 行为一致。
* **日志/记录区**：默认折叠，展开后最近 3000 条；重复「设备 SN 已存在」时 UI 错误区只一条。
* **C2**：PASS/FAIL 拟物化、日志默认折叠、状态栏语义收敛按 UI 约束落地。

---

## 七、执行时可直接复制的一段 Prompt（供 Agent 使用）

```
请实现 Phase 2.5 阶段 3（UI 行为与布局，C）的目标，严格遵循以下约束：

【开发原则】
- 严格 TDD 或先明确验收标准再实现；导出流程、校验弹窗、状态栏、自检期间禁用等须可验证或可演示。
- 遵守 docs/03_Dev_Rules_TDD_and_AI.md、docs/07_Technical_Architecture_and_Dev_Guide.md、docs/02_Architecture_Guardrails.md 中的架构与协作规则。

【范围与契约】
- 必读：docs/Architecture/Phase2.5_Stage_Plan.md §四（阶段 3）、docs/Architecture/Phase2.5_Technical_Refactor_Checklist.md §二与§四（导出、UI 与交互）。
- 前置：阶段 1、阶段 2 已完成（Session 生命周期、导出聚合、自检互斥等已落地，单元测试已人工 review）；Step 2 自检已执行，规则 3、5 已同意留后续处理。
- 交付：C1 优先——主界面文案「本次测试」「当前订单」、导出流程「选维度→选对象→执行」+ 覆盖确认弹窗、校验弹窗挂接、状态栏与无效操作/MES 预留、自检期间扫码/Start/End 禁用与 B 一致、日志 3k 与重复提示收敛；C2——PASS/FAIL 拟物化、日志默认折叠、状态栏语义收敛。
- 不做：MES 上报失败真实事件订阅（留阶段 4）；本阶段可占位或预留展示位。

【验收】
- 导出「选维度→选对象→执行」可用，覆盖确认弹窗；主界面文案不出现 SessionId/Session；校验弹窗挂接；状态栏与自检期间 UI 与 B 一致；日志 3k、重复提示收敛；C2 拟物化与折叠按 UI 约束落地。
```

---

**文档版本**  
* 依据：Phase2.5_Stage_Plan.md §四、Phase2.5_Technical_Refactor_Checklist.md §二/§四、Phase2.5_Step2_SelfCheck_Result.md、Stage2_Executable_Prompt、03/07/02 开发文档。  
* 若阶段计划或清单有修订，请同步更新本文档「必读文档与章节」「交付物」「不做/后置」及 §七 的复制段。
