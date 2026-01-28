# Phase 2.5 整体技术改造清单

本文档整合「数据结构 + 服务接口 + UI 变更点」及已拍板决策，供审阅与实施对照。实施时须同时遵守：

* `docs/Architecture/SnVerify_Phase2.5_UI_Data_Confirmation.md`（UI 与数据逻辑）
* `docs/Architecture/MES_Plugin_Gate_Design_Freeze.md`（MES 抽象与闸口）
* `docs/ui/SnVerify_UI_Design_Constraints.md`（工业上位机 UI 约束）

---

## 一、数据模型与数据库

### 1.1 核心模型层级

```
Project
 └─ Order
     └─ TestSession
         └─ TestRecord（SN 粒度）
```

* **Order**：订单级，每个订单绑定一个 Project，绑定后不可改。
* **TestSession**：运行级，一次「开始 → 测试 → 停止」的独立会话；同订单可多 Session、可跨天。
* **TestRecord**：不冗余 Project/Order，通过 SessionId 关联 Session。

### 1.2 表与唯一性

| 表 | 说明 | 唯一性 |
|----|------|--------|
| **Order** | OrderId (PK), OrderName, ProjectId, 等 | OrderName 符合命名规则 |
| **TestSession** | SessionId (PK), OrderId (FK), StartTime, EndTime, Status；可冗余 OrderName | SessionId 应用层 + 建议 DB 唯一索引 |
| **TestRecord** | 现有字段 + SessionId (FK)，不再以 BatchId 为主 | 按 Session 内业务需求 |

* **SessionId 规则**：`OrderId + "_" + yyyyMMdd_HHmmss`，应用层保证同一 Order 同一秒不重复；建议数据库对 SessionId 建唯一索引。
* **迁移**：版本未发布，可推倒重来，无需兼容旧 Batch 数据。

### 1.3 命名与校验

* **ProjectName / OrderName**：禁止文件系统特殊字符，长度上限 64，不允许中文。
* **校验时机**：开始测试时一次性校验；不通过则**弹窗提示**，不创建 Session。

---

## 二、导出与日志

### 2.1 导出粒度与 UI 流程

* **维度**：支持**按项目导出**、**按订单导出**。
* **UI 流程（保持界面简洁）**：
  1. 用户点击「导出」
  2. 选择「按项目导出」或「按订单导出」
  3. 再选择具体项目 或 具体订单
  4. 执行导出；若目标文件已存在则**弹窗确认覆盖或取消**。

### 2.2 文件与目录

* **导出文件**：每个 Session 单独文件。  
  * 命名示例：`OrderName_SessionId.xlsx`、`OrderName_SessionId.txt`。  
  * 未结束的 Session（EndTime = null）导出时标记为「未完成 / 异常」。
* **PASS Sheet**：当前导出范围内所有 PASS 的 TestRecord，从库查出后**原样写入**，不去重、不额外过滤；检验规则与现有一致。
* **FAIL Sheet**：导出时按 `(StickerSN, DeviceSN)` 去重，**保留第一条出现记录**；库内保留完整历史。
* **日志目录**：当前运行程序目录下的 `logs`。先不做自动清理，后续依压力测试再定。

### 2.3 与约束文档的差异说明

* 原 UI 确认清单中「导出文件覆盖同名文件，不生成额外后缀或提示」已由后续拍板**覆盖**为：**覆盖前弹窗确认或取消**。

---

## 三、服务层与编排

### 3.1 批次（Batch）退场

* 全面移除 Batch 概念，统一为 Order + TestSession + TestRecord。
* ProcessCoordinator、Storage、ViewModel、UI 文案均以 OrderId / SessionId 为入口。

### 3.2 会话与记录服务

* **Order / Session 管理**：创建 Order、创建 Session、结束 Session、按 Order/Session 查询。
* **TestRecord**：按 SessionId 写入与查询；导出时由调用方按 Session 或按项目/订单聚合后调用导出 API。

### 3.3 检验流程与 MES 插槽

* **检验链路**（不可被 MES 侵入）：Scan SN → Read Device SN → Verify（本站规则）→ Result。
* **MES 仅存在于 Gate 层**：
  * **Pre-Gate**：每笔 SN 前调用一次 `IMesPreCheck.CheckAsync(context)`；MesMode=Disabled 时不调。
  * **Post-Report**：在结果落库/UI 更新后，异步调用 `IMesResultReporter.ReportTestResultAsync(context)`；失败只记日志并触发 UI 提示，不反写结果、不阻断下一笔。
* **MesMode**：Disabled（完全不启用）/ Enabled（启用但失败不阻断）。Phase 2.5 **不允许 Strict**。
* **Post-Report 失败 UI**：抽象层预留「上报失败」事件或回调，由 UI（如状态栏或固定小字区）订阅并展示「MES 上报失败」等简短文案。

---

## 四、UI 与交互

### 4.1 校验与错误提示

* **输入校验**：开始测试时一次性校验（Project/Order 命名等）；不通过则**弹窗**提示，不创建 Session。
* **错误提示区**：仅用于**检验过程产生的错误**（ADB/设备/MES 等）；不用于 Start/End 无效操作提示。

### 4.2 按钮与状态

* **Start / End 重复点击**：「额外判断 TestRecord 是否实际生成」用于**状态栏**提示「本次操作无效 / 已忽略」；不写入错误提示区。
* **自检**：自检期间**禁用自检按钮**直至空闲；**禁止扫描 SN**（UI 层禁用扫码输入框）。
* **导出**：见 §2.1；覆盖时弹窗确认或取消。

### 4.3 日志与记录展示

* **日志区**：默认折叠，展开后显示**最近 3000 条**；不接受明显卡顿，压力测试不过再考虑分页或更小上限。
* **测试记录区**：当前 Session 记录，默认隐藏，展开后展示；条数上限策略同日志（当前 3k，后续可调）。
* **重复扫描「设备 SN 已存在」**：**UI 错误提示区仅展示一条**（刷新即可）；**日志每次保留**。

### 4.4 ADB / MES 文案与 Sheet

* **ADB**：沿用现有错误文案。
* **MES**：文案可简短；MES 异常时**不写入 PASS/FAIL Sheet**，仅日志 + UI 提示；MES 与 ADB 结果的上传关系后续单独梳理。

### 4.5 与约束文档的差异说明

* 原 UI 确认清单中「最近 1000 条」「10k+ 条」等，已统一为**最近 3000 条**为当前设计，压力测试后再优化。
* 原「MES 接口异常 … 同时记录 … FAIL Sheet」已明确为：**MES 异常不写 FAIL Sheet**，仅日志与 UI。

---

## 五、MES 抽象层（Phase 2.5 范围）

* **本阶段只做**：接口（IMesPreCheck、IMesResultReporter、MesContext、TestResultContext、MesCapabilities、IMesPlugin）+ Stub/NoOp 实现 + 现有上传逻辑收口为 ResultReporter 的 Adapter。
* **本阶段不做**：杰科真实协议实现；杰科接入与协议细节见 `MES_Plugin_Gate_Design_Freeze.md` §11「杰科协议待确认点」，下阶段对接时再填。

---

## 六、约束文档同步建议

实施前建议对以下文档做**审阅与必要修订**，使与上述清单一致。

### 6.1 SnVerify_Phase2.5_UI_Data_Confirmation.md

| 条目 | 当前表述 | 建议修订 |
|------|----------|----------|
| 6 | 导出文件覆盖同名文件，不生成额外后缀或提示操作员 | 改为：导出时若目标文件已存在，**弹窗由操作员选择覆盖或取消** |
| 14 | 打开后显示最近 1000 条 | 改为：打开后显示**最近 3000 条**，压力测试后再优化 |
| 27 | MES 接口异常在界面错误提示区显示，同时记录日志和 FAIL Sheet | 改为：MES 异常在界面提示并记录日志，**不写入 PASS/FAIL Sheet** |
| 32 | 10k+ 条时仍显示全部… | 改为：当前设计**最近 3000 条**，压力测试后再优化 |

其余条目与本文档及前期拍板一致，可保留；若希望将「导出按项目/按订单」「校验弹窗」「Start/End 状态栏提示」等显式化，可在该文档中新增 1～2 条简述。

### 6.2 MES_Plugin_Gate_Design_Freeze.md

* 已包含 Phase 2.5 补充决策（§10）与杰科待确认点（§11），**无需结构性增改**。
* 实施时以 §10（MesMode、Pre-Gate 每笔 SN、Post-Report 失败 UI）为为准即可。

### 6.3 其他约束文档

* **SnVerify_UI_Design_Constraints.md**：现有工业上位机约束（布局、颜色、字号、无动画等）继续生效，本文档未与之冲突。
* **是否需要新增**：若希望把「Phase 2.5 技术改造清单」本身列为 Cursor Agent 的必读输入，可在各相关文档的「参考文档」处增加对本文件的引用；不强制新增单独约束文档。

---

## 七、实施顺序与分阶段计划

详细阶段划分、交付物、依赖与可讨论点见：**`docs/Architecture/Phase2.5_Stage_Plan.md`**。

**阶段 1 可执行 Prompt**（开发原则、TDD、必读文档、交付物与验收标准）见：**`docs/Architecture/Phase2.5_Stage1_Executable_Prompt.md`**。Agent 执行阶段 1 时请以该文档为准，严格遵循其中 TDD 与架构约束。

**阶段 2 可执行 Prompt**（开发原则、必读文档、交付物与验收标准）见：**`docs/Architecture/Phase2.5_Stage2_Executable_Prompt.md`**。前置条件为阶段 1 已完成；执行阶段 2 时请以该文档为准，严格遵循其中 TDD 与架构约束。

阶段顺序概要：

1. **阶段 1（A）**：模型与概念重构 — Order / TestSession / TestRecord 表结构、SessionId 规则、按 Session 导出及 PASS/FAIL 去重规则。
2. **阶段 2（B）**：流程编排重构 — Start/End 防抖、Session 生命周期、自检与主流程互斥、ScanInputService 行为；**在本阶段预留 MES 调用点与 MesMode 分支**，不实现 MES。
3. **阶段 3（C）**：UI 行为与布局 — 订单/测试文案与导出流程、校验与覆盖确认弹窗、状态栏与错误区语义、拟物化与折叠、日志/记录 3k 条与重复提示收敛。
4. **阶段 4（D）**：MES 抽象与 Gate — 实现 Pre-Gate/Post-Report 接口与 Stub/NoOp、现有实现收口为 Adapter、Post-Report 失败 UI 事件。

以上 5 点（导出聚合 B 做、自检期间不允许 Start/End、文案仅不出现 SessionId/Session、C 可拆 C1/C2、MES 预留直接采纳建议）已在 `Phase2.5_Stage_Plan.md` **§七 已拍板结论**中确定，执行时以该结论为准。

---

**文档版本与依赖**  

* 本文档依赖并汇总自：Phase 2.5 讨论结论、SnVerify_Phase2.5_UI_Data_Confirmation.md、MES_Plugin_Gate_Design_Freeze.md。
* 若后续拍板与上述任一文档冲突，以最新拍板为准，并同步修订对应约束文档与本文档。
