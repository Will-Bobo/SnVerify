# 07_Technical_Architecture_and_Dev_Guide

> 本文档用于在 SnVerify Phase 1 阶段，作为**技术架构冻结后的唯一开发指导文档**。
> 目标读者：
>
> * 架构者（你 / ChatGPT）
> * 开发者（Cursor Agent）
> * 审核者（人工 Review）

---

## 1. 架构冻结声明（Architecture Freeze）

**冻结结论：**

* Phase 1 技术架构已冻结
* 架构冻结时间：2026-01
* 除非进入 Phase 2，否则不得调整核心架构原则

**冻结内容包括：**

* MVVM 架构模式
* Service 驱动状态模型
* SQLite 作为唯一事实源
* 批次 + SN 校验闭环
* 单线程顺序处理（Phase 1）

---

## 2. 总体技术架构概览

### 2.1 分层结构

```text
┌───────────────┐
│     View      │  XAML / Code-behind（仅 InitializeComponent）
└───────▲───────┘
        │ DataBinding / Command
┌───────┴───────┐
│  ViewModel    │  UI 状态 / Command / PropertyChanged
└───────▲───────┘
        │ Snapshot
┌───────┴───────┐
│    Service    │  业务流程 / 状态机 / 线程调度
└───────▲───────┘
        │ Repository / IO
┌───────┴───────┐
│ Infrastructure│  ADB / SQLite / FileLog / MES
└───────────────┘
```

---

## 3. 核心架构原则（硬约束）

### 3.1 MVVM 硬规则

* View 不允许包含任何业务逻辑
* ViewModel 不允许直接访问 IO / 协议 / 线程
* 所有真实状态由 Service 层维护

---

### 3.2 状态 vs 事件

* **状态（State）**：

  * 使用属性表达
  * 必须可重放、可缓存
  * 通过 PropertyChanged 驱动 UI

* **事件（Event）**：

  * 只用于一次性事实
  * 不得用于表达 UI 可感知状态

---

### 3.3 Snapshot 模型

Snapshot 对象必须：

* 不包含行为（无方法）
* 只读 / 不可变
* 可安全绑定 / 缓存 / 导出（Excel .xlsx，单文件双 Sheet：PASS / FAIL）

---

## 4. Phase 1 执行模型

### 4.1 单设备原子流程

* 一次扫码 → 一个完整校验流程
* 流程未结束前，拒绝新输入
* 所有异常必须归档为结果

---

### 4.2 ADB 访问模型（已验证）

```text
1. adb shell ylzero
2. adb shell getprop sys.skyroam.osi.sn
```

* 顺序执行
* 失败可重试（Service 内部）
* ViewModel 不感知 ADB 细节

---

## 5. 数据与存储架构

> **更新说明**：导出（Excel .xlsx，单文件双 Sheet：PASS / FAIL）规则已调整为 **Excel（.xlsx）单文件双 Sheet**，不再使用 Excel（.xlsx，单文件，包含 PASS / FAIL 两个 Sheet） 作为最终交付格式。

### 5.1 SQLite 唯一事实源

* 单文件数据库
* 数据量预期：≤ 100,000 行
* Phase 1 不拆表、不分库

---

### 5.2 核心数据模型（概念）

```text
Batch
 └─ BatchId
 └─ StartTime
 └─ Operator

SN_Record
 └─ BatchId
 └─ SN
 └─ Result (PASS / FAIL / TIMEOUT)
 └─ FailReason
 └─ Timestamp
```

---

### 5.3 批次规则

* 每个 SN 必须归属一个批次
* 同一批次内 SN 不允许重复
* 重复视为 FAIL

---

### 5.4 结果导出（Excel .xlsx，单文件双 Sheet：PASS / FAIL）规则（已冻结）

* **按批次导出（Excel .xlsx，单文件双 Sheet：PASS / FAIL）一个 Excel 文件（.xlsx）**

* 文件内包含两个 Sheet：

  * `PASS`
  * `FAIL`（包含 FAIL / TIMEOUT）

* Sheet 内字段结构一致，便于比对与审计

* 不再使用 Excel（.xlsx，单文件，包含 PASS / FAIL 两个 Sheet） 作为最终导出（Excel .xlsx，单文件双 Sheet：PASS / FAIL）格式

* Phase 1 仅要求本地导出（Excel .xlsx，单文件双 Sheet：PASS / FAIL），不涉及上传

* 每个 SN 必须归属一个批次

* 同一批次内 SN 不允许重复

* 重复视为 FAIL

---

## 6. 日志策略

* 业务结果：SQLite
* 运行日志：文件日志
* 日志不可从 UI 清除
* 日志仅用于问题定位

---

## 7. Command 与 UI 规则（强制）

* Command 只作为流程入口
* CanExecute 只依赖显式状态
* 状态变化必须显式 RaiseCanExecuteChanged

---

## 8. 代码规范（Cursor Agent 强制遵守）

### 8.1 注释与作者标注

* 所有 public 方法 / 类 / 复杂逻辑必须有注释
* AI 生成文件统一标注：

```csharp
/// <author>AI Assistant</author>
```

---

### 8.2 禁止项（红线）

* ViewModel 禁止 Dispatcher / Application
* 禁止在 View 中写业务逻辑
* 禁止绕过 Service 改状态

---

## 9. Phase 2 扩展预留

* 多工位
* 多设备并发
* MES 强绑定
* 权限 / 审计

---

## 10. 开发启动指引

开发顺序建议：

1. StorageService + SQLite Schema
2. Batch 生命周期 Service
3. SN 校验流程 Service
4. ViewModel 绑定与 UI

---

## 11.MES 集成策略（阶段性决策）

- MES 通过中间抽象层接入（IMesService）
- 当前阶段仅实现杰科 MES 的最小适配
- MES 支持 Enable / Disable 开关
- Phase 2 不完成最终 MES 对接
- Phase 3/4 再引入多工厂、多协议支持

---

> 本文档是 SnVerify Phase 1 的**最高技术约束文档**。
> 所有代码、测试、Agent 行为必须与本文档一致。

---

## 参考开源项目与架构标杆（长期阅读材料）

> 本节用于指导 Cursor Agent 与后续开发人员在实现与重构过程中**参考哪些类型的项目**，以及**明确哪些项目仅作思想理解而非直接引入**。

### 一、当前 Phase 1 推荐参考的项目类型（强相关）

#### 1. WPF + 原生 MVVM + Service 分层示例

**目标用途**：

* 学习 View / ViewModel / Service 的清晰职责划分
* 避免引入过重框架
* 保持代码可测试性（TDD 友好）

**重点关注点**：

* ViewModel 只依赖 Service 接口
* Service 不依赖任何 UI 框架
* View 层仅负责绑定（无业务逻辑）

> 示例来源（已放入 reference_open_source）：
>
> * mvvm-samples
> * mvvmlight（仅参考 MVVM 思想，不引入框架本身）

---

#### 2. SQLite / LiteDB 等轻量本地存储项目

**目标用途**：

* 学习单文件数据库的生命周期管理
* 参考 Repository / StorageService 设计方式
* 为 10w 级数据量提供稳定存储方案

**重点关注点**：

* 单表设计
* 批次（Batch）维度查询
* 数据导出（CSV / Excel）

> 示例来源：
>
> * liteDB 示例项目（仅参考数据访问模式）

---

#### 3. NUnit + Moq 的业务型测试示例

**目标用途**：

* 强化 TDD（测试先行）开发模式
* 学习如何 Mock 外部依赖（ADB / MES / Storage）

**重点关注点**：

* Service 层测试而非 UI 测试
* 明确 Arrange / Act / Assert 结构
* 测试失败即阻断实现

> 示例来源：
>
> * nunit-csharp-samples
> * moq 示例

---

### 二、暂不引入但需要理解的架构框架（思想层）

#### Prism（阶段性不引入）

**当前结论**：

* Prism 在 Phase 1 **不作为实现依赖**
* 仅作为架构思想参考

**原因说明**：

* Phase 1 项目规模小、流程集中
* Prism 的模块化、Region、导航能力暂时用不上
* 引入会显著增加 Cursor Agent 生成代码的复杂度与不可控性

**允许参考的内容**：

* 解耦思想
* Command / ViewModel 职责划分
* 长期演进思路

**明确禁止**：

* 在 Phase 1 中引入 Prism 相关 NuGet 包
* 使用 Region / Module / EventAggregator

> Prism 的引入评估将推迟至 Phase 2（多工位 / 插件化 / 复杂 UI 导航阶段）。

---

### 三、对 Cursor Agent 的明确约束（长期有效）

* 生成代码时：

  * **优先参考本项目 docs 目录中的架构与规则文档**
  * 参考开源项目仅用于理解模式，不直接复制复杂框架结构
* 禁止：

  * 擅自引入大型 MVVM 框架（如 Prism）
  * 为未来阶段提前设计模块化 / 插件化结构

> 结论性原则：
> **Phase 1 的目标是“最小闭环 + 可测试”，而不是“架构完整性”。**
