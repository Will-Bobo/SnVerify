# Phase 2.5 Step 2 自检结果

> 依据：`docs/Architecture/Phase2.5_Step2_Cursor_SelfCheck_Prompt.md` 自检规则（12 条）。  
> 执行时间：自检执行时填写。

---

## 自检结论汇总

| 规则 | 结论 | 说明 |
|------|------|------|
| 1️⃣ MVVM 分层遵守 | ✅ 通过 | ViewModel 仅状态与命令，UI 仅绑定与焦点/展开；无直接调用 Domain/Service。 |
| 2️⃣ Session/Order/Project 不被修改 | ✅ 通过 | ViewModel 不直接修改 TestRecord/Session/Order，仅通过 Service 调用。 |
| 3️⃣ 开始/结束按钮防抖 | ❌ 不通过 | 未实现「未生成 TestRecord 即 End」的判定与状态栏「本次操作无效/已忽略」提示。已标 TODO。 |
| 4️⃣ 自动/手动检验按钮 | ✅ 通过 | IsProcessing 时忽略扫码；手动检验通过命令触发；未直接操作 ScanInputService。 |
| 5️⃣ 状态栏更新 | ⚠️ 部分 | 待测/测试中/完成有绑定；Post-Report 异常仅日志，未在状态栏弱提示。已标 TODO。 |
| 6️⃣ 错误提示区 | ✅ 通过 | PASS/FAIL 高亮；FAIL 轻量显示；MES 异常不写业务结果。 |
| 7️⃣ 日志区域 | ⚠️ 部分 | 默认折叠可展开；日志当前按批次维度，未按 Session 过滤；条数默认 100，文档建议 1000/3000 可后续调。 |
| 8️⃣ 防止并发与状态冲突 | ✅ 通过 | 自检期间已禁用「人工检验」与扫码输入框（IsScanInputEnabled、StartVerifyCommand.CanExecute）。 |
| 9️⃣ 单元测试覆盖 | ⚠️ 部分 | 有 MainViewModelTests；缺防抖逻辑、Post-Report 异常、自检互斥的专项测试。 |
| 🔟 容错行为 | ✅ 通过 | 错误/重复 SN 时 UI 不阻塞，记录一次，日志与事件一致。 |
| 11️⃣ UI 无侵入 Domain | ✅ 通过 | 业务逻辑在 Service/Gate，ViewModel 不修改领域实体。 |
| 12️⃣ 文档与注释 | ✅ 通过 | public 方法与类有 XML 注释；AI 生成处有 author/remarks。 |

---

## 不通过项与 TODO（须在代码中标注）

### 规则 3：开始/结束按钮防抖

- **要求**：重复点击 Start/End 时，若未生成过 TestRecord → 判定为重复点击，状态栏提示「本次操作无效/已忽略」，日志或事件记录被忽略操作。
- **现状**：End 仅根据 IsBatchActive 启用，未检查当前 Session 是否已有 TestRecord；无「本次操作无效」状态栏文案。
- **位置**：`MainViewModel` 中 `EndBatchAsync`（或后续 Session 语义的 EndSession）及状态栏绑定处。
- **TODO**：在 ViewModel 中实现「End 前检查当前 Session 是否有 TestRecord；无则状态栏提示『本次操作无效/已忽略』且不执行 End」。

### 规则 5：Post-Report 异常弱提示

- **要求**：Post-Report 异常在状态栏或小字区弱提示，不阻塞流程。
- **现状**：ProcessCoordinator 内 Post-Report 仅 `_loggingService?.LogInfo("MES Post-Report 失败: ...")`，无状态栏/UI 弱提示。
- **TODO**：在编排层增加「Post-Report 失败」事件或回调，由 ViewModel 订阅并在状态栏显示简短文案（如「MES 上报失败」）。

### 规则 8：自检与主检验互斥（已实现）

- **要求**：自检期间禁用主检验按钮与扫码输入。
- **现状**：已实现。`IsScanInputEnabled => !IsProcessing && !IsSelfChecking`，扫码框与「人工检验」在自检期间禁用。

---

## TODO 归属（规则 3、5 是否下阶段做）

按 **Phase2.5_Stage_Plan.md**：

- **规则 3（Start/End 防抖 + 状态栏「本次操作无效/已忽略」）**  
  属 **阶段 2（B）** 交付物。可选做法：  
  - **本阶段补做**：在 ViewModel 中实现 End 前检查当前 Session 是否有 TestRecord，无则状态栏提示且不执行 End（需在 Batch 退场、Session 接入后再做，否则仍用 BatchId 语义）。  
  - **下阶段做**：与 Batch 退场 / Session 切完后一并实现，即「阶段 2 后续一轮」或进入阶段 3 前补齐。

- **规则 5（Post-Report 异常状态栏弱提示）**  
  阶段计划中：MES 上报失败提示在 **阶段 3 可预留展示位**，**阶段 4 接事件**。可选做法：  
  - **本阶段补做**：在 ProcessCoordinator 增加 Post-Report 失败事件，ViewModel 订阅并在状态栏显示「MES 上报失败」等。  
  - **下阶段做**：阶段 3 先预留状态栏展示位或占位文案，阶段 4 再接 MES 事件并绑定。

**结论**：规则 3、5 的 TODO 可以留到 **下一阶段**（规则 3 建议与 Session 切完后一起做；规则 5 可与阶段 3/4 的 MES 提示一起做）。若希望 Step 2 自检「全部通过」再进入 Step 3，则需在本阶段或阶段 2 后续一轮内完成规则 3（及视需要规则 5）。

---

## 部分通过项说明

- **规则 7**：日志区默认折叠、可展开；当前日志按批次（BatchId）维度，Step 2 仍为 Batch 语义，后续切 Session 后需「仅当前 Session 最近 N 条」及 Session/Order/Project 绑定；条数可配置，当前默认 100，文档建议 1000/3000 可后续调。
- **规则 9**：已有 ViewModel 与命令相关测试；防抖、Post-Report 异常、自检互斥的专项用例待补。

---

## 后续动作

1. **规则 8**：已实现，无需再改。
2. **规则 3、5**：代码中已标 TODO。若**本阶段不实现**，可留到「阶段 2 后续一轮」或「阶段 3/4」再做（见上「TODO 归属」）。
3. 规则 3、5 实现后建议补充单元测试（防抖、Post-Report 异常提示）。
4. 若接受规则 3、5 为后续阶段实现，可在此视为 Step 2 自检阶段性完成，进入 Step 3；否则在阶段 2 内补齐后再进入 Step 3。
