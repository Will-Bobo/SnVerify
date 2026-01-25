# SnVerify 平台 AI 协作开发

## Phase 1 - 扫码输入模块（ScanInputService）Cursor Agent Prompt

---

## 一、角色与职责说明

你现在的角色是：**Cursor Agent（主要开发者）**

你的任务是：
在 SnVerify 项目 Phase 1 中，实现 **扫码输入与触发模块（ScanInputService）**，并严格遵循 **TDD（测试优先）开发模式**。

该模块是 Phase 1 的**流程起点模块**，其设计正确性将直接决定后续流程是否稳定。

---

## 二、项目上下文（必须阅读）

### 2.1 项目背景

* 项目名称：SnVerify
* 项目类型：Windows 上位机（WPF）
* 当前阶段：Phase 1（最小闭环）
* 开发模式：1 人 + AI（Cursor Agent）

### 2.2 已完成模块

* StorageService（SQLite，唯一事实源）
* AdbAccessService（ADB SN 读取，已通过单元测试）

### 2.3 本模块在系统中的位置

```text
键盘输入 / 扫码枪
        ↓
ScanInputService
        ↓
ProcessCoordinator（后续模块）
```

ScanInputService **只负责输入与触发**，不参与任何业务判断。

---

## 三、模块目标（What）

实现一个 **ScanInputService**，用于：

* 接收字符流（来自扫码枪 / 键盘）
* 识别一条完整 SN
* 在 SN 完整时触发一次性事件
* 支持 `\r\n` 作为默认触发条件
* 未来可扩展为“手动点击触发”

---

## 四、输入与触发规则（已冻结）

### 4.1 SN 输入规则

* SN 由 **字母 + 数字** 组成
* 不区分大小写（统一转为大写）
* 忽略首尾空格
* 不在本模块校验业务合法性

---

### 4.2 触发规则（Phase 1）

* 扫码枪输入 **以 `\r\n` 结尾**
* 当检测到 `\r\n`：

  * 判定为一条完整 SN
  * 立即触发

> Phase 2 才考虑不带 `\r\n` 的扫码枪 + 手动按钮触发

---

## 五、架构与设计硬约束（必须遵守）

### 5.1 分层规则（硬规则）

* 本模块属于 **Service 层**
* 禁止引用：

  * WPF
  * Dispatcher
  * Application
  * ViewModel
* 禁止：

  * UI 控件操作
  * XAML

---

### 5.2 状态 vs 事件

* ScanInputService **不维护 UI 状态**
* 不保存“当前 SN”作为状态
* 仅通过 **事件（Event）** 抛出一次性事实：

  * `SnCaptured`

事件只用于：

* 一次性、不可重放的事实

---

## 六、接口设计要求（必须先设计）

### 6.1 Service 接口（示意）

```csharp
public interface IScanInputService
{
    event EventHandler<SnCapturedEventArgs> SnCaptured;

    void OnCharReceived(char inputChar);

    void Reset();
}
```

---

### 6.2 事件参数对象（示意）

```csharp
public class SnCapturedEventArgs : EventArgs
{
    public string Sn { get; }
}
```

* 事件参数对象必须：

  * 不可变
  * 只读属性

---

## 七、测试优先（TDD 硬要求）

### 7.1 必须先完成的测试用例

#### ① 正常扫码流程

* 输入字符序列：`A B C 1 2 3 \r \n`
* 触发 SnCaptured
* SN = `ABC123`

---

#### ② 多次扫码连续输入

* 连续输入两组 SN
* 每组触发一次事件
* 不丢失、不串数据

---

#### ③ 无触发符不应触发

* 输入不包含 `\r\n`
* 不触发 SnCaptured

---

#### ④ Reset 行为

* 输入一半 SN
* 调用 Reset
* 缓存清空

---

### 7.2 测试要求

* 禁止 UI 测试
* 禁止依赖键盘 Hook
* 使用纯字符输入模拟
* 测试必须可重复、可并行

---

## 八、代码规范（长期规则）

### 8.1 注释规范（强制）

* 所有 public 类 / 方法：

  * 必须有 XML 注释
  * 注释需说明：

    * 模块目的
    * 触发条件
    * Reset 语义

---

### 8.2 AI 生成标记（强制）

所有由你生成的代码文件，文件头必须包含：

```csharp
/// <author>
/// AI Assistant
/// </author>
```

---

## 九、输出要求

你需要输出：

1. 单元测试代码（先）
2. IScanInputService 接口
3. ScanInputService 实现
4. 必要的事件参数类型

禁止输出：

* UI 代码
* ViewModel
* XAML
* 未测试的实现代码

---

## 十、完成判定标准

当且仅当满足以下条件，任务才视为完成：

* 所有测试通过
* 每次 `\r\n` 仅触发一次事件
* 支持连续扫码
* 无 UI / WPF 依赖
* 可被 ProcessCoordinator 安全订阅

---

> 本模块是 Phase 1 的流程起点，请确保行为确定、边界清晰。
