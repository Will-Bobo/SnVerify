# Export_Semantics.md

> 文档类型：Architecture Semantics（导出语义定义）  
> 适用阶段：Phase 2.5 – Version / SN 双检验并存  
> 目标：明确 **“什么情况下应该产生导出结果”**，避免空导出、脏导出和语义歧义。

---

## 1. 文档目的

本文件用于定义 SnVerify 系统中 **导出（Export）行为的业务语义边界**，明确：

- 什么时候允许生成导出文件  
- 什么时候必须跳过导出  
- Storage / Aggregation / UI 各层的职责边界  

防止出现：

- 空 Excel / 空 TXT  
- “导出成功但没有任何业务数据”的假象  
- SN / Version 混合模式下的导出污染  

---

## 2. 导出层级与职责

### 2.1 UI 层（ViewModel / Dialog）

- 负责：  
  - 让用户选择导出维度（项目 / 订单）  
  - 让用户选择导出记录类型（SN / Version / All）  
- 不负责判断：  
  - 是否真的有可导出的记录  

### 2.2 Aggregation 层（ExportAggregationService）

- 负责：  
  - 根据项目 / 订单查找 Session 列表  
  - 逐 Session 调用 Storage 导出  
- 不负责：  
  - 判断 Session 是否“有业务数据”  

### 2.3 Storage 层（IStorageService.ExportBySessionAsync）

**唯一有权决定是否生成导出文件的层级**：

- 必须基于：
  - Session  
  - ExportRecordFilter（SN / Version / All）  
- 判断：
  - 在当前过滤条件下，是否存在可导出的 TestRecord  

---

## 3. 导出语义规则（冻结）

### 3.1 空记录规则（核心）

当且仅当满足以下条件之一时，Storage 层 **必须跳过导出，不生成任何文件**：

- Session 下不存在任何 TestRecord  
- Session 下存在 TestRecord，但在 ExportRecordFilter 过滤后：
  - 记录数为 0  

示例：

- Session 只有 SN 检验记录  
- 用户选择 “仅导出 Version 检验”  
→ 必须跳过该 Session 的导出  

---

## 4. 文件生成规则

### 4.1 允许的情况

- 过滤后存在 ≥ 1 条 TestRecord  
- 即使：
  - 只有 PASS  
  - 或只有 FAIL / TIMEOUT  
→ 仍然允许生成导出文件  

### 4.2 不允许的情况

- PASS 与 FAIL 均为空  
- 不生成空 Sheet / 空统计文件  

### 4.3 Sheet 规则（继承 Storage_Assumptions）

- Excel 文件包含：
  - PASS Sheet  
  - FAIL Sheet  
- Sheet 可为空，但文件本身不允许“无任何业务记录”  

---

## 5. 日志与用户反馈

### 5.1 Storage 层

- 当跳过导出时：
  - 必须记录 Info 日志  
  - 示例：
    - “Session=xxx 在当前过滤条件下无记录，跳过导出”  

### 5.2 UI 层（Phase 3 可选）

- 当所有 Session 均被跳过时：
  - 可提示用户：
    - “所选范围内无可导出的记录”  

---

## 6. 测试约束（TDD）

以下测试必须存在：

- Session 无任何 TestRecord → 不生成文件  
- Session 有记录，但过滤后为空 → 不生成文件  
- Session 有记录，过滤后非空 → 正常导出  

> 任何未来修改导出逻辑的代码，必须通过以上测试  

---

## 7. 与 Storage_Assumptions.md 的关系

- Storage_Assumptions.md 定义 **数据事实源约束**  
- 本文档定义 **导出行为语义**  
- 在 `Storage_Assumptions.md` 最后可引用：
  > 导出行为的具体语义与边界，请参考：  
  > docs/Architecture/Export_Semantics.md  

---
