# Storage_Assumptions.md

> 文档类型：Architecture Assumption（阶段性冻结）
> 适用阶段：Phase 1 – Minimal Closed Loop
> 目标：为 SnVerify 项目在 Phase 1 阶段提供**本地唯一事实源存储约束**，防止多源冲突和实现歧义。

---

## 1. 目的

* 确保所有 SN 校验结果均被**持久化保存**。
* SQLite 作为**唯一事实源**，所有 PASS / FAIL / TIMEOUT 结果必须首先写入 SQLite。
* CSV / Excel 仅用于**导出展示**，不作为事实源。
* 为 Phase 2 扩展（如 MES 对账、报表分析）提供可靠数据基础。
* **支持批次概念**，方便按生产批次统计、查询与导出。


- SQLite 作为 **本地唯一事实源**
- 所有 SN 校验结果必须持久化
- 日志不作为业务事实，仅用于问题排查
- Phase 1 数据规模预期：≤ 100,000 条记录
- 使用 **单 SQLite 文件**，不拆库、不分表

---

## 2. 数据存储选型

* **存储介质**：SQLite（已冻结）
* **日志**：建议同时在本地生成文件日志，**主要用于调试与操作审计**，不作为主事实源。
* 日志格式：纯文本（按批次或日期分文件），记录异常、操作步骤、错误原因。

---

## 3. SQLite 最小表结构设计

**表名：`sn_verify_records`**

| 字段名         | 类型                                | 说明                          |
| ----------- | --------------------------------- | --------------------------- |
| id          | INTEGER PRIMARY KEY AUTOINCREMENT | 唯一自增 ID                     |
| batch_id    | TEXT                              | 批次 ID，用于标识当前生产批次            |
| sn_scan     | TEXT                              | 扫码枪输入的 SN                   |
| sn_adb      | TEXT                              | 设备读取的 SN                    |
| result      | TEXT                              | 校验结果（PASS / FAIL / TIMEOUT） |
| workstation | TEXT                              | 当前工位标识                      |
| timestamp   | DATETIME                          | 校验时间                        |
| reason      | TEXT                              | 校验失败或异常原因，可空                |

> 注：此表为 Phase 1 最小闭环使用，Phase 2 可根据需要扩展额外字段（如操作员、MES 回传 ID 等）。

### 3.1 SN 重复校验规则

* 在同一批次 (`batch_id`) 内，**SN 不允许重复**。
* 如果发现重复 SN：

  * 判定为 FAIL
  * 写入 SQLite，并记录 reason 为 “Duplicate SN in batch”
* 校验逻辑在 `SnVerifyService` 层处理，ViewModel / UI 只消费校验结果

---

## 4. 数据写入与导出策略

* **原子性**：每次 SN 校验完成后立即写入 SQLite。
* **异常处理**：写入失败必须记录到文件日志，并弹出警告。
* **查询与导出**：

  * 导出必须按批次 `batch_id` 分组
  * 成功 (PASS) 与失败 (FAIL / TIMEOUT) 分别作为不同表格或 CSV 文件
  * 导出可按时间范围或批次过滤

---

## 5. 日志文件使用建议

* 文件日志仅做**操作记录和异常追踪**。
* 文件命名建议：`SnVerify_Log_YYYYMMDD_HHMMSS.txt`
* 内容示例：

  * 扫码内容
  * 校验结果
  * 异常或超时信息
  * MES 接口调用状态
* **不作为主数据存储**，SQLite 才是事实源。

---

## 6. 批次（Batch）假设

### 6.1 批次概念

- 每一次产线操作都必须归属于一个批次（Batch）
- 批次在 Phase 1 中由人工或系统创建
- 批次是结果导出、统计、追溯的最小单位

### 6.2 批次字段（概念）

| 字段 | 说明 |
|----|----|
| BatchId | 批次唯一标识 |
| StartTime | 批次开始时间 |
| Operator | 操作人（可选） |
| Remark | 备注（可选） |

---

## 7. 导出假设（已冻结）

### 7.1 导出粒度

- **按批次导出**
- 一个批次 → 一个导出文件

### 7.2 导出格式（最终结论）

- **Excel（.xlsx）**
- 单文件
- 包含两个 Sheet：
  - `PASS`
  - `FAIL`（包含 FAIL / TIMEOUT）

### 7.3 Sheet 规则

- 两个 Sheet 字段结构完全一致
- 字段顺序固定，便于审计与比对
- 不依赖 Excel COM 组件

---


## 8. Phase 2 预留（不在 Phase 1 实现）

- 数据分库 / 分表
- 云端同步
- 审计策略升级


> 本文档为 Phase 1 本地存储约束，**任何 Service 或 UI 层操作必须遵循**，保证数据唯一性和可追溯性，并支持批次管理、批次内 SN 重复校验以及按批次导出 CSV 分别显示成功和失败结果。
