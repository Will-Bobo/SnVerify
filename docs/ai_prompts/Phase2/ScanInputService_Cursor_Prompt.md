# ScanInputService_Cursor_Prompt.md

> 模块：ScanInputService
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/ScanInputService_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 监听单/多扫码枪输入
  * 原子触发校验流程（ProcessCoordinator）
  * 忽略非法字符和空格
  * Reset 支持，为下一次扫描准备
  * 提供 Snapshot 状态给 ViewModel/UI 层绑定
* **边界**：

  * 不操作 UI / Dispatcher / Application
  * 不处理流程逻辑，只触发事件和更新 Snapshot

---

## 二、模块目标

* **输入**：扫码枪字符串（可包含 `\r\n` 结束符）
* **输出**：触发 ProcessCoordinator 流程，更新 Snapshot
* **功能点**：

  1. 原子触发机制，防止重复触发
  2. 异常处理：重复 SN、空输入、扫码枪断开
  3. 支持多扫码枪输入（默认单扫码枪，支持未来扩展）
* **接口**：

  * `void OnScanInput(string sn)`
  * `void Reset()`
  * `ScanSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：触发流程、更新 Snapshot
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot 给 ViewModel 绑定
* **状态 vs 事件**：

  * Snapshot 只反映当前状态（可读、可绑定）
  * 事件仅用于一次性事实（触发流程）
* **线程与调度**：

  * 禁止使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`IsProcessing`, `LastScanSN`, `ErrorMessage`, `BatchId`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 单扫码枪输入正常 SN → Snapshot 更新 → 触发流程
  2. 批次 ID 正确记录
* **异常路径测试**：

  1. 空输入 → Snapshot 不更新，触发错误状态
  2. 重复 SN → Snapshot 标记重复错误
  3. 扫码枪断开 → Snapshot 标记异常
* **边界条件**：

  * 多扫码枪输入顺序触发测试
  * Reset 后可以重新扫描
* **Mock 外部依赖**：ProcessCoordinator 模拟，确保 ScanInputService 独立可测

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

1. 生成单元测试覆盖所有正常、异常、边界场景
2. 生成 ScanInputService 实现代码
3. Snapshot 对象和事件触发接口完整
4. 支持批次 ID 关联和 Reset 功能

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 原子触发机制有效
* 异常路径正确标记并可复现
* Reset 方法能清理状态并允许下一次扫描
