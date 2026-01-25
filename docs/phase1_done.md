# SnVerify Phase 1 文档包整理

> 目的：整理 Phase 1 所有文档、模块及测试结果，形成团队交付与存档使用的完整资料。

---

## 1. 模块列表及状态

| 模块                      | Cursor Prompt 文档                         | 单元测试状态 | 描述                                                                        |
| ----------------------- | ---------------------------------------- | ------ | ------------------------------------------------------------------------- |
| StorageService          | StorageService_Cursor_Prompt.md          | ✅ 通过   | SQLite 唯一事实源，批次字段，SN 去重，导出接口占位                                            |
| AdbAccessService        | ADB_Module_Cursor_Prompt.md              | ✅ 通过   | 调用 adb.exe，顺序命令，重试机制，超时处理                                                 |
| ScanInputService        | ScanInputService_Cursor_Prompt.md        | ✅ 通过   | 扫码枪输入，                                                                    |
| \n 触发事件，原子锁定，Reset 方法   |                                          |        |                                                                           |
| ProcessCoordinator      | ProcessCoordinator_Cursor_Prompt.md      | ✅ 通过   | 原子流程串联：ScanInputService → AdbAccessService → StorageService，Snapshot 状态驱动 |
| VerificationFlowService | VerificationFlowService_Cursor_Prompt.md | ✅ 通过   | 对外统一接口封装 ProcessCoordinator，支持 Snapshot、StartVerificationAsync、Reset      |

---

## 2. 架构与技术规则文档

* 06_Architecture_Technical_Rules.md

  * MVVM 分层约束
  * Service 层职责定义
  * 状态 vs 事件规则
  * Command 刷新规范
  * UI 线程封送规则

* 07_Technical_Architecture_and_Dev_Guide.md

  * 项目整体技术架构
  * SQLite 选型与存储策略
  * 流程原子性与锁定设计
  * TDD 流程规范
  * Cursor Agent 使用规则

---

## 3. 需求与假设文档

* SnVerify_Requirement_Overview_For_Team.md

  * 核心目标与价值
  * Phase 1 范围说明
  * 技术验证点和不确定点
  * 批次与 SN 校验要求

* ADB_Access_Assumptions.md

  * adb 命令顺序说明
  * 重试与超时规则
  * 工具路径规范

* Storage_Assumptions.md

  * SQLite 最小表设计
  * 批次概念、SN 去重要求
  * 导出规则（CSV，两个 sheet：PASS / FAIL）

* Phase1_Task_List.md

  * 任务清单，模块输入输出，测试点
  * 批次处理和 SN 重复校验
  * 按批次导出 CSV

---

## 4. 参考开源项目文档

* Reference Open Source: 供 Cursor Agent 长期参考

  * Moq / NUnit: 测试模板与 TDD 方法
  * mvvm-samples / mvvmlight: MVVM 分层与解耦思想
  * LiteDB: 嵌入式 DB 思路（概念参考）
  * Prism: 理解思想，但 Phase 1 不引入

---

## 5. Phase 1 测试总结

* 所有模块单元测试通过
* Mock 测试覆盖：逻辑正确性、流程原子性、异常路径
* Snapshot 对象验证通过（只读 / 可绑定 / 可导出）
* ProcessCoordinator 与 VerificationFlowService 测试通过
* 日志接口占位已准备好，后续 UI / 产线阶段落地

---

## 6. Phase 1 冻结结论

* 所有模块功能接口与逻辑冻结
* 技术架构冻结（WPF + MVVM + SQLite + TDD + Cursor Agent）
* Phase 2 可以在此基础上扩展 UI、场景测试、日志落地、产线验证

> Phase 1 文档包整理完毕，团队可用于交接、存档和 Phase 2 准备。
