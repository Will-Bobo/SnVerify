# Phase1_Task_List.md

## 项目角色分配

* 架构者：ChatGPT + 你（总体规划、设计文档、架构把控）
* 开发者：Cursor Agent（主要实现 Phase 1 任务）
* 审核者：人（对关键步骤、测试和实现进行审核）

---

## Phase 1 任务清单（批次 / SN 校验 / Excel 导出）

| 任务 ID  | 任务名称           | 输入                          | 输出 / 判定                         | 测试点               | 描述 |
| ------- | ------------------ | ----------------------------- | ---------------------------------- | -------------------- | ---- |
| P1-T1   | 扫码枪输入监听        | 模拟键盘输入                    | SN 字符串完整捕获                    | 完整性测试             | 捕获扫码枪输入；忽略首尾空格；统一大写；以 `\r\n` 作为输入完成触发条件 |
| P1-T2   | 输入触发锁定         | SN 捕获完成                     | isProcessing = true                | 状态测试               | 扫码完成后立即锁定 UI 与逻辑流程，处理期间丢弃其他输入 |
| P1-T3   | 工位准入校验         | SN + batch_id                 | PASS / NG                          | Mock MES 接口         | 调用 `getDutStationInfo.php` 校验当前工位是否允许测试 |
| P1-T4   | ADB 异步读取 SN     | USB 设备连接                   | SN_ADB / 超时                      | 模拟 ADB 服务          | 串行执行 `ylzero` → `getprop sys.skyroam.osi.sn`；失败重试 3 次，间隔 1 秒 |
| P1-T5   | SN 一致性校验       | SN_Scan + SN_ADB + batch_id   | PASS / NG                          | 单元测试               | SN 字符串完全匹配 → PASS；不匹配或超时 → NG；忽略空格、不区分大小写；**同一批次内 SN 不允许重复** |
| P1-T6   | MES 结果上传        | 校验结果                       | 上传成功 / 本地缓存                | Mock MES 接口         | 调用 `postTestDataStr.php`；失败时缓存结果并提示用户 |
| P1-T7   | 本地 SQLite 结果记录 | 校验过程 + 最终结果             | 写入 SQLite                        | 数据完整性测试         | 持久化记录：SN、批次 ID、时间戳、校验结果、失败原因（不一致 / 超时 / 重复 SN） |
| P1-T8   | Excel 导出（按批次） | 批次选择                       | 单个 Excel 文件（2 个 Sheet）      | 导出正确性 / 完整性    | **按批次导出校验结果；一个 Excel 文件；Sheet1 = PASS，Sheet2 = FAIL；字段结构一致** |
| P1-T9   | UI 状态更新         | 校验结果                       | PASS / NG 状态展示                | UI 状态测试           | UI 状态流转：Loading → PASS / NG → 回到等待扫码 |
| P1-T10  | 流程复位            | MES 返回或超时                  | isProcessing = false               | 状态测试               | 清空临时状态与缓存，准备处理下一台设备 |

---

## 说明与约束

1. **批次（Batch）是 Phase 1 的核心维度**
   - 所有 SN 校验、去重、导出、统计均以 batch_id 为边界
   - 不允许跨批次做 SN 唯一性判断

2. **SN 重复规则**
   - 同一 batch_id 下：
     - 已出现过的 SN → 直接判定 FAIL
     - 失败原因标记为 `DUPLICATE_SN`

3. **导出规则（Phase 1 冻结）**
   - 导出格式：`Excel (.xlsx)`
   - 导出单位：**单个批次**
   - 文件结构：
     - Sheet 1：PASS
     - Sheet 2：FAIL
   - PASS / FAIL 使用 **同一字段结构**

4. **SQLite 是唯一事实源**
   - 导出行为只读取 SQLite
   - 不依赖 UI 缓存或内存态数据

---

> 本任务清单作为 Phase 1 的 **执行冻结版本**，用于：
> - Cursor Agent 代码生成
> - 单元测试设计
> - 架构 Review 与交付验收
