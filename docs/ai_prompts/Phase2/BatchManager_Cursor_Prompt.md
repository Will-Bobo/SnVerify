# BatchManager_Cursor_Prompt.md

> 模块：BatchManager
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/BatchManager_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 批次管理（创建、开始、结束）
  * 批次号生成（默认使用时间命名 batch_YYYYMMDD_HHMMSS）
  * 与 StorageService 和日志系统集成
  * 提供 Snapshot 状态给 ViewModel/UI 层绑定
* **边界**：

  * 不操作 UI / Dispatcher / Application
  * 不处理流程原子性逻辑，只提供批次管理接口

---

## 二、模块目标

* **输入**：用户操作（开始/结束）、当前时间
* **输出**：批次对象，关联日志与 StorageService
* **功能点**：

  1. 批次创建、开始、结束
  2. 批次号唯一，默认时间命名，可自定义
  3. 与 StorageService 写入记录关联
  4. Snapshot 更新批次状态
* **接口**：

  * `Batch CreateBatch(string batchName = null)`
  * `void StartBatch(string batchId)`
  * `void EndBatch(string batchId)`
  * `BatchSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：批次管理，状态维护，异常处理
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 反映当前批次状态（只读、可绑定）
  * 事件仅用于一次性事实（批次开始/结束触发）
* **线程与调度**：

  * 多线程调用安全
  * 禁止使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 字段：`BatchId`, `BatchName`, `IsActive`, `ErrorMessage`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 创建批次 → Snapshot 更新 → 与 StorageService 正确关联
  2. 批次开始/结束操作 → 状态正确更新
* **异常路径测试**：

  1. 重复批次号 → Snapshot 标记错误
  2. 结束未开始批次 → Snapshot 标记异常
* **边界条件**：

  * 批次号为空、最大长度测试
  * 多线程创建批次验证
* **Mock 外部依赖**：StorageService、LoggingService 模拟

---

## 五、代码规范

* 所有 public 类 / 方法必须有 XML 注释
* 文件头标注 AI 生成：

```csharp
/// <author>
/// AI Assistant
/// </author>
```

* 禁止直接修改 View 或 UI 控件
* 命名规范严格遵守架构规则

---

## 六、输出要求

1. 生成单元测试覆盖正常、异常、边界场景
2. 生成 BatchManager 实现代码
3. Snapshot 对象和异常标记完整
4. 支持批次开始/结束、批次号生成和 StorageService 关联

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 批次号唯一，开始/结束操作正确
* 异常路径可正确捕获并可复现
