## Phase 2.5 – Step 6 Executable Prompt（数据库结构重构 + 业务联动，含 WPF/TDD 约束）

**角色**：你是 SnVerify 项目的主力开发（Cursor Agent / WPF + SQLite 后端），负责在**不改变 SN 检验业务规则**的前提下，重构数据库结构并联动调整业务逻辑与导出。

---

## 0. 前置约束与开发原则（必须严格遵守）

### 0.1 业务规则绝对不能变

- 所有 SN 校验/去重/历史绑定规则、ADB 错误处理、文案等以  
  `docs\rules\SN_Sticker_Device_Relation_Rules.md` 为准，**只许搬家，不许改规则**：
  - StickerSN / DeviceSN 的采集与比较规则；
  - PASS / FAIL / TIMEOUT 判定；
  - StickerSN / DeviceSN / 绑定关系的历史 PASS 检查逻辑；
  - ADB 错误（超时/读不到 SN）到 FAIL/TIMEOUT 的映射；
  - 所有对应的中文文案。

### 0.2 WPF 架构 & 开发约束

严格遵守 `docs\07_Technical_Architecture_and_Dev_Guide.md` 中的原则，包括但不限于：

- **MVVM**：
  - ViewModel 不直接操作 UI 控件、不引用 `Window` / `TextBox` 等 WPF 类型。
  - 所有 UI 行为通过绑定属性 + 命令实现。
- **线程模型**：
  - 禁止在 ViewModel 中使用 `Application.Current` / `Dispatcher`。
  - 仅允许在构造时捕获的 `SynchronizationContext` 上 `Post` 回 UI 线程（当前 `MainViewModel` / `RelayCommand` 模式为标准）。
- **服务分层**：
  - Domain / Services / ViewModels 职责清晰：
    - Domain：纯模型 & 规则，不引用基础设施。
    - Services：包含存储/协调/外部系统调用，但无 UI。
    - ViewModels：只做状态聚合和命令，不访问低层技术细节。
  - `StorageService` 仅负责数据持久化，不夹带 UI/业务判断。
- **可测试性**：
  - 所有新逻辑通过接口注入，便于在 `SnVerify.Tests` 中用 Mock 断言行为。
  - 不引入新的“快捷做法”（例如在 VM 中直接 new 窗口、MessageBox、`Dispatcher.Invoke` 等）。

### 0.3 TDD 开发流程

参考 `docs\03_Dev_Rules_TDD_and_AI.md`：

- 对每一块**非平凡逻辑**（尤其是：新表 CRUD、Session 生命周期、SN 历史查询、导出）：
  1. 先在 `SnVerify.Tests` 中写/补单元测试（让其失败）。
  2. 再实现或修改生产代码，使测试通过。
  3. 不允许只改生产代码而不补测试。
- 所有新公共方法/服务接口，都需要至少 **1 个单元测试**覆盖核心路径。

### 0.4 旧 Batch 链路可以推倒重来

- 当前没有发布版本，允许删除旧 `Batch` / `SnVerifyResult(按 Batch)` 结构；
- 不需要在线迁移工具，只需新代码与新表结构一致即可。

### 0.5 导出格式保持不变

- PASS/FAIL 双 Sheet、列名/列顺序/时间格式与现有导出保持一致：
  - Excel 表头（Id/条形码SN/设备SN/Result/FailReason/VerifyTime）不变；
  - 时间格式 `yyyy年M月d日 HH:mm:ss` 不变；
  - txt 文件的整体结构不变。
- 只改变**数据来源与维度**：从 `Batch + SnVerifyResult` → `Product + Order + TestSession + TestRecord`。

---

## 1. 目标数据模型

在 SQLite 中采用以下 4 张主表（不再冗余 TestSession.ProductId，SN 事实统一落在 `TestRecord`）：

```sql
CREATE TABLE IF NOT EXISTS Product (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductName TEXT    NOT NULL UNIQUE,   -- 业务上的产品型号，唯一
    Description TEXT,
    CreatedAt   DATETIME
);

CREATE TABLE IF NOT EXISTS "Order" (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderName TEXT    NOT NULL UNIQUE,     -- 订单名称，全局唯一
    ProductId INTEGER NOT NULL,            -- FK -> Product(Id)
    CreatedAt DATETIME,
    FOREIGN KEY (ProductId) REFERENCES Product(Id)
);

CREATE TABLE IF NOT EXISTS TestSession (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionName TEXT    NOT NULL UNIQUE,   -- 例如 OrderName_yyyyMMdd_HHmmss
    OrderId     INTEGER NOT NULL,          -- FK -> Order(Id)
    StartTime   DATETIME NOT NULL,
    EndTime     DATETIME,
    Status      TEXT,
    FOREIGN KEY (OrderId) REFERENCES "Order"(Id)
);

CREATE TABLE IF NOT EXISTS TestRecord (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId  INTEGER NOT NULL,           -- FK -> TestSession(Id)
    StickerSN  TEXT    NOT NULL,           -- 贴纸 SN（扫码）
    DeviceSN   TEXT,                       -- 设备 SN，允许 NULL（ADB 失败）
    Result     TEXT    NOT NULL,           -- PASS / FAIL / TIMEOUT
    FailReason TEXT,
    VerifyTime DATETIME NOT NULL,
    FOREIGN KEY (SessionId) REFERENCES TestSession(Id)
);
```

> 不再创建新的实体 `SnVerifyResult(SessionId)` 表；  
> 若需要“验证结果视图”，在 Service 层通过查询或 View 封装即可。

建议在实现时加上必要索引（可在 `CreateTablesAsync` 中完成）：

- `Order`：`UNIQUE(OrderName)`、`INDEX(ProductId)`
- `TestSession`：`UNIQUE(SessionName)`、`INDEX(OrderId)`
- `TestRecord`：`INDEX(SessionId)`，视情况再加 `StickerSN`/`DeviceSN` 索引。

---

## 2. StorageService & Domain 层重构任务

### 2.1 Domain 模型同步

文件：`SnVerify\Domain\Models\*.cs`

- 确保以下模型与新表结构一致：
  - `Product`：`Id`, `ProductName`, `Description`, `CreatedAt`
  - `Order`：`Id`, `OrderName`, `ProductId`, `CreatedAt`
  - `TestSession`：`Id`, `SessionName`, `OrderId`, `StartTime`, `EndTime`, `Status`
  - `TestRecord`：`Id`, `SessionId`, `StickerSN`, `DeviceSN`, `Result`, `FailReason`, `VerifyTime`
- 保持模型为**纯 POCO**，不引用任何 UI/EF 相关类型。

### 2.2 IStorageService 接口调整

文件：`SnVerify\Services\Storage\IStorageService.cs`

1. **删除/废弃 Batch 相关接口**（或标记为 `[Obsolete]`，最终不再在新代码中使用）：
   - `CreateBatchAsync`
   - `BatchExistsAsync`
   - `IsSnDuplicateAsync`
   - `IsSnDuplicateInPassAsync`
   - `GetFailResultBySnAsync`
   - `UpdateVerifyResultAsync`
   - `SaveVerifyResultAsync`
   - `GetResultsByBatchAsync`
   - `ExportBatchResultAsync`

2. **保留并重用的 SN 历史检查接口**（内部实现转为基于 `TestRecord`）：

```csharp
Task<bool> IsStickerSnInPassHistoryAsync(string stickerSN);
Task<bool> IsDeviceSnInPassHistoryAsync(string deviceSN);
Task<bool> IsBindingInPassHistoryAsync(string stickerSN, string deviceSN);
```

- 实现策略：
  - 使用 `TestRecord` 表 `WHERE Result='PASS'` 做历史查询；
  - 语义严格对齐 `SN_Sticker_Device_Relation_Rules.md` 中的说明。

3. **新增/调整 Product / Order / TestSession / TestRecord 接口**

建议接口形态（可微调）：

```csharp
// Product
Task<int> CreateProductAsync(Product product);
Task<IReadOnlyList<Product>> GetAllProductsAsync();

// Order
Task<int> CreateOrderAsync(Order order);
Task<bool> OrderNameExistsAsync(string orderName);
Task<IReadOnlyList<Order>> GetAllOrdersAsync();

// TestSession
Task<int> CreateSessionAsync(TestSession session);
Task<IReadOnlyList<TestSession>> GetSessionsByOrderIdAsync(int orderId);
Task<bool> SessionNameExistsAsync(string sessionName);

// TestRecord
Task SaveTestRecordAsync(TestRecord record);
Task<IReadOnlyList<TestRecord>> GetTestRecordsBySessionAsync(int sessionId);
Task<TestRecord> GetTestRecordBySessionAndStickerSnAsync(int sessionId, string stickerSN);
Task UpdateTestRecordAsync(TestRecord record);
```

> **TDD 要求**：  
> 为上述方法先在 `SnVerify.Tests\Services\Phase25StorageServiceTests.cs` 中添加/更新对应测试，再去实现接口与存储逻辑。

### 2.3 StorageService.CreateTablesAsync 重写

文件：`SnVerify\Services\Storage\StorageService.cs`

- 在 `CreateTablesAsync` 中：
  - 删除 `Batch` / 旧 `SnVerifyResult` 建表与 `ALTER TABLE` 逻辑；
  - 只保留/新增 `Product / Order / TestSession / TestRecord` 四张表的建表脚本；
  - 添加必要索引。
- 确保 `EnsureConnectionInitialized` 不再隐式创建旧的 Batch/SnVerifyResult 结构。

---

## 3. 会话生命周期 / ProcessCoordinator / VM 对接

### 3.1 Session 生命周期服务

文件：`SnVerify\Services\Session\ISessionLifecycleService.cs`、`SessionLifecycleService.cs`

- 目标：从 `Order.Id` + `ProductId` 创建并管理 `TestSession`，生成业务可读 `SessionName`。
- 建议流程：
  1. 从 UI 传入 `ProductName` / `OrderName`；
  2. 若 `Product` 不存在则创建，否则复用；
  3. 若 `Order` 不存在则创建，否则复用（`OrderName` 全局唯一）；
  4. 使用 `OrderName + "_" + yyyyMMdd_HHmmss` 生成 `SessionName`；
  5. 创建 `TestSession` 记录，返回 `TestSession.Id` 与 `SessionName`。
- 服务需要提供：

```csharp
int CurrentSessionId { get; }
string CurrentSessionName { get; }

int CreateAndStartSession(int orderId, string orderName);
void EndSession();
```

> 具体签名可结合现有 `SessionSnapshot` 做适配，但要保证：
> - ViewModel 能拿到“当前会话是否存在/活动中”；
> - `ProcessCoordinator` 能拿到当前 `SessionId`。

### 3.2 ProcessCoordinator 落库与历史查询迁移

文件：`SnVerify\Services\Coordination\ProcessCoordinator.cs`

- 所有原来写 `SnVerifyResult` 的逻辑改成写 `TestRecord`：
  - PASS 时：新建一条 `TestRecord(Result='PASS')`；
  - FAIL/TIMEOUT 时：新建/更新 `TestRecord(Result='FAIL'/'TIMEOUT')`；
  - 使用当前 `SessionId`（INT）作为外键。
- SN 历史绑定检查：
  - 原来在 `SnVerifyResult` 上的 SQL，全部改调用 `IStorageService` 的新方法（基于 `TestRecord` 实现）。
- 注意：
  - 判定逻辑、错误类型、文案 **不得改变**，只换表和字段名。

### 3.3 MainViewModel / UI 行为

文件：`SnVerify\ViewModels\MainViewModel.cs`、`SnVerify\MainWindow.xaml`

- 不改变 SN 检验业务流程，只改变数据维度与显示：
  - 开始测试：
    - 输入：`ProductName` + `OrderName`（或在 UI 上用 Product/Order 下拉+输入）；
    - 调用 Session 生命周期服务创建 Session；
    - 使用 `SessionName`（例如 `OrderName_yyyyMMdd_HHmmss`）作为“本次测试标识”（只读展示）。
  - 状态栏：
    - “当前订单”显示 `OrderName` 或 `OrderName(SessionName)`；
    - 最近结果/状态保持现有逻辑。
  - 导出：
    - 基于 Product / Order / Session 三个层级选择导出对象，调用新的导出服务。

> **WPF 约束**：  
> - ViewModel 只通过属性和命令与 View 交互；  
> - 不在 ViewModel 中直接 new 窗口 / 控件；  
> - 所有与 UI 控件交互的逻辑（例如 `Focus`）只在 `MainWindow.xaml.cs` 的简单事件处理中做。

---

## 4. 导出逻辑迁移（保持格式不变）

### 4.1 数据来源切换

- 以 `TestRecord` 为唯一事实源，实现：
  - **按 Session 导出**：
    - PASS Sheet：`TestRecord WHERE SessionId=@sid AND Result='PASS'`；
    - FAIL Sheet：`Result IN ('FAIL','TIMEOUT')`，按 `(StickerSN, DeviceSN)` 去重保留第一条。
  - **按 Order 导出**：
    - 找出该 Order 下所有 Session，再汇总其 `TestRecord`。
  - **按 Product 导出**：
    - 找出该 Product 下所有 Order → Session → TestRecord。

### 4.2 Excel/txt 结构

- 复用现有：
  - `WriteTestRecordSheetHeader` / `WriteTestRecordSheetData`；
  - txt 文件结构（第一行 Session 信息 + PASS/FAIL 行）。
- 确保：
  - 列顺序：`Id, 条形码SN, 设备SN, Result, FailReason, VerifyTime`；
  - 时间格式为 `yyyy年M月d日 HH:mm:ss` 字符串，而不是 Excel 数值日期。

> **TDD**：在 `Phase25StorageServiceTests` 等测试类中，新增/调整测试覆盖：
> - PASS/FAIL 拆分；
> - FAIL 按 `(StickerSN, DeviceSN)` 去重，保留第一条；
> - 时间格式；
> - 按 Session / 按 Order / 按 Product 导出路径。

---

## 5. TDD 执行顺序建议

1. **测试先行：存储层**
   - 在 `SnVerify.Tests\Services\Phase25StorageServiceTests.cs` 等处：
     - 为 Product/Order/TestSession/TestRecord 的 CRUD 写测试；
     - 为 SN 历史绑定规则对应的查询方法写测试；
     - 为导出逻辑写测试（按 Session/Order/Product）。
2. **实现存储层与 Domain 模型**
   - 重写 `CreateTablesAsync`；
   - 实现新的 `IStorageService` 接口；
   - 使上述测试全部通过。
3. **测试先行：Session 生命周期 + ProcessCoordinator**
   - 为新 Session 创建/结束、落库 TestRecord、历史绑定检查路径写测试；
   - 然后实现 `SessionLifecycleService` 与 `ProcessCoordinator` 调整。
4. **测试先行：导出 & VM 行为**
   - 为导出入口、按钮可用性、状态文本、Session 标识等写/更新 VM 测试；
   - 然后实现导出与 VM 改造。

> 执行过程中请始终对照：
> - 架构与代码风格：`docs\07_Technical_Architecture_and_Dev_Guide.md`
> - TDD 与 AI 协作规则：`docs\03_Dev_Rules_TDD_and_AI.md`
> - SN 业务规则：`docs\rules\SN_Sticker_Device_Relation_Rules.md`

