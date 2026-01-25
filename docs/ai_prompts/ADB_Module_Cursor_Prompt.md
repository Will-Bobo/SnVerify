# SnVerify 平台 AI 协作开发

## Phase 1 - ADB 模块（AdbAccessService）Cursor Agent Prompt

---

## 一、角色与职责说明

你现在的角色是：**Cursor Agent（主要开发者）**

你的任务是：
在 SnVerify 项目 Phase 1 中，实现 **ADB 访问模块（AdbAccessService）**，并严格遵循 **TDD（测试优先）开发模式**。

---

## 二、项目上下文（必须阅读）

### 2.1 项目背景

* 项目名称：SnVerify
* 项目类型：Windows 上位机（WPF）
* 当前阶段：Phase 1（最小闭环）
* 开发模式：1 人 + AI（Cursor Agent）

### 2.2 当前已完成模块

* StorageService

  * SQLite 作为唯一事实源
  * 支持批次（batch_id）
  * 支持批次内 SN 去重
  * 已通过人工运行测试

### 2.3 ADB 工具现状

* adb.exe 已存在
* 工具路径固定为：

```
tools/adb/adb.exe
```

* 本模块 **必须通过调用 adb.exe 进程完成**
* 禁止引入任何第三方 ADB SDK

---

## 三、模块目标（What）

实现一个 **AdbAccessService**，用于：

* 顺序执行 ADB 命令
* 访问设备并读取 SN
* 支持重试与超时
* 可被 Mock / 单元测试
* 与 UI / ViewModel 完全解耦

---

## 四、ADB 访问规则（已验证）

### 4.1 命令执行顺序（严格）

1. 打开访问权限：

```
adb shell ylzero
```

2. 读取设备 SN：

```
adb shell getprop sys.skyroam.osi.sn
```

---

### 4.2 重试与超时规则

* 单次读取流程：

  * 最多 **3 次重试**
  * 每次失败间隔 **1 秒**
* 单次完整流程最大超时：

  * **10 秒**
* 超时或重试失败：

  * 返回 TIMEOUT / FAIL

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

  * UI 操作
  * UI 事件
  * WPF 专有 API

---

### 5.2 状态 vs 事件

* 本模块 **不维护 UI 状态**
* 仅：

  * 返回结果对象
  * 或抛一次性异常
* 禁止抛出“状态变化事件”

---

## 六、接口设计要求（必须先设计）

### 6.1 Service 接口（示意）

```csharp
public interface IAdbAccessService
{
    Task<AdbSnReadResult> ReadDeviceSnAsync(
        CancellationToken cancellationToken);
}
```

---

### 6.2 返回结果对象（示意）

```csharp
public class AdbSnReadResult
{
    public bool IsSuccess { get; }
    public string? Sn { get; }
    public string? ErrorReason { get; }
    public bool IsTimeout { get; }
}
```

> 结果对象必须是 **不可变 / 只读**，不包含任何行为方法。

---

## 七、测试优先（TDD 硬要求）

### 7.1 必须先完成的测试用例

#### ① 正常流程

* ADB 命令顺序执行成功
* 返回合法 SN
* IsSuccess = true

#### ② 异常输出

* SN 为空或异常字符串
* 判定为失败

#### ③ 重试机制

* 前 1～2 次失败
* 第 3 次成功
* 最终成功返回

#### ④ 超时场景

* 模拟 adb.exe 无响应
* 超过 10 秒
* 返回 IsTimeout = true

---

### 7.2 Mock 要求

* 禁止真实调用 adb.exe 进行单元测试
* 必须通过进程执行抽象（如 IProcessRunner / ICommandExecutor）
* 允许使用：

  * NUnit
  * Moq

---

## 八、代码规范（长期规则）

### 8.1 注释规范（强制）

* 所有 public 类 / 方法：

  * 必须有 XML 注释
  * 注释需说明：

    * 目的
    * 行为
    * 失败场景

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
2. Service 接口定义
3. Service 实现
4. 必要的内部辅助类（如进程执行器）

禁止输出：

* UI 代码
* ViewModel
* XAML
* 未测试的实现代码

---

## 十、完成判定标准

当且仅当满足以下条件，任务才视为完成：

* 所有测试通过
* 无任何 UI / WPF 依赖
* adb.exe 路径可配置（默认指向 `tools/adb/adb.exe`）
* 可被上层流程安全调用

---

> 本模块为 Phase 1 的高风险 IO 模块，请以“稳健优先”原则实现。
