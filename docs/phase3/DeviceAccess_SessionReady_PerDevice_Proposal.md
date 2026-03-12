# SessionReady 语义与 Bootstrap 执行策略（评审结论与最终方案）

## 一、评审结论摘要

### 1.1 架构方向（采纳）

系统采用 **Environment Session Model（上位机环境会话模型）**，而非：

- Device Lifecycle Session  
- Physical Device Binding Session  

该模型在工业检测场景下更合适：Session 表示**上位机运行环境下 Shell 通道/环境是否就绪**，不表示设备身份或物理设备是否更换。

### 1.2 SessionReady 语义（必须保持）

- **SessionReady 仅用于屏蔽 Warmup 成本**
- SessionReady 表达：**Windows 上位机运行环境下 Shell 通道是否已经建立**
- **不**表达：设备身份状态、物理设备是否更换

### 1.3 禁止项（不得实施）

| 禁止项 | 说明 |
|--------|------|
| ❌ 通过 adb devices 判断设备是否更换 | 不引入设备识别逻辑 |
| ❌ 设备识别状态机 | 不增加设备身份状态 |
| ❌ 额外 adb IO 轮询 | 不增加探测调用 |

原因：增加检测延迟、破坏性能优化目标、无法可靠推断检测人员操作。

### 1.4 BootstrapCommandSpecs 执行策略（最终确定）

采用 **协议初始化模型**：

- **每次检测流程触发时**：执行 **BootstrapCommandSpecs**（不跳过）。
- 若 **Warmup 已完成** → 仅 **跳过 Shell Warmup**（EnsureShellWarmedUpAsync）。

**Warmup Barrier（环境级）**：

- 仅保护：`EnsureShellWarmedUpAsync`
- 用途：防止 adb shell 首次执行失败

**BootstrapCommandSpecs**：

- 每个检测批次执行一次（即每次进入检测流程/每次调用 EnsureSessionReadyAsync 时执行）。
- 不依赖设备状态判断。
- 不增加额外 IO 探测。

---

## 二、问题回顾（不采用 DeviceId 方案）

此前评估中提到的“换设备后未再执行 Bootstrap”的问题，在**当前架构选择**下处理方式为：

- **不**通过 DeviceId 绑定 Session 解决。
- **通过**“每次检测触发都执行 BootstrapCommandSpecs”解决：无论是否换设备，每次检测都会执行协议初始化，从而避免“新设备未执行 Bootstrap”的语义缺口，同时不引入设备探测与额外 IO。

---

## 三、状态模型（最终推荐）

DeviceSessionManager 内部维持：

| 状态变量 | 作用 |
|----------|------|
| `_sessionReady` | 环境会话初始化完成标记（仅与 Warmup 相关，见下） |
| `_warmupDone`（建议） | Shell 通道预热状态：为 true 时跳过 EnsureShellWarmedUpAsync |

约定：

- **SessionReady**：与“环境级 Warmup 是否已完成”等价；仅用于决定是否执行 Shell Warmup，**不**用于跳过 Bootstrap。
- **BootstrapCommandSpecs**：**不**用状态跳过，每次 `EnsureSessionReadyAsync` 被调用且配置存在时都执行。

（若实现上以 `_sessionReady` 同时表示“Warmup 已完成”，可保留单一变量并加注释；或拆分为 `_warmupDone` 以语义更清晰。）

---

## 四、推荐代码约定（重要）

在 DeviceSessionManager 中与 SessionReady / Warmup 相关处增加明确注释，例如：

```csharp
// SessionReady is environment-level barrier.
// It is not bound to device identity.
// Do not introduce device detection logic here.
```

---

## 五、最终实施要求（评审通过后执行）

### 5.1 必须删除 / 不得做

- ✅ **删除** 任何“按 DeviceId 绑定 Session”的设计（若此前有文档或占位）。
- **不要**：增加 `adb devices` 检测、设备切换判断逻辑、额外 IO 探测。

### 5.2 必须保持的策略

| 项 | 策略 |
|----|------|
| **Warmup** | 进程生命周期内只执行一次（由 _warmupDone 或 _sessionReady 屏蔽） |
| **BootstrapCommandSpecs** | 每检测批次执行（每次 EnsureSessionReadyAsync 调用时执行，不因 SessionReady 跳过） |
| **SessionReady** | 仅用于屏蔽 Warmup 成本（环境级） |
| **IO 探测** | 禁止额外增加 |

### 5.3 具体实现要点

1. **EnsureSessionReadyAsync**  
   - 仅用 `_warmupDone`（或等价意义的 `_sessionReady`）决定是否执行 **EnsureShellWarmedUpAsync**：若已执行则跳过。  
   - **不**因“已就绪”而跳过 **BootstrapCommandSpecs**：只要 `config?.BootstrapCommandSpecs` 非空，每次调用都执行。

2. **状态变量**  
   - 保留或引入 `_warmupDone`，在首次完成 Shell Warmup 后置为 true，且仅在 Warmup 分支使用。  
   - `_sessionReady` 若保留，则仅表示“环境会话已初始化（Warmup 已完成）”，不用于跳过 Bootstrap；或在注释中明确“仅屏蔽 Warmup 成本”。

3. **注释**  
   - 在 SessionReady / Warmup 相关逻辑处添加第四节约定的注释，明确“环境级、不绑定设备、不引入设备检测”。

4. **架构文档**  
   - 在 `DeviceAccess_Subsystem_Architecture_v1.md` 中更新：  
     - Session 采用 Environment Session Model；  
     - SessionReady 仅表示环境级 Warmup 完成；  
     - BootstrapCommandSpecs 每检测批次执行，不依赖设备状态、不增加额外 IO。

5. **单测**  
   - 调整/补充：同一进程内多次调用 `EnsureSessionReadyAsync` 时，Warmup 只执行一次，BootstrapCommandSpecs 每次调用都会执行（通过 mock 验证调用次数）。

---

## 六、小结

- **架构**：采用 Environment Session Model；SessionReady 仅表示上位机环境 Shell 通道就绪，不绑定设备。  
- **策略**：Warmup 进程级一次；BootstrapCommandSpecs 每检测批次执行；禁止 DeviceId 绑定与额外 adb IO。  
- **实施**：按第五节要求修改 DeviceSessionManager、补充注释、更新架构文档与单测。

**评审已通过，按第五节实施。**
