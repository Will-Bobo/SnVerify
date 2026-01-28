# Phase 2.5 分阶段实施计划

本文档在「四类工作 A/B/C/D」基础上，给出阶段顺序、各阶段目标与交付物、依赖与风险。**§七 已拍板结论**中的 5 点已确定，作为执行依据；后续若调整须同步修订各阶段描述。

---

## 一、四类工作与阶段映射

| 类别 | 范围概要 | 阶段归属 | 说明 |
|------|----------|----------|------|
| **A. 模型与概念重构（地基）** | Batch → Order/Project/Session；表结构推倒；导出规则重写；去重规则冻结 | **阶段 1** | 不可并行，后续都依赖 |
| **B. 流程编排重构（中枢）** | Start/End 防抖；Session 生命周期；自检与主流程互斥；ScanInputService 行为一致 | **阶段 2** | 高度耦合，建议单段连续完成；MES **调用点与 MesMode 分支**在本阶段预留 |
| **C. UI 行为与布局（表现层）** | PASS/FAIL 拟物化；日志折叠；状态栏语义；对外仅用「本次测试」「当前订单」，不出现 SessionId/Session | **阶段 3**，可拆 C1/C2 | 强依赖 A+B 的结果 |
| **D. MES 抽象与 Gate（接口层）** | Pre-Gate/Post-Report 接口；Capability 声明；Stub/NoOp；现有实现收口为 Adapter | **阶段 4** | 可后置，接口在阶段 2 预留 |

---

## 二、阶段 1：模型与概念重构（A）

### 2.1 目标

* 用 Order / TestSession / TestRecord 替代 Batch 概念，数据结构推倒重来。
* 导出规则按「每 Session 一文件、PASS 原样/FAIL 去重」落地并冻结。
* 为后续流程编排与 UI 提供**稳定数据契约**（OrderId、SessionId、命名规则、去重规则）。

### 2.2 交付物

* 表结构：Order、TestSession、TestRecord（含 SessionId 外键与唯一约束）。
* SessionId 规则：`OrderId + "_" + yyyyMMdd_HHmmss`，应用层 + 建议 DB 唯一索引。
* 导出能力：**按 Session 导出**（单 Session → xlsx 双 Sheet + txt）；PASS 不去重、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条。
* 命名与校验：ProjectName/OrderName 规则；开始测试时一次性校验（弹窗），本阶段可只做领域/服务内校验接口，弹窗由阶段 3 挂接。

### 2.3 不做 / 后置

* 按项目/按订单的**聚合与导出入口**放到阶段 2 或 3（业务层聚合 Session 再调「按 Session 导出」）。
* UI、ProcessCoordinator 仍可暂时沿用 Batch 术语与调用，在阶段 2 再切到 Order/Session。
* MES 不涉及。

### 2.4 风险与依赖

* 无前置依赖。完成后，B/C 均依赖本阶段的「表 + 导出 API + 命名规则」。

### 2.5 可执行 Prompt

* 阶段 1 的**可执行 Prompt**（含开发原则、必读文档、交付物与验收标准）见：**`docs/Architecture/Phase2.5_Stage1_Executable_Prompt.md`**。执行时严格遵循其中 TDD 与架构约束。

---

## 三、阶段 2：流程编排重构（B）

### 3.1 目标

* Start/End 防抖与无效点击提示（状态栏），Session 生命周期与状态一致。
* 自检与主流程互斥：自检期间**不允许 Start/End**，且禁用扫码、禁用自检按钮直至空闲。
* **导出聚合由 B 负责**：按 OrderId/ProjectId 查 Session 列表 + 逐 Session 调用阶段 1 的导出 API，并暴露给 ViewModel；C 只做「导出按钮 → 选维度 → 选对象 → 调该服务 + 覆盖确认弹窗」。
* ScanInputService 与「当前 Session」语义一致（输入触发检验时使用当前 SessionId）。
* **在本阶段为 MES 预留调用点与 MesMode 分支**：接口与枚举定义提前到阶段 1 末尾或本阶段开头，ProcessCoordinator 内留调用点与传参形态（MesContext/TestResultContext），实现用 null 或 NoOp；阶段 4 再实现接口体与注入。

### 3.2 交付物

* ProcessCoordinator（或等价编排）以 **SessionId/OrderId** 为入口；内部在「每笔 SN 前」留 Pre-Gate 调用点，在「结果落库后」留 Post-Report 调用点；根据 **MesMode**（Disabled/Enabled）决定是否调用、是否阻断（Phase 2.5 无 Strict，仅分支预留）。
* Session 生命周期服务：创建 Session、结束 Session、当前 Session 查询；与 Start/End 按钮逻辑挂接。
* **导出聚合服务**：按 OrderId/ProjectId 查 Session 列表，逐 Session 调用阶段 1 的「按 Session 导出」API，暴露给 ViewModel；覆盖确认逻辑可由调用方（C）或本服务参数控制。
* Start/End 防抖与「额外判断 TestRecord 是否实际生成」→ 状态栏提示无效点击。
* **自检期间**：禁用自检按钮直至空闲；**禁止扫描 SN**（与 ScanInputService 或 UI 禁用扫码框一致）；**禁用 Start/End 按钮**（不允许开始/结束 Session）。
* MES 预留：在阶段 1 末尾或本阶段开头定义 `IMesPreCheck` / `IMesResultReporter` / `MesContext` / `TestResultContext` / `MesCapabilities` / `MesMode`；ProcessCoordinator 内以 **NoOp 或 null** 调用 Pre-Gate / Post-Report，保证无 MES 时行为与现有一致。

### 3.3 不做 / 后置

* 不实现真实 MES 协议与 Stub/NoOp 的**可配置注入**（留在阶段 4）。
* 校验弹窗的 UI 挂接、导出按钮与「选维度→选对象→执行」的界面交互，留给阶段 3（C）。

### 3.4 风险与依赖

* 强依赖阶段 1 的 Order/Session/TestRecord 与导出 API。
* 本阶段建议在 Cursor 内**连续上下文**完成，减少接口反复。

---

## 四、阶段 3：UI 行为与布局（C）

### 4.1 目标

* **文案与绑定**：Batch → Order + Session；对外**允许**「本次测试」「当前订单」等说法，**仅不出现**「SessionId」「Session」字样。
* 导出流程：「导出 → 选按项目/按订单 → 选具体项目或订单 → 执行」；覆盖时弹窗确认或取消；聚合逻辑由 B 提供，C 只做交互与调 B 的导出服务。
* 校验弹窗、状态栏无效操作提示、MES 上报失败提示（后者在阶段 4 接事件，本阶段可先预留展示位或占位文案）。
* 自检期间：扫码框禁用；**Start/End 按钮禁用**（与 B 一致）；自检按钮在 B 中已禁，本阶段保证 UI 与之一致。
* 日志/记录区最近 3k 条；重复「设备 SN 已存在」UI 只一条、日志每次保留。
* PASS/FAIL 拟物化突出、日志区域默认折叠、状态栏语义收敛。

### 4.2 交付物

* 主界面：订单/测试相关控件与绑定（文案用「本次测试」「当前订单」等）；导出按钮及「选维度 → 选对象 → 执行」流程；覆盖确认弹窗；校验弹窗挂接。
* 检验区：PASS/FAIL 拟物化（C2）；错误区仅展示检验错误；状态栏展示当前 Order、Processing、最近结果等。
* 日志/记录区：默认折叠，展开后最近 3000 条；重复扫描时错误区只一条提示。
* 自检期间：扫码框禁用；Start/End 按钮禁用；自检按钮禁用（与 B 行为一致）。

### 4.3 不做 / 后置

* MES 上报失败的**真实事件订阅**与文案，可在阶段 4 接好后，再在 C 中接 UI 展示（若 C 先做，可占位“MES 上报失败”）。

### 4.4 已采纳：C 拆为 C1 / C2

* **C1**：订单/测试文案、导出流程、状态栏、校验弹窗、自检期间禁用扫码与 Start/End、日志 3k 与重复提示收敛。  
* **C2**：拟物化、折叠、颜色与布局微调。  
* 执行顺序：先交付 C1，再交付 C2；若时间允许也可同一批次内先后完成。

### 4.5 风险与依赖

* 强依赖阶段 1 的数据语义与阶段 2 的 Session 生命周期、导出服务、MES 调用点预留。

---

## 五、阶段 4：MES 抽象与 Gate（D）

### 5.1 目标

* 实现并注入 MES 抽象层：Pre-Gate / Post-Report 接口体、MesContext/TestResultContext、MesCapabilities、MesMode。
* Stub/NoOp 实现；将现有上传逻辑收口为 `IMesResultReporter` 的 Adapter（若有保留价值）。
* 在 ProcessCoordinator 中把阶段 2 预留的调用点接上真实接口与 MesMode 分支；Post-Report 失败时触发「上报失败」事件，供 UI 展示（状态栏或固定小字区）。

### 5.2 交付物

* 抽象接口与 DTO：如 `IMesPreCheck`、`IMesResultReporter`、`MesContext`、`TestResultContext`、`MesCapabilities`、`MesMode` 枚举。
* 实现：NoOpMesPlugin（或等价 Stub）；可选：现有 MES 上传收口为 JieKeResultReporterAdapter（仅占位，协议下阶段填）。
* 编排侧：ProcessCoordinator 在每笔 SN 前调 PreCheck（MesMode≠Disabled 且插件支持时），在结果后调 Post-Report；MesMode=Disabled 不调；Phase 2.5 无 Strict，仅分支预留。
* 「上报失败」事件/回调，ViewModel 或 UI 订阅后展示“MES 上报失败”等简短文案。

### 5.3 不做 / 后置

* 杰科真实协议、URL/鉴权/报文格式等，下阶段再做；本阶段仅把「接口 + Stub + 事件」打通。

### 5.4 风险与依赖

* 依赖阶段 2 已预留的调用点与 MesMode 分支；若阶段 2 未留插槽，本阶段需回溯到 ProcessCoordinator 补调用点。

---

## 六、整体顺序与里程碑

```
阶段 1（A）→ 阶段 2（B，含 MES 插槽）→ 阶段 3（C）→ 阶段 4（D）
```

| 里程碑 | 内容 | 可验收标准 |
|--------|------|------------|
| M1 | 阶段 1 完成 | 新表可用；按 Session 导出可运行；PASS/FAIL 规则符合清单 |
| M2 | 阶段 2 完成 | Start/End 防抖与状态栏提示；Session 生命周期正确；自检与扫码互斥；ProcessCoordinator 内可见 Pre/Post 调用点与 MesMode 分支 |
| M3 | 阶段 3 完成 | 导出流程「选维度→选对象→执行」可用；覆盖确认弹窗；校验弹窗；日志/记录 3k、重复提示收敛；拟物化与折叠按 UI 约束落地 |
| M4 | 阶段 4 完成 | MES 抽象可配置注入；NoOp/Stub 行为正确；Post-Report 失败有 UI 提示；无 MES 时行为与 M3 一致 |

---

## 七、已拍板结论（5 点）

以下 5 点已确定，已同步进各阶段描述；后续若调整须同步修订本文档对应段落。

| # | 事项 | 结论 |
|---|------|------|
| 1 | **导出聚合归属** | **B 做**。B 负责「按 OrderId/ProjectId 查 Session 列表 + 逐 Session 调用阶段 1 的导出 API」，并暴露给 ViewModel；C 只做「导出按钮 → 选维度 → 选对象 → 调该服务 + 覆盖确认弹窗」。 |
| 2 | **自检期间 Start/End** | **自检期间不允许 Start/End**。B 中约束自检期间禁用 Start/End 按钮逻辑，C 中保证该期间 Start/End 按钮禁用。 |
| 3 | **“不显示 Session”的边界** | **允许**出现「本次测试」「当前订单」等，**仅不出现**「SessionId」「Session」。 |
| 4 | **C 是否拆 C1/C2** | **C 可拆为 C1/C2**。先 C1（功能与语义），后 C2（拟物化、折叠与布局微调）。 |
| 5 | **阶段 2 的“MES 预留”粒度** | **直接采纳建议**：B 里留调用点 + 传参形态（MesContext/TestResultContext）；**接口与枚举定义**提前到阶段 1 末尾或阶段 2 开头，实现用 null/NoOp，阶段 4 再实现接口体与注入。 |
