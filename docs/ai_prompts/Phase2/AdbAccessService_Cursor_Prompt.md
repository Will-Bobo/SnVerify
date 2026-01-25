# AdbAccessService_Cursor_Prompt.md

> 模块：AdbAccessService
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/AdbAccessService_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 访问单/多ADB设备获取设备 SN
  * 顺序执行 `ylzero` → `getprop sys.skyroam.osi.sn`
  * 处理超时、重试、异常
  * 提供 Snapshot 状态给 ViewModel/UI 层绑定
* **边界**：

  * 不操作 UI / Dispatcher / Application
  * 不触发 ScanInputService 事件

---

## 二、模块目标

* **输入**：设备列表、SN 请求、批次信息
* **输出**：SN_ADB 或超时失败，更新 Snapshot
* **功能点**：

  1. 单设备访问，返回 SN
  2. 多设备接入 → 弹出警告 Snapshot
  3. 超时机制：默认 5~10 秒，可重试 3 次，间隔 1 秒
  4. 错误处理：设备未连接、命令执行失败、设备开机延时
* **接口**：

  * `Task<string> GetDeviceSNAsync(string deviceId)`
  * `bool CheckMultipleDevices(out List<string> deviceIds)`
  * `AdbSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：管理 ADB 访问、异常处理、重试机制
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 只反映当前状态（可读、可绑定）
  * 事件仅用于一次性事实（设备检测、命令完成）
* **线程与调度**：

  * 禁止使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`IsProcessing`, `LastSN`, `ErrorMessage`, `DeviceIds`, `BatchId`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 单设备正常访问 → 返回正确 SN
  2. 多设备存在 → Snapshot 更新并标记警告
* **异常路径测试**：

  1. 设备未连接 → Snapshot 错误信息
  2. ADB命令失败 → Snapshot 标记异常
  3. 超时 → Snapshot 标记超时错误
* **边界条件**：

  * 多设备同时访问测试
  * 重试机制验证
* **Mock 外部依赖**：模拟设备响应和命令执行

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
2. 生成 AdbAccessService 实现代码
3. Snapshot 对象和异常标记完整
4. 支持批次 ID 关联和多设备警告机制

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 超时与重试机制正确执行
* 多设备接入触发警告正确
* 错误路径可正确捕获并可复现
