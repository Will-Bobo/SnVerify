# SN 验证流程设计规范

> **文档用途**：作为架构级设计规范，供开发人员 / AI / 新成员理解 SnVerify 的流程设计与类关联，并在**新增功能检验流程**时按本规范执行。

---

## 一、设计目标

1. **单次触发、原子化执行**：一次扫码/操作触发完整校验流程，处理期间拒绝新输入
2. **明确 PASS/FAIL 判定**：结果唯一、可追溯、不可覆盖
3. **分层解耦**：Domain / Service / ViewModel / View 严格分离，Service 层不依赖 UI
4. **可扩展**：新增检验类型时，遵循既有模式，最小侵入

---

## 二、分层架构与职责

| 层级 | 职责 | 关键约束 |
|------|------|----------|
| **Domain** | 纯业务模型、枚举、规则定义 | 禁止引用 WPF、UI、网络、IO、Dispatcher |
| **Service** | 设备通信、IO、流程编排、事件转发 | 禁止引用 ViewModel / View；所有外部依赖通过接口注入 |
| **ViewModel** | 绑定 Snapshot、触发 Command、驱动 UI 刷新 | 不直接访问 UI / Dispatcher / Thread |
| **View** | XAML 与 InitializeComponent() | 不包含业务逻辑 |

---

## 三、核心流程概览

### 3.1 两种检验类型

| 检验类型 | 枚举值 | 输入 | 输出 | 流程入口 |
|----------|--------|------|------|----------|
| **SN 匹配** | `VerificationType.SnMatch` | 扫码枪 SN (StickerSN) | PASS / FAIL / TIMEOUT | `IVerificationFlowService.StartVerificationAsync(sn)` |
| **版本匹配** | `VerificationType.VersionMatch` | TestSession + ExpectedVersion | TestRecord | `IVersionVerificationFlowService.ExecuteVersionCheckAsync(session)` |

### 3.2 SN 匹配流程（主流程）

```
扫码枪输入 (OnCharReceived / OnScanInputAsync)
    ↓
ScanInputService 识别完整 SN (以 \r\n 结尾)
    ↓
MainViewModel 收到 SnCaptured 或直接调用
    ↓
IVerificationFlowService.StartVerificationAsync(sn)
    ↓
ProcessCoordinator.StartVerificationAsync(sn)  [原子锁定]
    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. 原子锁定检查 (isProcessing) → 若已处理中则忽略                             │
│  2. MES Pre-Gate（可选，MesMode≠Disabled 时）                                 │
│  3. IAdbAccessService.ReadDeviceSnAsync() → DeviceSN                          │
│  4. 决策树校验（SN_Sticker_Device_Relation_Rules）                             │
│     - 规则1: StickerSN==DeviceSN 且无历史 PASS → PASS                         │
│     - 规则2~5: 其他情况 → FAIL                                                │
│  5. IStorageService.SaveTestRecordAsync() / UpdateTestRecordAsync()            │
│  6. MES Post-Report（可选）                                                    │
│  7. 更新 VerificationSnapshot → SnapshotChanged 事件                           │
│  8. 释放锁定 (isProcessing = false)                                           │
└─────────────────────────────────────────────────────────────────────────────┘
    ↓
ViewModel 订阅 SnapshotChanged / 轮询 Snapshot → UI 刷新
```

### 3.3 版本匹配流程（扩展流程）

```
用户点击「版本检验」按钮
    ↓
MainViewModel 调用 IVersionVerificationFlowService.ExecuteVersionCheckAsync(session)
    ↓
VersionVerificationFlowService
    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. 更新 Snapshot = Processing                                                │
│  2. IAdbAccessService.ReadDeviceInfoAsync() → ActualVersion                    │
│  3. ExpectedVersion vs ActualVersion 字符串匹配                                │
│  4. IStorageService.SaveTestRecordAsync()                                     │
│  5. 更新 Snapshot = Completed                                                 │
└─────────────────────────────────────────────────────────────────────────────┘
    ↓
返回 TestRecord，ViewModel 更新 UI
```

---

## 四、类与流程关联

### 4.1 流程编排层（Coordination）

| 类/接口 | 职责 | 依赖 |
|---------|------|------|
| `IProcessCoordinator` | SN 校验流程编排接口 | - |
| `ProcessCoordinator` | SN 校验流程编排实现，实现原子锁定、MES 挂载、决策树 | IStorageService, IAdbAccessService, ILoggingService, IMesPreCheck, IMesResultReporter |
| `IVerificationFlowService` | 对外统一校验入口（SN Match） | IProcessCoordinator |
| `VerificationFlowService` | 委托给 ProcessCoordinator，桥接 MES 事件 | IProcessCoordinator, IFileLogger |
| `IVersionVerificationFlowService` | 版本匹配校验接口 | - |
| `VersionVerificationFlowService` | 版本匹配校验实现 | IAdbAccessService, IStorageService |
| `IVerificationFlowServiceFactory` | 按 SessionId 创建 IVerificationFlowService | - |
| `VerificationFlowServiceFactory` | 创建 ProcessCoordinator + VerificationFlowService | IStorageService, IAdbAccessService, ILoggingService |

### 4.2 输入层（Input）

| 类/接口 | 职责 | 流程触发 |
|---------|------|----------|
| `IScanInputService` | 扫码枪输入捕获、SN 完成识别 | SnCaptured 事件 / OnScanInputAsync |
| `ScanInputService` | 字符缓冲、\r\n 识别、可选内置 ProcessCoordinator 调用 | 触发 ProcessCoordinator 或通知 MainViewModel |

### 4.3 设备访问层（Adb）

| 类/接口 | 职责 | 被调用方 |
|---------|------|----------|
| `IAdbAccessService` | 执行 ADB 命令读取设备 SN / 设备信息 | ProcessCoordinator, VersionVerificationFlowService |
| `AdbAccessService` | ylzero + getprop 串行执行，超时重试 | - |
| `IProcessRunner` | 进程执行抽象 | AdbAccessService |

### 4.4 存储层（Storage）

| 类/接口 | 职责 | 被调用方 |
|---------|------|----------|
| `IStorageService` | TestRecord 读写、历史 PASS 绑定查询 | ProcessCoordinator, VersionVerificationFlowService |
| `StorageService` | SQLite 持久化 | - |

### 4.5 会话层（Session）

| 类/接口 | 职责 | 与流程关系 |
|---------|------|------------|
| `ISessionLifecycleService` | Session 创建/开始/结束 | SessionId 作为 ProcessCoordinator 入口，决定当前检验上下文 |

### 4.6 MES 层（Mes.Gate）

| 类/接口 | 职责 | 挂载点 |
|---------|------|--------|
| `IMesPreCheck` | 工位准入校验 | ProcessCoordinator 流程开始前（每条 SN 前） |
| `IMesResultReporter` | 测试结果上报 | ProcessCoordinator 落库后 |
| `MesMode` | Disabled / Enabled / Strict | 控制 Pre-Gate / Post-Report 是否调用及阻断行为 |

### 4.7 状态对象（Domain.State）

| 类 | 用途 | 不可变性 |
|----|------|----------|
| `VerificationSnapshot` | 校验流程状态（CurrentSn, IsProcessing, LastResult, FailReason 等） | 只读，工厂方法创建 |
| `SessionSnapshot` | Session 状态 | 只读 |
| `ScanSnapshot` | 扫码输入状态 | 只读 |

---

## 五、新增功能检验流程的设计规范

当需要新增一种检验类型（如 MAC 校验、IMEI 校验等）时，按以下规范执行：

### 5.1 设计 Checklist

- [ ] 确定检验类型枚举：在 `VerificationType` 中新增（如 `MacMatch`）
- [ ] 定义检验输入：明确输入来源（扫码 / 按钮 / 配置）
- [ ] 定义检验输出：PASS / FAIL / TIMEOUT 及 TestRecord 字段
- [ ] 确定设备数据来源：是否需要 ADB / 其他接口

### 5.2 实现步骤

#### Step 1：定义流程服务接口与实现

```csharp
// 1. 新增接口
public interface IXxxVerificationFlowService
{
    VerificationSnapshot Snapshot { get; }
    void ResetToIdle();
    Task<TestRecord> ExecuteXxxCheckAsync(/* 必要参数 */, CancellationToken ct = default);
}

// 2. 实现类
public class XxxVerificationFlowService : IXxxVerificationFlowService
{
    private readonly IAdbAccessService _adbAccessService;  // 若需读设备
    private readonly IStorageService _storageService;
    private VerificationSnapshot _snapshot;
    // 实现 ExecuteXxxCheckAsync：读设备 → 判定 → 落库 → 更新 Snapshot
}
```

**约束**：
- 使用 `VerificationSnapshot` 表达流程状态（Processing / Completed）
- 落库使用 `IStorageService.SaveTestRecordAsync`，TestRecord 需符合既有模型
- 禁止引用 ViewModel / View / Dispatcher

#### Step 2：扩展 IAdbAccessService（若需新设备数据）

```csharp
// 在 IAdbAccessService 中新增
Task<AdbXxxResult> ReadDeviceXxxAsync(CancellationToken ct = default);
```

#### Step 3：挂接 ViewModel

- 在 `MainViewModel` 中注入 `IXxxVerificationFlowService`
- 新增 Command（如 `StartXxxVerifyCommand`），Execute 中调用 `ExecuteXxxCheckAsync`
- 将 `Snapshot` 或返回的 `TestRecord` 绑定到 UI 属性

#### Step 4：工厂与依赖注入

- 在 `ServiceFactory` 中创建 `XxxVerificationFlowService` 实例
- 通过构造函数注入到 `MainViewModel`

### 5.3 流程模式要求

| 要求 | 说明 |
|------|------|
| **原子性** | 流程执行期间设置 `IsProcessing = true`，完成后设为 `false` |
| **状态快照** | 使用 `VerificationSnapshot` 或等价只读对象，通过 PropertyChanged / 事件通知 UI |
| **异常处理** | 所有异常路径必须更新 Snapshot 并落库（FAIL/TIMEOUT），不得静默吞掉 |
| **存储一致性** | 结果必须写入 `TestRecord`，SessionId 等字段与既有逻辑一致 |
| **MES 挂载** | 若需 MES，通过 ProcessCoordinator 的 Pre-Gate / Post-Report 扩展，或在新流程中显式调用 IMesResultReporter |

### 5.4 与 SN 流程的关系

- **SN 匹配流程**：核心链路 `Scan → ADB → Verify → Result` 不可侵入；MES 仅通过 Pre-Gate / Post-Report 挂载
- **新检验流程**：可独立实现，不强制复用 ProcessCoordinator；但需遵守同样的 Snapshot、Storage、MES 约定
- **Session 共享**：新检验可与 SN 检验共享同一 Session，通过 `TestSession.VerificationType` 区分

---

## 六、流程图（类依赖关系）

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           MainViewModel                                    │
│  (订阅 SnapshotChanged / SnCaptured，调用 StartVerificationAsync 等)       │
└────────────────────────────────┬─────────────────────────────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────┐   ┌─────────────────────┐   ┌──────────────────────────┐
│ IVerification   │   │ IScanInputService   │   │ IVersionVerification     │
│ FlowService     │   │                     │   │ FlowService              │
└────────┬────────┘   └──────────┬──────────┘   └──────────┬───────────────┘
         │                       │                          │
         │ 委托                  │ SnCaptured / OnScanAsync  │
         ▼                       ▼                          ▼
┌─────────────────┐   ┌─────────────────────┐   ┌──────────────────────────┐
│ Process         │   │ ScanInputService    │   │ VersionVerification      │
│ Coordinator     │   │ (可选内置 Coordinator)│   │ FlowService             │
└────────┬────────┘   └─────────────────────┘   └──────────┬───────────────┘
         │                                                  │
         │ 调用                                              │ 调用
         ├──────────────────┬───────────────────┬───────────┴──────────────┐
         ▼                  ▼                   ▼                          ▼
┌─────────────┐   ┌─────────────────┐   ┌──────────────┐   ┌─────────────────┐
│ IAdbAccess  │   │ IStorageService │   │ IMesPreCheck │   │ IMesResult      │
│ Service     │   │                 │   │ IMesResult   │   │ Reporter        │
└─────────────┘   └─────────────────┘   │ Reporter     │   └─────────────────┘
         ▲                  ▲            └──────────────┘            ▲
         │                  │                                        │
         └──────────────────┴────────────────────────────────────────┘
              (VersionVerificationFlowService 也使用 Adb + Storage)
```

---

## 七、参考文档

| 文档 | 用途 |
|------|------|
| `06_Architecture_Technical_Rules.md` | 技术宪法：分层、MVVM、状态与事件规范 |
| `SN_Sticker_Device_Relation_Rules.md` | SN 校验决策树唯一事实来源 |
| `ProcessCoordinator_cursor_prompt.md` | ProcessCoordinator 模块实现指南 |
| `Phase1_Task_List.md` | Phase 1 任务清单 |

---

**文档版本**：v1.0  
**状态**：✅ 可作为新增功能检验流程的设计规范
