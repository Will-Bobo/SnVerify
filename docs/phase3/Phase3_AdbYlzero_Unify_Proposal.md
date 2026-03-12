# AdbAccessService「ylzero」命令统一封装方案（评审稿）

> **Step 1 实施状态**：已完成。RunYlzeroAsync 仅执行 `shell ylzero`（无 deviceId）；IsYlzeroResultAcceptableForSnRead 含 127/255 等注释；ReadDeviceSnAsync、ReadDeviceInfoAsync 已改为使用统一入口；GetDeviceSNAsync 已标记 Obsolete，内部代码未改。编译与单测通过。

## 一、现状

`AdbAccessService.cs` 中执行 **adb shell ylzero**（或带设备 ID 的 `shell -s {deviceId} ylzero`）共有 **三处**，且成功/失败判别与后续策略不一致：

| 位置 | 命令 | 超时 | 成功判别与后续策略 |
|------|------|------|---------------------|
| **ReadDeviceSnAsync**（约 L117–L185） | `shell ylzero` | TotalTimeoutMs / MaxRetries | **超时** → 重试或返回失败。<br>**!IsSuccess**：ExitCode 127（命令不存在）/ 255（user 版本）→ 记录日志并**继续** SN 读取；其他 ExitCode → 重试或返回失败。<br>**异常** → 重试或返回失败。 |
| **ReadDeviceInfoAsync**（约 L277–L301） | `shell ylzero` | TotalTimeoutMs | 使用 **IsYlzeroResultAcceptableForSnRead** 判定；不可接受时仅记录日志，**一律继续**；不重试、不返回失败。 |
| **GetDeviceSNAsync 内部**（约 L426–L448） | `shell -s {deviceId} ylzero` | TotalTimeoutMs / MaxRetries | **!IsSuccess 或 IsTimeout** → 重试或设置 Snapshot.Error 并 return null。<br>**未**对 127/255 做“可继续”处理，即 127/255 在此处也视为失败。 |

ylzero 的「特殊性质」在注释中已说明：

- **ExitCode 127**：命令不存在（如 debug 版机器），可继续 SN 读取。
- **ExitCode 255**：user 版本设备特殊状态，可继续 SN 读取。
- 超时或其他 ExitCode：当前在两处视为致命错误，需重试或返回失败。

三处逻辑分散，且第三处（**GetDeviceSNAsync**）与第一处对 127/255 的策略不一致，不利于维护和后续修改。  
> **说明**：方案中「第三处」即 **GetDeviceSNAsync** 方法内部的 ylzero 调用（代码中无名为 ReadDeviceSnWithDeviceId 的方法）。

---

## 二、目标

- 将 **ylzero 的调用**与 **“是否可继续 SN 读取”的判别**收到同一套方法里，便于复用和统一规则。
- 不改变现有对外行为（ReadDeviceSnAsync / ReadDeviceInfoAsync / GetDeviceSNAsync）的语义，仅做抽取与可选的政策统一。

---

## 三、方案

### 3.1 新增两个私有方法

1. **RunYlzeroAsync**(int timeoutMs, CancellationToken cancellationToken) → **ProcessExecutionResult**
   - **职责**：统一执行 `adb shell ylzero`（ADB 口令，打开访问权限）；不负责重试、日志、业务判定。
   - **实现**：固定参数 `shell ylzero`，调用 `_processRunner.RunAsync(_adbPath, "shell ylzero", timeoutMs, cancellationToken)` 并返回结果。
   - **说明**：不再支持 `shell -s {deviceId} ylzero`，仅当前默认设备；GetDeviceSNAsync 内部仍保留自有实现，待 Step 2 删除。

2. **IsYlzeroResultAcceptableForSnRead**(ProcessExecutionResult result) → **bool**
   - **职责**：封装「ylzero 执行后是否可继续执行 SN 读取」的判别规则（与当前 ReadDeviceSnAsync 注释一致）。
   - **返回 true**：`result.IsSuccess`，或 `result.ExitCode == 127`，或 `result.ExitCode == 255`（即：成功或“可容忍”的失败）。
   - **返回 false**：`result.IsTimeout`，或其它非成功且非 127/255 的 ExitCode。
   - **可选**：在方法内对 127/255 打 Debug 日志（与现 L163–L168 的语义一致），避免三处各自写日志。

调用关系建议：

- **ReadDeviceSnAsync**：调用 `RunYlzeroAsync(TotalTimeoutMs / MaxRetries, token)`，再用 `IsYlzeroResultAcceptableForSnRead(ylzeroResult)` 决定是否继续；若 false，则维持现有重试/返回失败逻辑。
- **ReadDeviceInfoAsync**：调用 `RunYlzeroAsync(TotalTimeoutMs, token)`，与 ReadDeviceSnAsync 使用同一判定逻辑 `IsYlzeroResultAcceptableForSnRead`，仅当结果不可接受时打日志；异常仍在外层 catch，保持“一律继续”。
- **GetDeviceSNAsync**：内部不调用 RunYlzeroAsync，仍保留原有 `shell -s {deviceId} ylzero` 内联实现，待 Step 2 一并删除。

### 3.2 关于 127/255 的日志

- **选项 A**：在 `IsYlzeroResultAcceptableForSnRead` 内，当 result 为 127 或 255 时写 Debug 日志（内容与现 L163–L168 一致），调用方不再重复写。
- **选项 B**：`IsYlzeroResultAcceptableForSnRead` 只做布尔判断，日志仍由各调用方写。

建议 **选项 A**，这样“127/255 可继续”的语义与“如何记录”集中在一处。

### 3.3 第三处（GetDeviceSNAsync）是否与第一处统一 127/255 政策

- **保持现状（不统一）**：GetDeviceSNAsync 内仍为“仅 IsSuccess 且 !IsTimeout 才继续”，127/255 仍导致重试/返回 null。理由：多设备场景可能希望更严格。
- **统一为 Acceptable**：GetDeviceSNAsync 内也使用 `IsYlzeroResultAcceptableForSnRead`，127/255 时继续读 SN。理由：与 ReadDeviceSnAsync 行为一致，避免同一命令在不同入口表现不同。

**评审结论**：统一为 Acceptable，即 127/255 视为可继续读取 SN。

---

## 四、GetDeviceSNAsync 删除可行性确认

经对代码库检索确认：

- **生产代码**：无任何调用 `GetDeviceSNAsync`（MainViewModel、ProcessCoordinator、VersionVerificationFlowService 等均未使用）。
- **仅被单测使用**：`SnVerify.Tests/Services/AdbAccessServicePhase2Tests.cs` 中有多处用例直接调用 `GetDeviceSNAsync`。
- **接口与实现**：`IAdbAccessService` 中声明了 `GetDeviceSNAsync`，`AdbAccessService` 中实现了该方法（即本方案中的「第三处」ylzero 调用所在方法）。

**评审结论**：确认删除 GetDeviceSNAsync，但必须**分阶段**进行，符合工业产线软件安全实践：**不得直接删除接口，必须先 Deprecated（Obsolete）**。

---

## 五、最终评审结论（更新版）

### 5.1 总体结论

✅ **方案通过评审**，建议按统一封装策略实施。

- 将 ylzero 命令执行路径统一为 **RunYlzeroAsync**；
- 将 SN 读取可继续性判定统一为 **IsYlzeroResultAcceptableForSnRead**；
- 消除三处分散判定逻辑（第三处随 GetDeviceSNAsync 在 Phase 2 移除后不再存在）；
- **保持现有产线行为不变**（最重要）。

整体符合单一职责、策略封装、产线稳定优先原则，风险可控。

### 5.2 GetDeviceSNAsync：分阶段删除（已确认）

⚠️ **强烈建议**：不要直接删除接口，必须先 Deprecated。这是工业产线软件的安全实践。

| 阶段 | 动作 | 说明 |
|------|------|------|
| **Step 1（本方案实施）** | 在 **IAdbAccessService** 与 **AdbAccessService** 上对 `GetDeviceSNAsync` 标记 **Obsolete** | 使用 `[Obsolete("Phase2 legacy method. Use ReadDeviceSnAsync instead.")]`，观察一段时间。本阶段**不删除**接口、实现与单测。 |
| **Step 2（后续单独执行）** | 确认产线测试稳定、无外部调用、单测已清理后，再真正删除 | 移除接口声明、实现代码、以及 **AdbAccessServicePhase2Tests** 中所有以 `GetDeviceSNAsync` 为被测对象的用例。 |

因 GetDeviceSNAsync 已确认删除（分两阶段），**方案中「第三处」在 Step 2 完成后不再存在**。系统最终只保留：

- **ReadDeviceSnAsync**（主路径）
- **ReadDeviceInfoAsync**（辅助路径）

### 5.3 ylzero 封装策略（最终版）

**RunYlzeroAsync**（必须统一使用）：

| 职责 | 统一执行 `adb shell ylzero`（ADB 口令），执行一次并返回结果 |
|------|----------------------------------|
| 规则 | 固定命令 `shell ylzero`，不考虑 deviceId（不带设备 ID 的 ylzero 已废弃） |
| 不负责 | 重试、日志策略、业务判定；只做执行。 |

**IsYlzeroResultAcceptableForSnRead**（判定策略统一）：

| 条件 | 结果 |
|------|------|
| ExitCode == 0 | ✔ 可继续 |
| ExitCode == 127 | ✔ 可继续（命令不存在） |
| ExitCode == 255 | ✔ 可继续（user 版本） |
| Timeout | ✘ 不可继续 |
| 其他 ExitCode | ✘ 不可继续 |

**日志策略**：选项 A，在判定方法内部统一记录 Debug 日志（127/255 时）。

---

## 六、实施范围（最终版，评审通过后执行）

### 6.1 本次实施（Step 1：统一封装 + Obsolete）

| 类型 | 项 | 说明 |
|------|-----|------|
| **新增** | **RunYlzeroAsync** | 统一执行 `shell ylzero`（无 deviceId 参数）；不负责重试、日志、业务判定。 |
| **新增** | **IsYlzeroResultAcceptableForSnRead** | 统一 SN 可继续判定；127/255 时在方法内写 Debug 日志。 |
| **修改** | **ReadDeviceSnAsync** | 改为：RunYlzeroAsync → IsYlzeroResultAcceptableForSnRead → 保持原有重试策略与返回语义。 |
| **修改** | **ReadDeviceInfoAsync** | 改为调用 RunYlzeroAsync，并用 IsYlzeroResultAcceptableForSnRead 判定（与 ReadDeviceSnAsync 一致）；保持「一律继续」语义与异常处理。 |
| **废弃（不删除）** | **GetDeviceSNAsync** | 在 **IAdbAccessService** 与 **AdbAccessService** 上为该 method 添加 `[Obsolete("Phase2 legacy method. Use ReadDeviceSnAsync instead.")]`。**本阶段不删除**接口、实现与单测；实现代码可保持现状（第三处 ylzero 仍在内联），以最小化本次变更范围。 |

### 6.2 后续实施（Step 2：真正删除，单独排期）

在确认产线测试稳定、无外部调用、单测已清理后执行：

- 从 **IAdbAccessService** 移除 `GetDeviceSNAsync` 声明；
- 从 **AdbAccessService** 移除 `GetDeviceSNAsync` 实现（含其内 ylzero + SN 读取逻辑）；
- 删除 **AdbAccessServicePhase2Tests** 中所有仅针对 `GetDeviceSNAsync` 的用例。

### 6.3 代码复杂度与风险（评审评价）

| 模块 | 复杂度 |
|------|--------|
| RunYlzeroAsync | 低 |
| 判定策略封装（IsYlzeroResultAcceptableForSnRead） | 低 |
| ReadDeviceSnAsync 改造 | 中 |
| 测试回归 | 低 |

| 风险点 | 评级 |
|--------|------|
| 命令执行路径变化 | 低 |
| 产线行为变化 | 极低（未改语义） |
| 单测失败 | 需回归 |
| 接口删除 | 需分阶段（先 Obsolete 再删） |

本方案属于工业产线软件安全重构：不引入行为破坏性变更，适合逐步落地。评审通过后按 §6.1 实施并跑通现有测试。
