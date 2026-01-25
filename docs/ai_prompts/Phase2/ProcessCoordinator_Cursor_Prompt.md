# ProcessCoordinator_Cursor_Prompt.md

> 模块：ProcessCoordinator
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/ProcessCoordinator_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 串联 ScanInputService、AdbAccessService、StorageService 模块
  * 管理流程原子性，保证单 SN 校验流程不可中断
  * 更新 Snapshot 状态给 ViewModel/UI 层绑定
  * 异常处理和流程锁定管理
* **边界**：

  * 不直接操作 UI / Dispatcher / Application
  * 不处理 Storage 内部写入逻辑或 ADB 具体命令

---

## 二、模块目标

* **输入**：SN 字符串、批次信息
* **输出**：校验结果状态、Snapshot 更新
* **功能点**：

  1. 原子流程管理：单 SN 校验流程锁定期间丢弃其他输入
  2. 调用 ScanInputService → AdbAccessService → StorageService → VerificationFlowService
  3. 异常处理：重复 SN、ADB/Storage 异常、超时
  4. Snapshot 提供：流程状态、错误信息、当前 SN、批次 ID
* **接口**：

  * `Task<ProcessResult> StartVerificationAsync(string sn)`
  * `void Reset()`
  * `ProcessSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：流程编排、状态管理、异常捕获
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 显示当前流程状态和错误信息
  * 事件仅用于一次性事实（流程完成/异常）
* **线程与调度**：

  * 流程内部异步任务管理，多线程安全
  * 禁止直接使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`IsProcessing`, `CurrentSN`, `ResultStatus`, `ErrorMessage`, `BatchId`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 单 SN 流程顺利执行 → Snapshot 更新，结果为 PASS
  2. 批次信息正确关联
* **异常路径测试**：

  1. 重复 SN → Snapshot 标记错误
  2. ADB 异常 → Snapshot 错误信息
  3. Storage 写入异常 → Snapshot 错误信息
  4. 超时 → Snapshot 标记超时错误
* **边界条件**：

  * 多扫码枪输入触发并发流程测试
  * 流程 Reset 后重新触发
* **Mock 外部依赖**：ScanInputService, AdbAccessService, StorageService, VerificationFlowService 模拟

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
2. 生成 ProcessCoordinator 实现代码
3. Snapshot 对象和异常标记完整
4. 流程原子锁有效，异常路径正确处理

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 原子流程锁有效，其他输入在锁定期间被丢弃
* 异常路径可正确捕获并可复现
* 批次 ID 正确关联
