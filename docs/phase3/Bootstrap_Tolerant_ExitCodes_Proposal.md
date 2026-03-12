# Bootstrap 命令“宽容退出码”方案（评审稿）

## 一、问题背景

### 1.1 现有 AdbAccessService 对 "shell ylzero" 的语义

在 `AdbAccessService.ReadDeviceSnAsync` 中（约 114–186 行），**shell ylzero** 的处理逻辑为：

| 结果 | 行为 |
|------|------|
| **IsTimeout** | 重试或返回失败（视为不可继续）。 |
| **IsSuccess** | 继续后续 SN 读取。 |
| **ExitCode 127** | 命令不存在（如 debug 版机器）；**记录日志，继续 SN 读取**。 |
| **ExitCode 255** | user 版本设备特殊状态；**记录日志，继续 SN 读取**。 |
| **其他 ExitCode** | 重试或返回失败。 |
| **执行异常** | 重试或返回失败。 |

即：**ylzero 并非“必须成功”，127/255 视为可接受，仅超时、异常或其它退出码才视为失败。**

### 1.2 当前 DeviceAccess 子系统的行为

- **DeviceAdbConfig.BootstrapCommands** 为 `List<string>`，仅表示命令字符串。
- **DeviceSessionManager.EnsureSessionReadyAsync** 对每条 Bootstrap 命令执行后 **仅判断 `result.IsSuccess`**；若 `!result.IsSuccess` 即 **抛异常**，整次设备读取失败。

因此，若将 **shell ylzero** 配置为 SOLTAG25 / KM001 的 BootstrapCommands，在返回 127 或 255 的设备上会触发“Bootstrap 命令失败”，与现有 Legacy 路径下“可继续 SN 读取”的语义不一致。

---

## 二、目标

在 **不破坏现有架构**（Domain 不依赖 Infrastructure、配置驱动、Session 级 Bootstrap）的前提下，支持：

- 部分 Bootstrap 命令在 **特定退出码**（如 127、255）下 **视为可接受**，不抛异常，继续后续流程；
- **超时策略可配置**：支持“超时即失败”“超时视为通过（Warmup 宽容）”“超时重试”三种策略，以覆盖工业场景下 **Warmup 命令无输出但设备已 ready** 的情况；
- **未列入可接受范围的退出码**、**执行异常**（及策略为 Fail 时的超时）仍视为失败（抛异常或按现有策略处理）。

---

## 三、方案概述

为 Bootstrap 命令增加两类策略：

1. **退出码宽容**：AcceptableExitCodes，某条命令执行后 IsSuccess 或 ExitCode 在列表中即视为通过。
2. **超时策略（Timeout Acceptance Policy）**：TimeoutBehavior，规定该条命令**超时**时是视为失败、视为通过（Ignore，Warmup 宽容）、还是重试（Retry）。

### 3.1 配置模型（Domain）

- **新增枚举**（`Domain/DeviceAccess/`）：
  - **BootstrapTimeoutBehavior**
    - **Fail**：超时视为失败，抛异常（默认，与当前行为一致）。
    - **Ignore**：超时视为通过，继续下一条（**Warmup Command Timeout Tolerance**：例如 shell ylzero 无输出但设备已 ready）。
    - **Retry**：超时后重试该条命令（次数可由实现约定，如最多 1 次或 2 次）。
- **新增类型**（`Domain/DeviceAccess/`）：
  - **BootstrapCommandSpec**
    - `string Command`
    - `int[] AcceptableExitCodes`（可选）  
      语义：执行后若 IsSuccess 或 ExitCode 在列表中，则视为通过；否则再根据超时策略判断。
    - **`BootstrapTimeoutBehavior TimeoutBehavior`**（默认 **Fail**）  
      语义：当 **result.IsTimeout == true** 时：Fail → 抛异常；Ignore → 视为通过并继续；Retry → 重试该条（次数上限由实现约定，如 2 次后仍超时则 Fail）。
- **DeviceAdbConfig 变更**：
  - 将 **BootstrapCommands** 从 `List<string>` 改为 **BootstrapCommandSpecs**（`List<BootstrapCommandSpec>`）；
  - 推荐：**仅保留 BootstrapCommandSpecs**，迁移时一次性替换并删除 BootstrapCommands。

### 3.2 执行逻辑（Infrastructure）

- **DeviceSessionManager.EnsureSessionReadyAsync**（对每条 `BootstrapCommandSpec spec`）：
  1. **执行命令**，得到 result（含 IsSuccess、ExitCode、IsTimeout）。
  2. **若 result.IsTimeout**：
     - **Fail** → 抛异常（Bootstrap 命令超时）；
     - **Ignore** → 视为通过，继续下一条（可选：Debug 日志 “Bootstrap 命令超时，按策略忽略”）；
     - **Retry** → 重试该条（建议最多 1～2 次）；若达到重试上限仍超时 → 抛异常。
  3. **若 !result.IsTimeout**：
     - **IsSuccess** → 通过；
     - **!IsSuccess** 且 **AcceptableExitCodes 非空** 且 **result.ExitCode 在 AcceptableExitCodes 中** → 通过（可选：Debug 日志）；
     - 否则 → 抛异常（含命令与 ExitCode/ErrorMessage）。
  4. **执行异常**（如进程抛异常）：一律视为失败，抛异常；不参与 TimeoutBehavior（若需“异常也忽略”可后续再扩展）。
- 若为兼容保留的 **BootstrapCommands**（string 列表）：行为与现有一致（仅 IsSuccess 通过；超时即失败），或视为“无宽容”。

### 3.3 ProductRegistry 配置示例

**KM001（ylzero：允许 127/255，且支持 Warmup 超时宽容）：**

```csharp
BootstrapCommandSpecs = new List<BootstrapCommandSpec>
{
    new BootstrapCommandSpec
    {
        Command = "shell ylzero",
        AcceptableExitCodes = new[] { 127, 255 },   // 与 AdbAccessService 语义一致
        TimeoutBehavior = BootstrapTimeoutBehavior.Ignore  // 无输出但设备已 ready 时继续
    }
}
```

**若希望超时重试一次再决定：**

```csharp
new BootstrapCommandSpec
{
    Command = "shell ylzero",
    AcceptableExitCodes = new[] { 127, 255 },
    TimeoutBehavior = BootstrapTimeoutBehavior.Retry
}
```

**其他产品“必须成功且超时即失败”的 Bootstrap：**

```csharp
new BootstrapCommandSpec
{
    Command = "shell some_init",
    AcceptableExitCodes = null,
    TimeoutBehavior = BootstrapTimeoutBehavior.Fail  // 默认，可省略
}
```

- `AcceptableExitCodes == null` 或空：仅 `IsSuccess` 时视为成功。
- `TimeoutBehavior` 默认 **Fail**，保证未显式配置时行为与当前一致。

### 3.4 向后兼容与迁移

- **方案 A（推荐）**：DeviceAdbConfig 仅保留 **BootstrapCommandSpecs**；删除 **BootstrapCommands**。  
  - 所有使用 Bootstrap 的产品（如 KM001）在 Registry 中改为 BootstrapCommandSpecs，ylzero 配 AcceptableExitCodes = [127, 255]、**TimeoutBehavior = Ignore**（或 Retry，视需求）。  
  - DeviceSessionManager 只处理 BootstrapCommandSpecs。
- **方案 B**：同时保留 BootstrapCommands 与 BootstrapCommandSpecs；优先使用 BootstrapCommandSpecs，若为空则回退到 BootstrapCommands（旧行为）。  
  - 迁移期可先不动现有配置，新配置用 BootstrapCommandSpecs；最后再统一迁移并删除 BootstrapCommands。

### 3.5 TimeoutBehavior 使用建议（工业协议）

| 场景 | 建议 |
|------|------|
| **Warmup 命令（如 shell ylzero）** | 无输出但设备可能已 ready → **Ignore**；若希望再争取一次 → **Retry**（重试次数由实现约定，如 1 次）。 |
| **必须成功的初始化命令** | **Fail**（默认），超时即失败。 |
| **可选预热、超时不影响后续** | **Ignore**。 |

---

## 四、实施步骤建议（评审通过后执行）

1. **Domain**：新增 **BootstrapTimeoutBehavior** 枚举（Fail, Ignore, Retry）；新增 **BootstrapCommandSpec**（Command, AcceptableExitCodes, **TimeoutBehavior**，默认 Fail）；**DeviceAdbConfig** 新增 **BootstrapCommandSpecs**，并弃用或移除 **BootstrapCommands**（按选定兼容策略）。
2. **Infrastructure**：**DeviceSessionManager.EnsureSessionReadyAsync** 改为按 **BootstrapCommandSpecs** 执行，按 3.2 规则处理 **超时（含 TimeoutBehavior）** 与 **退出码（AcceptableExitCodes）**；若保留兼容，则对 BootstrapCommands 保持现有严格逻辑（超时即失败）。
3. **ProductRegistry**：将 KM001（及所有使用 "shell ylzero" 的产品）改为 **BootstrapCommandSpecs**，Command = "shell ylzero", AcceptableExitCodes = [127, 255], **TimeoutBehavior = Ignore**（或 Retry）。
4. **单测**：为 DeviceSessionManager 增加用例——ylzero 返回 127/255 时不抛异常；超时且 TimeoutBehavior = Ignore 时不抛异常；超时且 TimeoutBehavior = Fail 时抛异常；Retry 时重试次数与最终失败/通过；Success 时通过。
5. 全量编译与现有 339 用例通过。

---

## 五、风险与注意

- **ProcessExecutionResult**：需能取到 **ExitCode**、**IsTimeout**。当前 `SnVerify.Services.Adb.ProcessExecutionResult` 若已有这些属性，则无需改；否则需在 Domain/Infrastructure 可访问的接口中暴露。
- **Retry 次数**：建议实现时约定 Retry 上限（如 1 次或 2 次），避免单条命令无限重试；达到上限仍超时则按 **Fail** 处理并抛异常。
- **日志**：对“可接受退出码”或“超时 Ignore”通过的情况，建议打 Debug 日志，便于排查。
- **SOLTAG25**：若后续为 SOLTAG25 配置 Phase3 并加入 ylzero，同样使用 BootstrapCommandSpecs + AcceptableExitCodes = [127, 255] + **TimeoutBehavior = Ignore**（或 Retry）即可。

---

## 六、结论

- 通过引入 **BootstrapCommandSpec**（Command + AcceptableExitCodes + **TimeoutBehavior**）并让 **DeviceSessionManager** 按“成功 / 可接受退出码 / 超时策略”执行，即可在现有 DeviceAccess 子系统中复现 AdbAccessService 对 **shell ylzero** 的宽容语义，并支持工业场景下的 **Warmup Command Timeout Tolerance**（超时但设备已 ready 时继续）。
- **TimeoutBehavior** 提供 Fail / Ignore / Retry 三种策略，满足“超时即失败”“超时视为通过”“超时重试”的需求；默认 **Fail** 保证未配置时行为与当前一致。
- 建议采用 **仅 BootstrapCommandSpecs、移除 BootstrapCommands** 的迁移方式，配置一次到位，逻辑单一。

**评审通过后，按第四节步骤实施。**
