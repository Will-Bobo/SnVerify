# SnVerify Phase 2 Cursor Prompt 清单

> 目的：为 Phase 2 各模块生成可直接供 Cursor Agent 执行的 Prompt 列表，确保 TDD 优先、模块化、可控

---

## 一、存放位置建议

* 路径：`docs/ai_prompts/Phase2/`
* 文件命名示例：`ScanInputService_Cursor_Prompt.md` 等，每个模块一个文档
* Cursor Agent 访问路径：

```
docs/ai_prompts/Phase2/<模块名>_Cursor_Prompt.md
```

* 保留 Phase 2 总文档 `SnVerify_Phase2_Requirement_And_UI_Design.md` 用于阅读参考

---

## 二、模块清单与 Prompt 文件名

| 模块                      | Prompt 文件名                               | 描述                                               |
| ----------------------- | ---------------------------------------- | ------------------------------------------------ |
| ScanInputService        | ScanInputService_Cursor_Prompt.md        | 支持多扫码枪输入、原子触发、Reset 方法、TDD 测试要求                  |
| ProcessCoordinator      | ProcessCoordinator_Cursor_Prompt.md      | 多工位流程原子化、Snapshot 状态驱动、异常处理、TDD                  |
| VerificationFlowService | VerificationFlowService_Cursor_Prompt.md | 对外封装 ProcessCoordinator 接口、批次管理、状态 Snapshot、异常处理 |
| AdbAccessService        | AdbAccessService_Cursor_Prompt.md        | 多设备访问、开机等待、重试机制、超时处理、异常路径单元测试                    |
| StorageService          | StorageService_Cursor_Prompt.md          | 批次数据存储、SN 去重、结果导出、日志占位接口                         |
| BatchManager            | BatchManager_Cursor_Prompt.md            | 批次号生成策略、批次开始/结束逻辑、与 StorageService/日志接口集成        |
| LoggingService          | LoggingService_Cursor_Prompt.md          | 占位日志接口 Info/Warn/Error，批次轮换、压缩策略                 |
| MES接口占位                 | MESInterface_Cursor_Prompt.md            | 异步调用接口、失败缓存、人工干预提示、Mock 测试                       |
| UI / View               | UI_Cursor_Prompt.md                      | 界面布局、状态显示、操作员可操作性、批次/检验区/日志区/状态栏                 |
| AutoCheckButton         | AutoCheckButton_Cursor_Prompt.md         | 手动触发完整流程模拟，测试 ADB 可访问性、流程锁定、异常捕获                 |

---

## 三、使用说明

1. **每个模块一个 Prompt 文档**：Cursor Agent 读取单个模块 Prompt 生成对应模块代码和单元测试
2. **TDD 测试优先**：先生成单元测试，Mock 外部依赖，再生成实现代码
3. **严格遵守架构规则**：MVVM 分层、Service 层职责、Snapshot 状态驱动、事件只用于一次性事实
4. **UI 与流程逻辑解耦**：Cursor Agent 不生成 UI 控件逻辑，只生成可绑定的 Snapshot / 数据接口
5. **存档与参考**：总文档 `SnVerify_Phase2_Requirement_And_UI_Design.md` 用于阅读和上下文参考
