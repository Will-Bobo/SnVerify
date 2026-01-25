# MESInterface_Cursor_Prompt.md

> 模块：MESInterface
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/MESInterface_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 提供 MES 接口占位，支持校验结果上传和状态查询
  * 异步调用接口，失败时缓存数据并提示人工干预
  * 提供 Snapshot 状态给 UI/ViewModel 绑定
* **边界**：

  * 不处理 ScanInputService / ADB / Storage 内部逻辑
  * 不直接操作 UI / Dispatcher / Application

---

## 二、模块目标

* **输入**：SN 校验结果、批次信息
* **输出**：模拟 MES 返回结果（成功/失败）、更新 Snapshot
* **功能点**：

  1. 异步上传校验结果
  2. 上传失败 → 缓存数据，提示人工干预
  3. Snapshot 提供接口调用状态、错误信息、批次 ID
* **接口**：

  * `Task<MESResult> UploadTestResultAsync(SNResult result)`
  * `List<SNResult> GetCachedResults()`
  * `MESSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：接口调用、失败缓存、异常处理
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 反映当前 MES 接口状态
  * 事件仅用于一次性事实（接口调用完成/异常）
* **线程与调度**：

  * 异步任务管理，多线程安全
  * 禁止使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`IsProcessing`, `LastResultStatus`, `ErrorMessage`, `BatchId`, `CachedCount`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 上传成功 → Snapshot 更新为成功
  2. 上传失败 → Snapshot 更新错误信息，数据缓存
* **异常路径测试**：

  1. 网络异常 → Snapshot 标记错误并缓存
  2. 多次上传失败 → 缓存数据累积
* **边界条件**：

  * 批次为空
  * 大量缓存数据上传验证
* **Mock 外部依赖**：MES 接口模拟

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
2. 生成 MESInterface 占位实现代码
3. Snapshot 对象和异常标记完整
4. 异步上传与失败缓存逻辑完整

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 上传失败数据缓存正确，人工提示可用
* 异常路径可正确捕获并可复现
