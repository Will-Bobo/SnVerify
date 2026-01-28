# Phase 2.5 – Step 2 Cursor Agent Self-Check Prompt

> 目标：在 Cursor Agent 执行 Phase 2.5 Step 2（ViewModel + UI 行为 + 流程逻辑）时，自行校验生成内容是否符合冻结约束和设计规则。

---

## 自检规则（12 条）



### 1️⃣ MVVM 分层遵守
- ViewModel 仅处理状态逻辑，不操作 UI 控件
- UI 通过绑定或事件接收状态，不直接调用 Domain/Service
- 禁止硬编码 UI 更新逻辑

### 2️⃣ Session / Order / Project 不被修改
- Step 1 中冻结的数据结构不可被 UI / ViewModel 修改
- UI 只能显示、绑定，不产生业务修改

### 3️⃣ 开始 / 结束按钮防抖
- 重复点击 Start / End 按钮时：
  - 检查是否生成过 TestRecord
  - 未生成 TestRecord → 判定为重复点击
  - 日志或事件记录被忽略操作

### 4️⃣ 自动 / 手动检验按钮
- 自动检验等待当前 SN 处理完成（IsProcessing false）
- 手动检验触发时可立即处理输入 SN
- ScanInputService 不直接被 UI 操作，需通过事件触发

### 5️⃣ 状态栏更新
- 显示当前状态：
  - 待测 / 测试中 / 完成
- Post-Report 异常弱提示（小字或状态栏，不阻塞流程）
- 状态更新仅通过 ViewModel 绑定或事件触发

### 6️⃣ 错误提示区
- PASS / FAIL 高亮显示
- FAIL 信息以轻量文字显示
- Post-Report MES 异常仅通过事件或日志显示，不影响业务结果

### 7️⃣ 日志区域
- 默认隐藏，可手动打开
- 仅显示当前 Session 最近的 N 条记录（如 1000 条）
- 不影响测试流程
- 日志信息必须正确绑定 Session / Order / Project

### 8️⃣ 防止并发与状态冲突
- 所有 UI 操作需等待当前 SN 或 Session 状态完成
- 检查 IsProcessing 与按钮状态一致性
- 自检按钮与主检验按钮互斥

### 9️⃣ 单元测试覆盖
- ViewModel 状态更新测试
- 按钮行为测试（Start/End/AutoCheck）
- 异常提示测试（Post-Report MES 异常）
- 防抖逻辑测试

### 🔟 容错行为
- 输入错误 SN / 重复 SN 时：
  - UI 不阻塞
  - 错误记录仅记录一次
  - 日志和事件保持一致

### 11️⃣ UI 无侵入 Domain
- UI 或 ViewModel 不直接修改 TestRecord / Session / Order
- 所有业务逻辑仍在 Service / Gate 层

### 12️⃣ 文档与注释
- 所有 public 方法和类必须有 XML 注释
- 明确标注 AI 生成和自检点
- 所有 TODO 或可扩展接口必须注明用途，不得绕过自检规则

---

## 使用方法

1. Cursor Agent 在生成 Step 2 代码后，自动检查每条规则是否被满足
2. 不满足规则的部分必须在代码注释中标注 TODO 或 Issue
3. 所有规则通过后，Step 2 才算完成，可进入 Step 3（UI 重构 / 拟物化 PASS/FAIL）

