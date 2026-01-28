# Phase 2.5 阶段 3 执行结果

> 依据：`docs/Architecture/Phase2.5_Stage3_Executable_Prompt.md`  
> 执行时间：阶段 3 执行时填写

---

## 执行结论

**阶段 3（UI 行为与布局，C）已完成**：C1（功能与语义）与 C2（拟物化与布局）均已落地。

---

## 已完成交付物

### C1：功能与语义

1. **✅ C1.1：主界面文案与绑定**
   - 文案已更新：`批次号` → `本次测试`，`开始批次` → `开始测试`，`结束批次` → `结束测试`
   - 状态栏：`批次: {0}` → `当前订单: {0}`
   - 对外不出现 SessionId/Session 字样，使用「本次测试」「当前订单」等说法
   - **注意**：内部仍使用 Batch 过渡（阶段 2 未完全退场），待 B 完全切 Session 后对齐

2. **✅ C1.2：导出流程「选维度 → 选对象 → 执行」+ 覆盖确认弹窗**
   - 实现「选维度（按项目 / 按订单）」→「选对象（具体项目或订单）」→ 执行流程
   - 调用 B 提供的 `IExportAggregationService`（`ExportByOrderIdAsync` / `ExportByProjectIdAsync`）
   - 覆盖确认弹窗：检查目标文件是否存在，存在则弹窗确认覆盖或取消
   - **新增接口**：`IStorageService.GetAllOrdersAsync()`、`GetAllProjectIdsAsync()`（用于「选对象」）

3. **✅ C1.3：校验弹窗挂接**
   - 开始测试时挂接 `IOrderNameValidator`，命名校验不通过则弹窗提示且不创建 Session
   - `ServiceFactory` 创建 `OrderNameValidator` 并注入到 `MainViewModel`

4. **✅ C1.4：状态栏无效操作/MES 预留提示**
   - 状态栏新增 `StatusBarMessage` 属性（用于无效操作/MES 上报失败提示）
   - 规则 3（End 前检查 TestRecord）与规则 5（Post-Report 失败事件）的 TODO 已保留，状态栏预留展示位

5. **✅ C1.5：日志区最近 3000 条配置**
   - `ServiceFactory` 创建 `LoggingService` 时指定 `maxRecentMessages: 3000`

6. **✅ C1.6：重复「设备SN已存在」UI只一条**
   - `FailReason` 属性中实现：若当前失败原因与上次相同且为「设备SN已存在」，则不重复显示（UI 只一条）
   - 日志每次保留（不受此限制）

### C2：拟物化与布局

7. **✅ C2：PASS/FAIL 拟物化、日志默认折叠、状态栏语义收敛**
   - **PASS/FAIL 拟物化**：已有 `ResultCardStyle`（外框拟物）+ 内部状态灯纯色块（Pass=绿、Fail=红、Processing=蓝、Idle=灰）
   - **日志默认折叠**：`Expander` 的 `IsExpanded="False"`，默认折叠
   - **状态栏语义收敛**：已更新为「当前订单」「处理中」「最近结果」+ `StatusBarMessage`（无效操作/MES 预留）

---

## Phase25 命名空间检查

**✅ 无 Phase25 命名空间或文件夹残留**：
- 所有 Phase25 文件夹已重命名为 `Domain\Validation`
- 所有命名空间使用 `SnVerify.Domain.Validation`（SessionIdGenerator、IOrderNameValidator、OrderNameValidator）
- 代码中仅注释提到 "Phase 2.5"（文档注释说明来源），无命名空间或文件夹引用

---

## 代码变更摘要

### 新增接口与方法

- **IStorageService**：
  - `Task<IReadOnlyList<Order>> GetAllOrdersAsync()`（阶段 3 C1.2）
  - `Task<IReadOnlyList<string>> GetAllProjectIdsAsync()`（阶段 3 C1.2）
- **StorageService**：实现上述两个方法

### ViewModel 变更

- **MainViewModel**：
  - 新增依赖：`IExportAggregationService`、`IOrderNameValidator`
  - 新增属性：`StatusBarMessage`（状态栏消息）、`IsScanInputEnabled`（自检期间禁用扫码）
  - `ExportAsync`：重写为「选维度→选对象→执行」+ 覆盖确认弹窗
  - `StartBatchAsync`：挂接校验弹窗（`IOrderNameValidator`）
  - `FailReason`：实现重复「设备SN已存在」UI只一条
  - `CurrentBatchId`：注释更新为「当前订单」（内部仍用 BatchId 过渡）

### UI 变更

- **MainWindow.xaml**：
  - 文案更新：`批次号` → `本次测试`，`开始批次` → `开始测试`，`结束批次` → `结束测试`
  - 状态栏：`批次: {0}` → `当前订单: {0}`，新增 `StatusBarMessage` 绑定
  - 日志区域：已有 `Expander` 且 `IsExpanded="False"`（默认折叠）
  - PASS/FAIL 拟物化：已有 `ResultCardStyle` 与状态灯样式

### 服务工厂变更

- **ServiceFactory**：
  - 创建 `ExportAggregationService` 并注入到 `MainViewModel`
  - 创建 `OrderNameValidator` 并注入到 `MainViewModel`
  - `LoggingService` 创建时指定 `maxRecentMessages: 3000`

---

## 验收标准（M3）检查

| 验收项 | 状态 | 说明 |
|--------|------|------|
| 导出流程「选维度→选对象→执行」可用 | ✅ | 已实现 MessageBox 选择维度 + ListBox 选择对象 + 调用 B 的导出服务 |
| 覆盖时弹窗确认或取消 | ✅ | 检查目标文件存在后弹窗确认覆盖 |
| 主界面文案不出现 SessionId/Session | ✅ | 使用「本次测试」「当前订单」等 |
| 校验弹窗挂接 | ✅ | `StartBatchAsync` 中挂接 `IOrderNameValidator` |
| 状态栏与自检期间 UI 与 B 一致 | ✅ | 状态栏已更新，自检期间禁用扫码/人工检验（规则 8 已实现） |
| 日志 3k、重复提示收敛 | ✅ | 日志 3000 条，重复「设备SN已存在」UI只一条 |
| C2 拟物化与折叠按 UI 约束落地 | ✅ | PASS/FAIL 拟物化、日志默认折叠、状态栏语义收敛 |

---

## 后续动作

1. **规则 3、5 的 TODO**：已在代码中保留，待阶段 2 后续或阶段 4 实现后绑定状态栏提示。
2. **Batch 退场**：当前 ViewModel 仍使用 Batch 术语（`IBatchManager`、`BatchSnapshot` 等），内部逻辑待阶段 2 后续完全切 Session 后对齐。
3. **单元测试**：建议为导出流程「选维度→选对象→执行」、校验弹窗挂接、状态栏消息、重复提示收敛补充单元测试或集成验证。

---

**文档版本**  
* 依据：Phase2.5_Stage3_Executable_Prompt.md、Phase2.5_Stage_Plan.md §四、Phase2.5_Technical_Refactor_Checklist.md §二/§四。
