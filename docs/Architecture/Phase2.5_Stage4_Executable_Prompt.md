# Phase 2.5 阶段 4（最终阶段）可执行 Prompt（Cursor Agent）

> 角色：**资深工业上位机（WPF）主开发工程师**  
> 目标：完成 Phase 2.5 最后一阶段的收口：**MES Gate 事件化（Post-Report 弱提示链路闭环）+ 清理前三次自检遗留 TODO（尤其 Start/End 防抖）+ 补齐关键单元测试**。  
> 最高约束：严格遵循 `docs/07_Technical_Architecture_and_Dev_Guide.md`（MVVM/Service/Snapshot/禁止项/TDD）。  

---

## 0. 输入与冻结约束（必须阅读并遵守）

### 0.1 强制参考（优先级从高到低）

1. `docs/07_Technical_Architecture_and_Dev_Guide.md`（硬规则、红线）
2. `docs/Architecture/MES_Plugin_Gate_Design_Freeze.md`（MES Gate 冻结设计）
3. 自检遗留与 TODO 汇总：
   - `docs/Architecture/Phase2.5_Step1_SelfCheck_Result.md`
   - `docs/Architecture/Phase2.5_Step2_SelfCheck_Result.md`
   - `docs/Architecture/Phase2.5_Step3_SelfCheck_Result.md`
4. Step4 自检规则（实现后必须再次自检并产出结果文件）：
   - `docs/Architecture/Phase2.5_Step4_Cursor_SelfCheck_Prompt.md`

### 0.2 红线（任何情况下不得违反）

- View **禁止**包含业务逻辑（仅绑定、最少 code-behind：InitializeComponent + 必要的 UI 行为如 Focus/IME）
- ViewModel **禁止**：
  - 直接访问 IO/协议/线程（如 ADB、SQLite、文件系统、HTTP）
  - 使用 `Application.Current` / `Dispatcher`
  - 直接弹窗（必须走 `IUserDialogService`）
- Service 层维护真实状态；UI 只绑定 Snapshot/状态属性
- Snapshot 必须不可变、无行为
- **TDD**：先写失败测试（SnVerify.Tests），再改实现

---

## 1. 三次自检遗留问题汇总（本阶段必须处理/收口）

> 这些内容来自 Step1/2/3 自检结果文档；本阶段作为最终阶段，需要明确“实现/关闭/保留”的最终结论，并在代码与文档中一致。

### 1.1 遗留 TODO #1：Start/End 防抖（必须完成，关闭 TODO）

来源：
- Step2 规则 3：开始/结束按钮防抖 ❌（要求：未生成 TestRecord 的 End 判无效，状态栏提示「本次操作无效/已忽略」）
- Step3 规则 5：防抖逻辑与重复点击判定 ❌

本阶段要求（冻结语义）：
- 重复点击 Start/End **不得触发重复动作**
- 若 End 时发现 **本次测试未产生任何 TestRecord**：
  - 不执行 End 的真实动作
  - 状态栏弱提示：**「本次操作无效/已忽略」**
  - 记录一条日志（说明该操作被忽略）
- 防抖逻辑必须发生在 **ViewModel（Command/状态机）层**，不得靠 UI 临时禁用绕过判定

### 1.2 遗留 TODO #2：Post-Report 失败 UI 弱提示事件（必须完成，关闭 TODO）

来源：
- Step2 规则 5：Post-Report 异常弱提示 ⚠️
- Step3 规则 6：Post-Report 失败事件反馈 ⚠️
- `MES_Plugin_Gate_Design_Freeze.md` §3.3 / §10.4：明确要求“弱提示 + 不阻断 + 不 FAIL 化 + 不弹窗”

本阶段要求（冻结语义）：
- Post-Report（MES 上报）失败：
  - **不影响**本站 PASS/FAIL
  - 通过 **事件/回调/状态** 通知到 UI
  - UI 用状态栏小字弱提示：**「MES 上报失败（不影响当前测试结果）」**（或更短，但必须明确“不影响结果”）
  - 同时写日志（WARN/INFO 均可，但文案需可定位）

### 1.3 遗留 TODO #3：单元测试覆盖不足（必须补齐关键用例）

来源：
- Step2 规则 9 ⚠️、Step3 规则 🔟 ⚠️：缺防抖、Post-Report 弱提示、自检互斥等专项测试

本阶段要求：
- 仅在 `SnVerify.Tests` 下新增/修改测试
- 新增的测试必须覆盖：
  - Start/End 防抖（包含“无 TestRecord 的 End 被忽略 + 状态栏提示”）
  - Post-Report 失败 → 事件到 ViewModel → `StatusBarMessage` 更新（不改变 PASS/FAIL）
  - 自检互斥（自检进行中时，Start/End/Export/StartVerify/扫码输入被禁用或忽略）

---

## 2. 阶段 4 实现范围（必须完成）

### 2.1 MES 抽象层对齐（以现有代码为准，禁止引入新的命名体系）

说明：
- 设计文档中有示例 IMesClient/IMesGate，但本项目 Phase2.5 已冻结并落地的抽象为：
  - `IMesPreCheck`
  - `IMesResultReporter`
  - `MesCapabilities`
  - `MesMode`
- 本阶段 **禁止**再引入第二套接口命名（如 IMesClient/IMesGate），必须在现有抽象上完成闭环。

必须校验并固化：
- `MesMode`：**仅允许 Disabled / Enabled**（Phase2.5 禁止开放 Strict）
- 每条 SN 前调用一次 PreCheck（MesMode != Disabled 且 SupportsPreCheck 时）
- Post-Report 失败不阻断、不回滚、不重试业务流程，但要事件化通知 UI

### 2.2 Post-Report 失败事件化闭环

实现建议（可调整，但必须满足冻结边界）：
- 在编排层（例如 `ProcessCoordinator` 或其下游）增加一个 **仅用于通知** 的事件/回调接口，例如：
  - `IMesNotificationSink`（或类似命名）仅包含 `OnPostReportFailed(message, context)` / `OnConnectionLost(...)`
  - 或在现有服务上增加 .NET event（例如 `event EventHandler<MesEventArgs> MesEventOccurred`）
- ViewModel 订阅该通知（通过依赖注入获得），将弱提示写入 `StatusBarMessage`
- UI 已绑定 `StatusBarMessage`，不得在 ViewModel 中直接操作 View 控件

必须保证：
- 事件通知 **不改变** PASS/FAIL
- 事件通知不会引入跨线程 UI 访问问题（使用 ViewModel 已捕获的 `SynchronizationContext` 投递）

### 2.3 Start/End 防抖落地（关闭 TODO）

实现要求：
- 防抖判断必须依赖“显式状态”，不能依赖控件状态
- End 前判断“本次测试是否生成过 TestRecord”：
  - 推荐通过 StorageService（或聚合 Service）查询“当前 Session/本次测试的记录条数”
  - 不允许 ViewModel 直接查数据库/文件（必须通过 Service 接口）
- 状态栏提示文案固定：**「本次操作无效/已忽略」**

---

## 3. TDD 执行顺序（强制）

1. 先在 `SnVerify.Tests` 中为上述 2.2/2.3 的行为写测试，确保测试失败  
2. 再修改生产代码使测试通过  
3. 修改后再次补充边界用例（例如：多次触发 Post-Report 失败事件不应刷屏；重复 End 不应重复执行）  

---

## 4. 验收标准（必须全部满足）

### 4.1 功能验收

- Start/End 防抖完整可用：
  - 未生成 TestRecord 的 End → 不执行 End 动作，状态栏提示「本次操作无效/已忽略」，并记录日志
- Post-Report 失败：
  - 不影响 PASS/FAIL
  - 状态栏出现弱提示（明确“不影响结果”）
  - 有日志可定位
- MesMode：
  - 仅 Disabled / Enabled
  - Disabled 时不调用 PreCheck/Post-Report

### 4.2 架构验收

- 无新增 UI 直连 MES/IO 的越权
- ViewModel 无 `Dispatcher` / `Application.Current`
- 不引入 Phase25 命名空间/文件夹
- public 类/方法具备 XML 注释，AI 生成文件含 author 标注

### 4.3 测试验收

- `SnVerify.Tests` 新增/更新的测试覆盖 2.2/2.3，且语义清晰（Arrange/Act/Assert）

---

## 5. 输出物（必须生成）

1. **阶段 4 执行结果文档**：`docs/Architecture/Phase2.5_Stage4_Execution_Result.md`
2. **Step 4 自检结果文档**（按 Step2/Step3 结果模板格式）：`docs/Architecture/Phase2.5_Step4_SelfCheck_Result.md`
   - 依据：`docs/Architecture/Phase2.5_Step4_Cursor_SelfCheck_Prompt.md`
   - 必须逐条给出 ✅/⚠️/❌ 与说明
3. 代码中所有与本阶段相关的 TODO：
   - 要么被实现并移除
   - 要么在文档中明确“为何保留、何时解决、风险评估”（最终阶段原则上应关闭）

