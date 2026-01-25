# StorageService_Cursor_Prompt.md

> 模块：StorageService
> 目标：Phase 2 完整执行级 Cursor Prompt
> 存放路径：`docs/ai_prompts/Phase2/StorageService_Cursor_Prompt.md`

---

## 一、角色与职责

* **角色**：Cursor Agent 为主要开发者
* **职责**：

  * 管理 SQLite 数据库存储 SN 校验结果
  * 批次管理、SN 去重
  * 导出 CSV / Excel（PASS / FAIL 分表）
  * 提供 Snapshot 状态给 ViewModel/UI 层绑定
* **边界**：

  * 不操作 UI / Dispatcher / Application
  * 不处理流程原子性逻辑，只提供存储接口和状态

---

## 二、模块目标

* **输入**：SN 校验结果、批次号、错误信息
* **输出**：写入 SQLite 数据库、导出 CSV/Excel 文件、更新 Snapshot
* **功能点**：

  1. 单表管理当前批次 SN 校验结果
  2. SN 去重：同一批次不允许重复 SN
  3. 批次管理：支持开始/结束批次
  4. 导出功能：按批次生成 CSV / Excel，PASS/FAIL 分表
* **接口**：

  * `void SaveResult(SNResult result)`
  * `List<SNResult> QueryBatch(string batchId)`
  * `void ExportBatch(string batchId)`
  * `StorageSnapshot Snapshot { get; }`

---

## 三、架构与约束

* **Service 层职责**：存储、查询、导出逻辑，异常处理
* **MVVM 分层约束**：不直接操作 UI，只提供 Snapshot
* **状态 vs 事件**：

  * Snapshot 反映存储状态（可读、可绑定）
  * 事件仅用于一次性事实（写入完成、导出完成）
* **线程与调度**：

  * 支持多线程写入，线程安全
  * 禁止使用 Dispatcher / Application.Current / Thread / Task.Run
* **Snapshot 对象定义**：

  * 属性只读、不可变
  * 包含字段：`IsProcessing`, `LastSavedSN`, `BatchId`, `ErrorMessage`

---

## 四、单元测试要求（TDD）

* **正常路径测试**：

  1. 写入 SN 校验结果 → 数据库成功更新
  2. 导出批次 → CSV / Excel 文件生成
* **异常路径测试**：

  1. 重复 SN → Snapshot 错误信息
  2. 数据库写入失败 → Snapshot 错误信息
  3. 批次导出失败 → Snapshot 错误信息
* **边界条件**：

  * 批次内 0 条、3k 条以上记录测试
  * 多线程写入验证
* **Mock 外部依赖**：数据库、文件系统模拟

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
2. 生成 StorageService 实现代码
3. Snapshot 对象和异常标记完整
4. 支持批次管理、SN 去重、CSV/Excel 导出

---

## 七、完成判定标准

* 单元测试全部通过
* Snapshot 属性更新正确且只读
* 批次 SN 去重正确执行
* 导出文件生成正确，PASS/FAIL 分表
