# LoggingService_Cursor_Prompt.md

> 模块：LoggingService
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/LoggingService_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 提供 Info/Warn/Error 日志接口
  * 批次轮换管理日志文件
  * 压缩或淘汰老日志（可配置策略）
  * 提供 Snapshot 状态供 UI/ViewModel 绑定
* **边界**：

  * 不操作 UI / Dispatcher / Application
  * 不参与流程逻辑或存储校验结果

---

## 二、模块目标

* **输入**：日志消息、批次号、日志级别
* **输出**：写入日志文件、更新 Snapshot
* **功能点**：

  1. 批次开始 → 新日志文件创建，文件名包含批次名
  2. 支持 Info/Warn/Error 日志记录
  3. 文件轮换、压缩或按时间淘汰老日志
  4. Snapshot 提供当前日志文件名和状态
* **接口**：

  * `void LogInfo(string message)`
  * `void LogWarn(string message)`
  * `void LogError(string message)`
  * `LoggingSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：日志管理、文件轮换、异常处理
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 反映当前日志状态（只读、可绑定）
  * 事件仅用于一次性事实（日志写入完成）
* **线程与调度**：

  * 支持多线程写入，线程安全
  * 禁止使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`CurrentLogFile`, `BatchId`, `LastMessage`, `ErrorMessage`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. Info/Warn/Error 日志写入 → Snapshot 更新
  2. 批次轮换测试 → 新日志文件生成
* **异常路径测试**：

  1. 文件不可写 → Snapshot 标记错误
  2. 批次未创建 → Snapshot 标记异常
* **边界条件**：

  * 多线程并发写入测试
  * 文件大小超过阈值轮换测试
* **Mock 外部依赖**：文件系统模拟

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
2. 生成 LoggingService 实现代码
3. Snapshot 对象和异常标记完整
4. 支持批次轮换、压缩/淘汰策略

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 日志轮换、压缩或淘汰机制有效
* 异常路径可正确捕获并可复现
