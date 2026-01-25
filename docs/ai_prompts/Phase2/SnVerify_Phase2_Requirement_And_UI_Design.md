# SnVerify Phase 2 需求与 UI 初版设计

> 目的：基于 Phase 1 的最小闭环，为 Phase 2 生成完整模块清单、Cursor Prompt 草稿和初步 UI 设计方案，支持后续开发与测试。

---

## 一、模块清单与目标

| 模块                                           | 目标                    | 备注                              |
| -------------------------------------------- | --------------------- | ------------------------------- |
| ScanInputService                             | 支持多扫码枪输入，原子触发流程       | 保持原子性，事件驱动，Reset 支持             |
| ProcessCoordinator / VerificationFlowService | 多工位流程编排，原子锁定，异常处理     | Snapshot 状态驱动，TDD 优先            |
| AdbAccessService                             | 多设备访问，超时与重试机制         | 支持设备开机等待，ADB命令顺序执行              |
| StorageService                               | 批次数据存储，SN 去重，结果导出     | 日志文件按批次轮换，CSV / Excel 分表        |
| BatchManager                                 | 批次管理，自动生成批次号，开始/结束批次  | 批次号默认时间命名 batch_YYYYMMDD_HHMMSS |
| LoggingService                               | 日志落地接口                | 占位接口，未来文件/UI落地，可轮换压缩            |
| MES接口占位                                      | 异步上报和接口调用             | Phase 2 后期集成，可人工测试替代            |
| UI / View                                    | 显示检验状态、批次、测试记录、日志、状态栏 | 界面简约大方，易操作员理解                   |
| AutoCheckButton                              | 测试按钮，触发完整流程模拟         | 检查 ADB 设备可访问性                   |

---

## 二、Cursor Prompt 草稿策略

1. **ScanInputService Prompt**

   * 支持多扫码枪输入
   * 原子事件触发
   * Reset 方法
   * Mock 测试覆盖原子锁、异常输入、连续输入

2. **ProcessCoordinator / VerificationFlowService Prompt**

   * 多工位流程原子化
   * Snapshot 状态驱动 UI
   * 异常处理路径测试
   * Mock ScanInputService、ADB、StorageService、MES

3. **AdbAccessService Prompt**

   * 多设备访问检测
   * 开机等待机制 + 重试 + 超时处理
   * 异常路径单元测试覆盖

4. **StorageService Prompt**

   * 批次数据存储，SN 去重逻辑
   * 导出 CSV / Excel，分表 PASS / FAIL
   * Mock 测试导出正确性

5. **BatchManager Prompt**

   * 批次号生成策略
   * 批次开始/结束逻辑
   * 对应 StorageService 与日志接口集成

6. **LoggingService Prompt**

   * 占位接口，提供 Info / Warn / Error 方法
   * 支持批次轮换与压缩策略

7. **MES接口占位 Prompt**

   * 异步调用接口，失败缓存、人工干预提示
   * Mock 测试 MES 调用逻辑

8. **UI / View Prompt**

   * 扫码输入文本框、检验状态区域、批次管理、测试记录区、日志区、状态栏
   * 默认布局简约，操作员易操作
   * PASS / FAIL 状态明确，失败显示详细原因

9. **AutoCheckButton Prompt**

   * 调用完整流程模拟
   * 测试 ADB可访问性、流程原子锁、异常处理路径

---

## 三、UI 初版设计（简约大方）

### 3.1 窗口属性

* Title: Smartke SN校验程序
* 尺寸: H=800, W=1300

### 3.2 布局设计

| 区域     | 位置    | 描述                                     |
| ------ | ----- | -------------------------------------- |
| 批次区    | 窗口上方  | 批次输入框，开始/结束按钮，默认时间命名批次                 |
| 检验区    | 中央    | 当前检验状态显示：空闲 / 检验中，PASS / FAIL，失败显示详细错误 |
| 文本框    | 检验区下方 | 扫码枪输入，触发流程，检验完成自动清空                    |
| 测试记录区  | 可折叠   | 每批次记录查看，约 3k 条数据以上，默认关闭                |
| 日志区    | 主窗口下方 | 日志显示区，可折叠、搜索，默认关闭                      |
| 状态栏    | 窗口底部  | 系统全局状态，批次状态，异常提示                       |
| 自动检验按钮 | 检验区右侧 | 手动触发完整流程模拟                             |

### 3.3 交互说明

* 扫码枪输入自动触发流程 → Snapshot 更新 → 检验状态显示
* 批次开始按钮 → 新批次创建 → 日志轮换
* 批次结束按钮 → 当前批次结束 → 导出 CSV / Excel
* AutoCheckButton → 手动触发流程检查 ADB 可访问性
* 异常显示统一弹窗提示 + 日志记录

---

## 四、技术验证点 / 风险点

1. 多扫码枪并发输入 → 测试原子触发机制是否可靠
2. 多ADB设备 → 弹警告，验证流程是否正确
3. 扫码枪触发机制 → 流程锁定、输入丢失问题
4. 日志轮换与导出 → 文件名、压缩、批次关联
5. 批次管理 → 批次号正确生成，批次内 SN 去重逻辑
6. UI操作可用性 → 操作员易用，状态显示清晰
7. ADB设备开机等待机制 → 测试流程是否稳定
8. MES接口占位 → 异常处理和重试机制验证
9. 自动检验按钮 → 测试 ADB访问、流程锁定和异常捕获

---

> 备注：Phase 2 仍然保留 Phase 1 的架构约束和 TDD 流程，所有模块均遵守 Service 层职责、Snapshot 状态驱动、事件仅用于一次性事实、MVVM分层约束，UI 与流程逻辑解耦。

下一步可以基于此文档逐个生成 **Phase 2 Cursor Prompt**，然后开始编写单元测试覆盖初版逻辑。
