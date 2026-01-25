# SnVerify Phase 2 Cursor Agent 执行计划表

> 目的：明确 Phase 2 所有模块 Prompt 的执行顺序、TDD 测试优先级和集成测试节点，方便 Cursor Agent 顺序或并行生成代码。

---

## 一、执行策略说明

1. **TDD 测试优先**

   * 所有模块先生成单元测试，再生成实现代码
   * Mock 外部依赖（ADB、Storage、MES、ScanInputService）

2. **模块化执行**

   * 每个模块单独 Prompt 文档
   * 可以顺序执行，也可对完全独立模块进行并行执行

3. **集成闭环验证**

   * 完成单模块 TDD 后，进行模块间 Mock 集成测试
   * 确认流程原子性、异常路径、批次管理、状态 Snapshot 正确

4. **人工验证节点**

   * ScanInputService + ADB + Storage + UI 初版组合，可用真实设备进行小批量测试

---

## 二、模块执行顺序及提示

| 顺序 | 模块                      | Prompt 文件                                | 备注 / 测试优先级                        |
| -- | ----------------------- | ---------------------------------------- | --------------------------------- |
| 1  | ScanInputService        | ScanInputService_Cursor_Prompt.md        | 单元测试优先，事件触发、Reset、原子锁定            |
| 2  | AdbAccessService        | AdbAccessService_Cursor_Prompt.md        | 单元测试：多设备、开机等待、重试机制                |
| 3  | StorageService          | StorageService_Cursor_Prompt.md          | 单元测试：批次、SN 去重、导出 CSV / Excel      |
| 4  | BatchManager            | BatchManager_Cursor_Prompt.md            | 单元测试：批次生成、开始/结束逻辑                 |
| 5  | ProcessCoordinator      | ProcessCoordinator_Cursor_Prompt.md      | 单元测试：原子流程、Snapshot 状态驱动、异常路径      |
| 6  | VerificationFlowService | VerificationFlowService_Cursor_Prompt.md | 单元测试：对外接口封装、批次管理、异常处理             |
| 7  | LoggingService          | LoggingService_Cursor_Prompt.md          | 单元测试：占位接口，Info/Warn/Error 方法、批次轮换 |
| 8  | MES接口占位                 | MESInterface_Cursor_Prompt.md            | 单元测试：异步接口调用、失败缓存、人工干预提示           |
| 9  | UI / View               | UI_Cursor_Prompt.md                      | 单元测试：界面绑定 Snapshot，批次、检验区、日志区、状态栏 |
| 10 | AutoCheckButton         | AutoCheckButton_Cursor_Prompt.md         | 单元测试：触发完整流程模拟、ADB 可访问性、异常捕获       |

> 注：模块 1~6 是核心流程，可顺序执行；模块 7~10 可在条件允许下并行生成，因为大多不影响核心流程逻辑。

---

## 三、执行建议

1. **顺序执行**

   * 推荐顺序：ScanInputService → AdbAccessService → StorageService → BatchManager → ProcessCoordinator → VerificationFlowService → LoggingService → MES接口占位 → UI / View → AutoCheckButton
   * 优点：核心流程先稳住，后续界面与日志可插拔

2. **并行执行**

   * 可对完全独立模块（LoggingService、MES接口占位、UI / View、AutoCheckButton）进行并行生成，提高效率
   * 注意依赖关系：UI/AutoCheckButton 最好在核心流程 Prompt 完成后再生成，以便绑定 Snapshot

3. **TDD 验证**

   * 每个模块生成完 Prompt → 写单元测试 → 先跑 Mock 测试 → 再生成实现代码
   * 遇到异常场景及时调整 Prompt 或接口设计

4. **集成测试节点**

   * 核心流程（ScanInputService + AdbAccessService + StorageService + ProcessCoordinator + VerificationFlowService）完成单元测试后进行 Mock 集成测试
   * 确认流程原子锁、异常处理、批次管理、Snapshot 状态更新正确

---

## 四、文件存放建议

* 所有模块 Prompt 文档：

```
docs/ai_prompts/Phase2/<模块名>_Cursor_Prompt.md
```

* Phase 2 总文档参考：

```
docs/Phase2/SnVerify_Phase2_Requirement_And_UI_Design.md
```

* 执行计划表存档：

```
docs/Phase2/Phase2_Cursor_Execution_Plan.md
```
