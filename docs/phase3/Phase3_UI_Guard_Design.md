# Phase3 UI Guard 设计方案

本文档为 **MainViewModel / Phase3 UI 防呆与交互设计说明**，作为后续代码实现依据。仅设计文档，不涉及现有代码修改。

---

## 一、整体设计原则

Phase3 UI 的操作规则遵循以下原则：

- **防呆逻辑前移**  
  与产品密切相关的输入（`ProjectIdInput` / `OrderIdInput`）在 **`ProductCode` 变化时立即处理**，避免错误数据进入 Session。

- **StartBatch 为最终校验入口**  
  所有开始测试前的业务检查逻辑 **统一集中在** `StartBatchCommand` → `StartBatchAsync()`。

- **减少操作员交互**  
  **只有在「项目名」已存在历史记录的情况下才弹出提示**，其余情况无额外交互。

---

## 二、ProductCode 切换行为（SelectedProductCode Change）

### 2.1 行为触发条件

当 `SelectedProductCode` 发生变化时，满足下列条件才进入防呆逻辑：

- `IsSessionActive == false`
- ProductCode 是否变化必须使用：

  ```csharp
  !string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase)
  ```

  即：若 `oldCode` 与 `newCode` 忽略大小写后相同，则认为未变化，**不触发**清空。

### 2.2 系统行为

满足条件时自动执行（无弹窗）：

- `ProjectIdInput = ""`
- `OrderIdInput = ""`

**启动/初始化约定（实现细节）**：首次设置 `SelectedProductCode`（例如启动时从配置恢复或初始化默认值）不视为“切换”，因此不会触发清空；只有当 `oldCode` 非空且确实发生变化时才清空，避免覆盖 Settings 恢复的项目名/订单名。该行为由专用的 **初始化 Scope** 控制（见后文约束）。

### 2.3 交互约束

- 禁止任何对话框、禁止回滚 `SelectedProductCode`。

---

## 三、StartBatch 前置校验流程

`StartBatchAsync` 内顺序：

1. **Step1** Trim 输入  
2. **Step2** 基础输入校验（ProductCode、项目名、订单名等）  
3. **Step3** 项目名是否存在（调用 `ShouldWarnProjectAlreadyExistsAsync(projectName)`）  
4. **若存在** → 弹出友情提示；**继续**则进入 Step5，**取消**则终止  
5. **Step5** 创建 Session（现有逻辑不变）

**补充约定**：StartBatch 不会在校验失败时自动修改/回填 `ProjectIdInput`（避免出现“提示项目名为空，但输入框被系统自动写入 ProductCode”的误导）。

---

## 四、ProjectName 重复检测规则（按项目名查找）

### 4.1 检测对象与查找方式（与代码一致）

- **检测对象**：**项目名**（即用户输入的「项目名」）。
  - 在 ViewModel 中对应：`ProjectIdInput`（界面文案为「项目名」）。
  - 在存储层对应：`Product.ProductName`（Phase 2.5 用 ProductName 作为“项目”标识，Order 通过 ProductId 关联 Product）。

- **查找方式**：  
  通过 **项目名** 在历史“项目名”列表中做 **存在性判断**。  
  即：当前输入的项目名是否在“已有项目名集合”中出现过。

- **与评审结论的对应**：  
  评审结论中的“ProjectId”仅为举例。方案以实际代码为准：**按项目名（ProjectName）查找匹配**即可；存储层接口参数名仍可能为 `projectId`，但语义与数据均为 **项目名（Product.ProductName）**。

### 4.2 匹配规则

- 仅按 **项目名字符串** 判断是否已存在。
- 不需要按 `(ProductCode, ProjectId)` 组合判断。
- 比较时使用 **忽略大小写** 的字符串比较，与 ProductCode 变化判断一致，建议使用 `StringComparison.OrdinalIgnoreCase`。

### 4.3 设计理由

- 项目名在业务上具备唯一语义，不同项目一般不共享同名。
- 与现有存储一致：当前实现中“项目”维度即 `Product.ProductName`，无需改存储结构。

---

## 五、项目名已存在提示（友情提示）

当 **项目名** 已存在历史记录时，弹出：

**提示内容：**

> 检测到项目名 "{项目名}" 已存在历史记录。  
> 继续使用可能导致不同批次数据混在一起。  
>  
> 是否继续开始测试？

**按钮：** 继续 / 取消  

**行为：** 继续 → 继续执行 StartBatch；取消 → 终止。  

每次点击 StartBatch 且项目名已存在时都弹窗，不做“避免重复弹窗”的缓存。

---

## 六、数据来源与实现说明（项目名存在性查询）

### 6.1 查询方式（Phase3）

- **采用接口**：存储层新增 **`ProjectNameExistsAsync(projectName)`**，用于直接查询项目名是否已存在。  
  - 调用方：`MainViewModel.ShouldWarnProjectAlreadyExistsAsync(projectName)` 内调用 `_storageService.ProjectNameExistsAsync(projectName)`。  
  - 返回：`Task<bool>`，存在为 true，不存在为 false。

- **存储层约定**：  
  - 接口定义：例如 `Task<bool> ProjectNameExistsAsync(string projectName)`（或 `projectId` 参数名，语义为项目名）。  
  - 实现：按 `Product.ProductName` 做存在性查询；建议在数据库侧使用“存在即返回”的写法，例如：  
    - `SELECT 1 FROM Product WHERE ProductName = @projectName LIMIT 1`（或等价写法），  
  - 复杂度：O(1)（或索引查找），避免全表扫描或全量加载。

- **比较规则**：  
  - 存储层实现时，项目名比较建议 **忽略大小写**（与第四节匹配规则一致），以便与 UI 侧 `StringComparison.OrdinalIgnoreCase` 语义一致。

### 6.2 与现有接口的关系

- 导出等场景仍使用现有 `GetAllProjectIdsAsync()`，无需修改。  
- 仅在本方案涉及的“开始测试前项目名重复提示”流程中，使用 `ProjectNameExistsAsync` 查询；实现阶段需在 `IStorageService` / `StorageService` 中新增该接口及实现。

---

## 七、与现有逻辑的兼容性

以下保持不变：

- Settings 保存逻辑（StartBatch 成功后才保存项目名/订单号等）
- SessionLifecycleService、LoggingService 的现有行为
- StorageService 中现有接口（如 `GetAllProjectIdsAsync`、`GetSessionsByProjectIdAsync` 等）的签名与行为

**本次方案范围内的存储层变更**：在 `IStorageService` / `StorageService` 中 **新增** `ProjectNameExistsAsync(projectName)` 接口及实现，用于项目名存在性查询；不修改现有接口的签名或实现。

---

## 八、MainViewModel 需新增的方法（方案级）

### 8.1 `OnProductCodeChanged(oldCode, newCode)`

- 使用 `string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase)` 判断是否变化；为 false 时执行 `ProjectIdInput = ""`、`OrderIdInput = ""`。  
- 无弹窗、不回滚 SelectedProductCode。  
- `SelectedProductCode` 的 setter 在 `IsSessionActive == true` 且新值与旧值不同的情况下，应**直接忽略本次修改**（既不更新字段，也不调用 `OnProductCodeChanged`），即：测试进行中禁止切换产品类型，避免 UI 与实际 Session 产品不一致。  
- 为避免错误顺序，推荐 setter 结构为：  

  ```csharp
  public string SelectedProductCode
  {
      get => _selectedProductCode;
      set
      {
          var oldCode = _selectedProductCode;

          // 先防守：活动 Session 一律忽略切换，且不触发 PropertyChanged
          if (IsSessionActive &&
              !string.Equals(oldCode, value, StringComparison.OrdinalIgnoreCase))
              return;

          if (SetProperty(ref _selectedProductCode, value))
          {
              if (!IsSessionActive)
                  OnProductCodeChanged(oldCode, value);
          }
      }
  }
  ```

  即：**在调用 `SetProperty` / 触发 PropertyChanged 之前就拦截掉活动 Session 下的不合法切换**。

### 8.2 `ShouldWarnProjectAlreadyExistsAsync(projectName)`

- 参数为当前项目名（即 `ProjectIdInput.Trim()` 的取值）。  
- 若为空则返回 false。  
- 调用 **`_storageService.ProjectNameExistsAsync(projectName)`** 查询该项目名是否已存在；返回 true 则本方法返回 true（需要弹窗），否则返回 false。  
- 不使用 `_lastProjectWarningKey` 等缓存，每次 StartBatch 且存在即提示。

### 8.3 初始化 Scope（约束 1 & 2）

- 引入专用的初始化 Scope，例如 `private IDisposable BeginInitializationScope()` 或 `SuppressProductChangeEffectForInitialization()`：  
  - 仅在构造函数中使用，用于包裹“加载产品列表 + 恢复上次 ProductCode/版本”的初始化逻辑。  
  - Scope 内部使用**计数器模式**，支持安全嵌套：  

    ```csharp
    private int _initScopeCounter;
    private bool IsInitialization => _initScopeCounter > 0;

    private IDisposable BeginInitializationScope()
    {
        _initScopeCounter++;
        return new DisposeAction(() =>
        {
            _initScopeCounter = Math.Max(0, _initScopeCounter - 1); // 防御性保护，避免计数器变为负数
        });
    }
    ```

  - `OnProductCodeChanged` 中使用 `IsInitialization` 判断是否处于初始化阶段：  

    ```csharp
    if (IsInitialization)
        return;
    ```

  - 示例结构（伪代码）：  

    ```csharp
    private void InitializeViewModel()
    {
        using (BeginInitializationScope())
        {
            LoadAvailableProducts();
            RestoreLastProductAndExpectedVersions();
        }

        // 然后再恢复 ProjectIdInput / OrderIdInput
        RestoreProjectOrderFromSettings();
    }
    ```

- Scope 内部只做一件事：在进入时设置 `_suppressProductChangeEffect = true`，在 `Dispose()` 时恢复为 false。  
- **约束 1**：此 Scope **只允许在初始化阶段（构造函数）使用**，不得在运行期其它逻辑中使用，以避免绕过防呆。  
- **约束 2（恢复顺序）**：  
  - 必须先完成产品列表加载与 ProductCode 恢复（Scope 内），再恢复 `ProjectIdInput/OrderIdInput`（Scope 外）。  
  - 在代码注释中明确：`RestoreProjectOrderFromSettings()` **必须在产品选择完成之后调用**，否则未来维护者改变顺序会重新引入“恢复后又被清空”的问题。

### 8.4 `ClearProjectOrder` 的唯一入口（约束 3）

- 定义一个内部方法 `ClearProjectOrder()`，仅负责将 `ProjectIdInput` 与 `OrderIdInput` 置空，并触发 PropertyChanged。  
- **约束 3**：`ClearProjectOrder()` **只能在 `OnProductCodeChanged` 中被调用**，不得在以下位置直接或间接调用：  
  - `ProjectIdInput` 的 setter  
  - `OrderIdInput` 的 setter  
  - `StartBatchAsync` 或其它命令处理逻辑  
- 正确的调用链应始终是：  

  ```text
  用户操作 → 修改 SelectedProductCode
            → OnProductCodeChanged(oldCode, newCode)
            → ClearProjectOrder()
  ```

  通过这一约束，避免将来在其它 setter 或命令里“顺手清空”，导致行为难以追踪、与防呆设计不一致。

---

## 九、操作员行为场景摘要

- **场景 1**：切换 ProductCode → 满足条件时自动清空项目名、订单名，无弹窗。  
- **场景 2**：输入**未出现过**的项目名并点击开始测试 → 不弹窗，直接开始。  
- **场景 3**：输入**已存在**的项目名并点击开始测试 → 弹出项目名重复提示，操作员选择继续或取消。

### 9.1 关键测试用例补充（含产品顺序变化）

为确保实现不依赖产品列表顺序，并覆盖初始化/真实切换/保存恢复等关键路径，建议至少包含以下单元测试：

- **测试 1：初始化恢复（产品顺序 A）**  
  - Products = `["SOLTAG25","KM001"]`  
  - Settings: `LastProductCode="KM001"`, `LastProjectId="P123"`, `LastOrderId="O123"`  
  - 期望：构造后 `SelectedProductCode=="KM001"` 且 `ProjectIdInput=="P123"`, `OrderIdInput=="O123"`。

- **测试 5：初始化恢复（产品顺序 B，顺序反转）**  
  - Products = `["KM001","SOLTAG25"]`  
  - Settings 同上  
  - 期望同测试 1，确保逻辑不依赖“LastProductCode 是否是第一个产品”。

- **测试 2：初始化不触发清空**  
  - 在初始化 Scope 内多次改变 `SelectedProductCode`，结束后检查 `ProjectIdInput/OrderIdInput` 仍为 Settings 恢复值，未被清空。

- **测试 3：真实切换必须清空**  
  - 初始化完成后（Scope 已结束），设置 `SelectedProductCode="SOLTAG25"`, `ProjectIdInput="P1"`, `OrderIdInput="O1"`；  
  - 再切到 `SelectedProductCode="KM001"`；期望项目名/订单名被清空。

- **测试 4：保存逻辑（与项目类型无关）**  
  - 在 SOLTAG25 / KM001 下各跑一次成功的 `StartBatch`，检查  
    - `Settings.LastProjectId == 当前 ProjectIdInput`  
    - `Settings.LastOrderId  == 当前 OrderIdInput`  
  - 配合测试 1/5 确认下次启动时能正确恢复。

- **测试 6：Session 激活时禁止切换产品**  
  - 启动测试使 `IsSessionActive == true`，然后尝试修改 `SelectedProductCode`；  
  - 期望：`SelectedProductCode` 保持原值，`OnProductCodeChanged` 不被调用，`ProjectIdInput/OrderIdInput` 不被清空，确保测试进行中不能通过 UI 切换产品类型。

---

## 十二、Phase3 SN 唯一性与批次定义（Design Only）

> 本节只描述规则设计，不代表当前代码已完全实现；实际行为应通过规则引擎与存储层实现逐步对齐。

### 12.1 批次键 BatchKey 定义

- 对于 Phase3 SN 检验，批次的工程定义为：

  ```text
  BatchKey = (ProjectName, OrderName)
  ```

  其中：
  - `ProjectName`：来自 `Product.ProductName`，与 UI 上的“项目名”一致，用于表示项目/产品实例。  
  - `OrderName`：业务订单名（当前系统中的 `Order.OrderName`）。

### 12.2 SN 唯一性规则（Phase3 专用）

- Phase3 SN 唯一性约束定义为：

  ```text
  (ProjectName, OrderName, StickerSN)
  ```

  即：
  - **同一 BatchKey 内（同一 ProjectName + 同一 OrderName）**：  
    - 某个 `StickerSN` 一旦有一条 `Result = 'PASS'` 的记录，再次使用该 `StickerSN` 应判定为重复（FAIL）。  
  - **跨 BatchKey**：  
    - 不同 ProductCode 或不同 OrderName 视为不同批次；  
    - 允许相同 `StickerSN` 在不同批次分别 PASS。

- **仅 PASS 参与唯一性判断**：  
  - 只有 `Result = 'PASS'` 的记录会被视为“占用 SN”；`FAIL` / `ERROR` / 其它状态的记录**不参与 SN 唯一性判断**。  
  - 例如，同一 BatchKey 下第一次 PASS 后，后续若都是 FAIL 记录，则不会因为这些 FAIL 而阻止再次尝试，只有再次 PASS 才会触发“重复 SN”判定。

- 这等价于表格行为：

  | ProjectName | OrderName | StickerSN | 结果期望 |
  |-------------|-----------|-----------|----------|
  | A           | B         | X         | 第一次 → PASS |
  | C           | B         | X         | 第二次（项目变更）→ PASS |
  | C           | B         | X         | 第三次（同批次重复）→ FAIL |
  | A           | C         | X         | 不同订单 → PASS |
  | A           | B         | Y         | 不同 SN → PASS |

### 12.3 Phase3 ChipId 唯一性（批次内）

- Phase3 下，ChipId 的唯一性规则与 SN 保持一致，同样基于批次键 `BatchKey = (ProjectName, OrderName)`：  
  - **唯一性约束**：  
    - `(ProjectName, OrderName, ChipId)` 在同一批次内只允许出现一次 `Result = 'PASS'` 记录；  
    - 当同一批次内某个 `ChipId` 已经 PASS，再次使用相同 `ChipId` 时应判定为重复（`CHIPID_DUPLICATE`）。  
  - **跨批次行为**：  
    - 不同 `ProjectName` 或不同 `OrderName` 视为不同批次，允许相同 `ChipId` 在不同批次分别 PASS。  

- 存储层接口与实现：  
  - `IStorageService.IsChipIdPassedInBatchAsync(string projectName, string orderId, string chipId)`  
  - `StorageService.IsChipIdPassedInBatchAsync(...)` 通过联结 `TestRecord → TestSession → Order → Product`，按  
    `o.OrderName = @OrderId AND p.ProductName = @ProjectName AND r.ChipId = @ChipId AND r.Result = 'PASS'`  
    判断是否存在 PASS 记录，仅 `Result = 'PASS'` 记录参与 ChipId 唯一性判断。  

- 规则执行层：  
  - `RulePipelineExecutor` 在 Phase3 路径中使用 `_storageService.IsChipIdPassedInBatchAsync(profile.ProductName, orderId, chipId)` 进行 ChipId 批次内唯一性判断；  
  - `ProcessCoordinatorPhase3` 只依赖上述批次级接口，不再使用旧的 `IsChipIdPassedInOrderAsync` 作为 Phase3 唯一性规则入口。  

### 12.4 Phase3 与全局 SN 历史规则的关系

- 系统中还存在跨所有批次的全局历史检查接口，例如：
  - `IsStickerSnInPassHistoryAsync`  
  - `IsDeviceSnInPassHistoryAsync`

- 这些接口代表的是另一套更强的业务规则（“SN 在系统中不能重复 PASS”），与上面的 Phase3 批次内唯一性是两个不同层级：
  - **Phase3 SN 唯一性**：  
    - 仅按 `(ProductCode, OrderName, StickerSN)` 判断当前批次内是否重复，**不使用 SessionId 作为唯一性维度**，Session 只是某个批次的一次执行周期。  
  - **全局历史规则**：  
    - 按 `(StickerSN)` 或 `(DeviceSN)` 跨所有批次判断是否已通过。

- 为满足“项目切换视为新批次、允许同一 SN 在不同项目下重新测试”的需求，设计上约定：
  - **Phase3 的 SN Duplicate 判定仅基于 `(ProductCode, OrderName, StickerSN)`**；  
  - Phase3 主检验流**不应再调用全局 `IsStickerSnInPassHistoryAsync` / `IsDeviceSnInPassHistoryAsync` 来阻止跨项目/跨订单的再次 PASS**。  
  - 若未来需要“设备永久出站”或更强的全局 SN 约束，应在文档中单独设计并实现新的规则，而不是隐式复用当前全局历史接口。

### 12.5 Phase3 SN / ChipId 唯一性测试建议

为验证上述 SN / ChipId 规则，建议在规则引擎或服务层增加如下测试场景（TDD）：

- **用例 1：不同项目，同一订单，同一 SN**  
  - (ProductCode=A, OrderName=B, StickerSN=X) → PASS  
  - (ProductCode=C, OrderName=B, StickerSN=X) → PASS  
  - (ProductCode=C, OrderName=B, StickerSN=X) → FAIL  

- **用例 2：同一项目，不同订单，同一 SN**  
  - (A,B,X) → PASS  
  - (A,C,X) → PASS  

- **用例 3：同一项目、同一订单，不同 SN**  
  - (A,B,X) → PASS  
  - (A,B,Y) → PASS  

- **用例 4：Legacy 与 Phase3 共存场景（可选）**  
  - 在 Legacy 模式下 PASS 某 SN；  
  - 在 Phase3 模式下，基于 `(ProductCode, OrderName, StickerSN)` 的规则再次检验该 SN，应按 Phase3 规则行为（是否允许、是否视为新批次），该行为需在业务评审中另行确定并写入相应文档。

- **ChipId 场景（建议与 SN 对齐）：**  
  - **用例 5：不同项目，同一订单，同一 ChipId**  
    - (ProjectName=A, OrderName=B, ChipId=F501) → PASS  
    - (ProjectName=C, OrderName=B, ChipId=F501) → PASS  
    - (ProjectName=C, OrderName=B, ChipId=F501) → FAIL（同一批次内重复）  
  - **用例 6：同一项目，不同订单，同一 ChipId**  
    - (A,B,F501) → PASS  
    - (A,C,F501) → PASS  
  - **用例 7：同一项目、同一订单，不同 ChipId**  
    - (A,B,F501) → PASS  
    - (A,B,F502) → PASS  

---

## 十、本次任务范围

- 当前步骤仅更新/维护方案设计文档，不执行代码、UI、Service 修改。  
- 文档与实现约定：**重复检测按项目名（ProjectName）查找**，采用 **`ProjectNameExistsAsync(projectName)`** 查询存在性；实现阶段需在 Storage 层新增该接口及实现（按 `Product.ProductName` 查询，建议忽略大小写、索引查找 O(1)）。

---

## 十一、实现状态（供维护参考）

- **已实现**（与方案一致）：
  - `IStorageService` / `StorageService` 新增 `ProjectNameExistsAsync(projectName)`，按 `Product.ProductName`、LOWER 比较忽略大小写，`SELECT 1 ... LIMIT 1`。
  - `IUserDialogService` 新增 `Confirm(message, title)`；`WpfUserDialogService` 实现为 Yes/No 对话框。
  - `MainViewModel.OnProductCodeChanged(oldCode, newCode)`：`string.Equals(OrdinalIgnoreCase)` 为 false 时清空 `ProjectIdInput`、`OrderIdInput`。
  - `MainViewModel.ShouldWarnProjectAlreadyExistsAsync(projectName)`：调用 `_storageService.ProjectNameExistsAsync`。
  - `SelectedProductCode` setter 在未激活 Session 且值变化时调用 `OnProductCodeChanged`。
  - `StartBatchAsync` 在基础校验通过后调用 `ShouldWarnProjectAlreadyExistsAsync`，若存在则 `Confirm`，取消则 return，继续则创建 Session。
- **单元测试**：`Phase25StorageServiceTests.ProjectNameExistsAsync_*`；`MainViewModelTests.SelectedProductCode_WhenChanged_*`、`StartBatch_WhenProjectNameExists_*`。
