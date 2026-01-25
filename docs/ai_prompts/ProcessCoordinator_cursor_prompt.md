# SnVerify 平台 AI 协作开发

## Phase 1 - 流程编排模块（ProcessCoordinator / VerificationFlowService）Cursor Agent Prompt

---

## 一、角色与职责说明

你现在的角色是：**Cursor Agent（主要开发者）**

你的任务是：

* 实现 SnVerify Phase 1 中的 **ProcessCoordinator / VerificationFlowService**
* 严格遵循 **TDD（测试优先）**
* 负责将各个 Service 模块（ScanInputService、AdbAccessService、StorageService）串联成**原子化流程**
* 保持 **原子锁定 / 超时 / 状态驱动**，不破坏已完成模块约束

---

## 二、模块目标（What）

* 接收 ScanInputService 的事件（SN 捕获）
* 调用 AdbAccessService 获取设备 SN
* 调用 StorageService 保存校验结果
* 调用 StorageService 导出（按批次）结果
* 实现**一次性锁定流程**：

  * 扫描触发 → 流程运行 → 流程完成 → 释放锁
* 处理超时、重复 SN、ADB 异常、MES 接口异常
* 保证 UI 可绑定状态（只读、线程安全），但不直接操作 UI

---

## 三、架构与设计约束（必须遵守）

### 3.1 分层规则

* ProcessCoordinator 属于 **Service 层**
* 禁止引用 WPF / Dispatcher / Application / ViewModel
* 与 UI 的交互只通过**只读状态对象**（Snapshot）和 PropertyChanged
* 所有调用外部 Service（ADB、Storage、MES）必须通过接口注入

### 3.2 状态 vs 事件

* 使用 **状态对象 + PropertyChanged** 表达当前流程状态
* **事件仅用于一次性事实**（如 SN 捕获、流程完成、异常发生）
* 禁止为状态变化定义事件

### 3.3 流程原子化规则

* isProcessing = true 时，拒绝处理新 SN
* 流程完成或超时 → isProcessing = false
* 所有 Service 调用失败 → 记录结果 / 缓存 → 流程结束 → 恢复监听

---

## 四、接口与数据对象设计

### 4.1 状态对象（Snapshot）

```csharp
public class VerificationSnapshot
{
    public string CurrentSn { get; }
    public bool IsProcessing { get; }
    public string? LastResult { get; }
    public string? FailReason { get; }
    public DateTime Timestamp { get; }
}
```

> Snapshot 对象必须不可变，可绑定 UI，可导出，不包含方法。

### 4.2 ProcessCoordinator 接口示意

```csharp
public interface IProcessCoordinator
{
    VerificationSnapshot Snapshot { get; }
    event EventHandler<VerificationSnapshot> SnapshotChanged;

    Task StartVerificationAsync(string sn);
    void Reset();
}
```

* StartVerificationAsync 包含整个原子流程
* Reset 清理状态，允许下一次扫描

---

## 五、测试优先（TDD）

### 5.1 必须先完成的测试用例

1. 正常流程：SN 捕获 → ADB 读取 → 校验一致 → 存储 → Snapshot 更新 → isProcessing = false
2. 重复 SN：判定 FAIL → Snapshot 更新 → 流程结束
3. ADB 超时或失败：判定 TIMEOUT / FAIL → Snapshot 更新 → 流程结束
4. MES 接口失败：缓存结果 → Snapshot 更新 → 流程结束
5. 流程原子锁定测试：流程执行期间输入新 SN → 忽略
6. Reset 测试：执行 Reset → isProcessing = false，允许新流程启动

### 5.2 Mock 要求

* Mock ScanInputService、AdbAccessService、StorageService、MES 接口
* 所有逻辑测试必须可重复、独立、可并行

---

## 六、代码规范（长期规则）

* 所有 public 类 / 方法必须有 XML 注释
* 文件头必须标注 AI 生成：

```csharp
/// <author>
/// AI Assistant
/// </author>
```

* 不允许在 Service 中写 UI / Dispatcher / Thread 调度
* Command / PropertyChanged 必须遵守 MVVM 约束

---

## 七、输出要求

* 单元测试代码（先写）
* IProcessCoordinator 接口
* ProcessCoordinator 实现
* Snapshot 数据对象
* Mock 所需的辅助类

禁止输出：

* UI / ViewModel / XAML
* 未测试的流程实现

---

## 八、完成判定标准

* 所有单元测试通过
* 流程原子锁定正确执行
* 所有异常路径正确更新 Snapshot
* 流程结束后 isProcessing = false
* 可以与 ScanInputService、AdbAccessService、StorageService 无缝集成

> 本模块是 Phase 1 的核心流程编排，请确保原子性、可测试性、可追溯性。
