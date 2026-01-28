# Phase2.5_Step1_Cursor_Prompt.md

> 模块：Phase 2.5 – Step 1
> 目标：完整执行级 Cursor Prompt（模型 & 数据结构 & 抽象接口 & 单元测试）
> 存放路径：`docs/ai_prompts/Phase2/Phase2.5_Step1_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 构建 Project / Order / Session / TestRecord 模型
  * 实现 MES 抽象接口：IMesClient、IMesGate、事件通知机制
  * 设计 OperationGuard 或等效机制防止重复 Start/End
  * 生成完整单元测试覆盖所有正常、异常、边界场景
* **边界**：

  * 不操作 UI / ViewModel / WPF
  * 不做真实 MES 网络调用或文件 IO
  * 不接入 ScanInputService 事件

---

## 二、模块目标

* **输入**：Order / Project 信息、Session 触发信号、模拟 MES 上报
* **输出**：TestRecord / TestSession 数据、MES 事件（仅通知，不影响结果）
* **功能点**：

  1. 创建 Session，保证 SessionId 唯一且不可修改
  2. TestRecord 仅存事实，不含业务判断
  3. MES Pre-Gate / Post-Report 抽象接口，Post-Report 失败产生事件或日志
  4. OperationGuard 防抖，重复 Start/End 且无 TestRecord → 判定重复
  5. 去重规则仅在导出逻辑或视图层，不破坏原始数据
* **接口**：

  * `Task<MesConnectionStatus> CheckConnectionAsync()`
  * `Task<MesReportResult> ReportTestResultAsync(TestResultBundle bundle)`
  * `GateDecision EvaluatePreTest(TestContext context)`

---

## 三、架构与约束

* **Service 层职责**：管理 Session / TestRecord 逻辑、OperationGuard、MES Gate
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot 或事件通知
* **状态 vs 事件**：

  * Snapshot / 数据对象只反映当前状态，可读可绑定
  * 事件仅用于 Post-Report 异常通知
* **线程与调度**：

  * 禁止 Dispatcher / Application.Current / Task.Run
* **数据对象定义**：

  * 属性只读、不可变
  * Session: SessionId, ProjectName, OrderName, StartTime, EndTime
  * TestRecord: DeviceSn, Result, Timestamp, ErrorCode

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. Session 创建 / 关闭 → 数据不可变
  2. TestRecord 正常生成，PASS / FAIL 正确标记
  3. Gate 阻断/放行逻辑正确
* **异常路径测试**：

  1. MES Report 失败 → 事件生成但 TestRecord 不变
  2. 重复 Start/End → 防抖机制生效
* **边界条件**：

  * FAIL Sheet 去重，PASS 不去重
  * 多 MES 插件能力配置测试
* **Mock 外部依赖**：模拟 MES、Gate、Event 触发

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

1. 生成完整 Domain / Core 代码
2. 完整单元测试覆盖所有正常、异常、边界场景
3. MES 抽象接口和事件通知机制实现
4. Snapshot / 数据对象不可变，属性只读
5. OperationGuard 防抖逻辑完整

---

## 七、完成判定标准

* 单元测试全部通过
* Session / TestRecord 数据正确且不可修改
* Gate 阻断 / 放行行为符合预期
* MES Post-Report 异常事件生成正确
* 防抖 / 重复 Start/End 判定正确
* FAIL Sheet 去重逻辑符合要求
