# VerificationFlowService_Cursor_Prompt.md

> 模块：VerificationFlowService
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/VerificationFlowService_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 封装 ProcessCoordinator 提供统一接口
  * 管理批次状态、流程启动与复位
  * 更新 Snapshot 状态供 UI/ViewModel 绑定
  * 处理异常路径，确保流程可控
* **边界**：

  * 不直接操作 UI / Dispatcher / Application
  * 不处理 ScanInputService 或 AdbAccessService 内部逻辑

---

## 二、模块目标

* **输入**：SN 字符串、批次信息
* **输出**：流程结果状态、Snapshot 更新
* **功能点**：

  1. 提供统一接口 `StartVerificationAsync`, `Reset`, `GetSnapshot`
  2. 流程管理（串联 ScanInputService、AdbAccessService、StorageService）
  3. 异常处理（重复 SN、ADB/Storage 异常、超时）
  4. Snapshot 提供流程状态、错误信息、批次 ID
* **接口**：

  * `Task<VerificationResult> StartVerificationAsync(string sn)`
  * `void Reset()`
  * `VerificationSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：流程封装、批次管理、状态维护、异常捕获
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 显示当前流程状态和错误信息
  * 事件仅用于一次性事实（流程完成/异常）
* **线程与调度**：

  * 流程异步任务管理，多线程安全
  * 禁止直接使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`IsProcessing`, `CurrentSN`, `ResultStatus`, `ErrorMessage`, `BatchId`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 流程顺利执行 → Snapshot 更新，结果为 PASS
  2. 批次信息正确关联
* **异常路径测试**：

  1. 重复 SN → Snapshot 标记错误
  2. ADB/Storage 异常 → Snapshot 错误信息
  3. 超时 → Snapshot 标记超时错误
* **边界条件**：

  * 多扫码枪输入触发并发流程测试
  * Reset 后重新触发流程
* **Mock 外部依赖**：ProcessCoordinator、StorageService 模拟

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
2. 生成 VerificationFlowService 实现代码
3. Snapshot 对象和异常标记完整
4. 流程原子锁有效，异常路径正确处理
5. 批次 ID 正确关联

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 原子流程锁有效，其他输入在锁定期间被丢弃
* 异常路径可正确捕获并可复现
* 批次
