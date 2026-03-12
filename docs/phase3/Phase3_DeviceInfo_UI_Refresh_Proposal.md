# Phase3 设备信息 UI 刷新方案评审

## 1. 背景与现状

### 1.1 UI 现状

- **MainWindow.xaml**（约 692–734 行）中「设备信息」GroupBox 绑定到 `CurrentDeviceInfo` 的 6 个字段：
  - DeviceSN、ChipId、WifiMac、AndroidVersion、BoardVersion、ChargeBoardVersion
- 该区域仅在 Phase3 产品（如 KM001）下显示（`IsPhase3Product`）。

### 1.2 数据流现状

| 环节 | 说明 |
|------|------|
| **VerificationFlowService.Snapshot** | 来自 ProcessCoordinator.Snapshot，类型为 **VerificationSnapshot**。 |
| **VerificationSnapshot** | 当前仅包含：CurrentSn、**DeviceSN**（string）、IsProcessing、LastResult、FailReason、BatchId、Timestamp。**不包含** ChipId、WifiMac、AndroidVersion 等字段。 |
| **MainViewModel** | 在 `VerificationSnapshot` 的 setter 中，仅当 `_verificationSnapshot.DeviceSN` 非空时，设置 `CurrentDeviceInfo = new DeviceInfo { DeviceSn = _verificationSnapshot.DeviceSN }`，**其余 5 个字段从未被赋值**。 |
| **ProcessCoordinator（Phase3 路径）** | 调用 `IRulePipelineExecutor.ExecuteAsync` 得到 **RuleExecutionResult**，其中包含完整 **DeviceInfo**（含 DeviceSn、ChipId、WifiMac、AndroidVersion、BoardVersion、ChargeBoardVersion）。当前仅将 `execResult?.DeviceInfo?.DeviceSn` 以字符串形式传入 `VerificationSnapshot.Completed(..., deviceSN)`，**未把完整 DeviceInfo 传给 Snapshot**。 |

结论：**界面上除 DeviceSN 外，ChipId、WifiMac、AndroidVersion、BoardVersion、ChargeBoardVersion 均无数据源，不会刷新。**

---

## 2. 目标

- 在 Phase3 校验流程完成（或中间有设备信息时），使「设备信息」区域能展示本次读取到的**完整设备信息**（6 个字段一致由同一数据源驱动）。
- 不改变现有 Snapshot 的语义与事件形态，仅扩展“可选携带 DeviceInfo”，保证向后兼容。

---

## 3. 方案概述

采用 **“在 VerificationSnapshot 上携带可选 DeviceInfo”** 的方式，使 UI 的单一数据源仍为 Snapshot，避免再引入单独事件或“最后设备信息”等第二数据源。

### 3.1 设计要点

1. **VerificationSnapshot** 增加可选只读属性 **DeviceInfo**（`DeviceInfo` 类型，可为 null）。
2. **VerificationSnapshot.Completed** 增加可选参数 `DeviceInfo deviceInfo = null`，在 Phase3 路径下传入本次的 **DeviceInfo**；其余路径保持传 null，行为与现在一致。
3. **ProcessCoordinator** 在 Phase3 完成时，**统一 deviceSN 与 DeviceInfo 来源**（见下 3.1.1），再调用 `VerificationSnapshot.Completed(..., deviceSN, deviceInfo)`。
4. **MainViewModel** 在 `VerificationSnapshot` setter 中按 3.2 节 fallback 更新展示；推荐采用 3.4 节“CurrentDeviceInfo 直接引用 Snapshot”的写法，便于未来扩展。
5. **VersionVerificationFlowService** 等其它构造 `Completed` 的调用方不持有完整 DeviceInfo，继续传 null，无需改业务逻辑。

#### 3.1.1 微调一：DeviceSN 与 DeviceInfo.DeviceSn 统一来源

Snapshot 中同时存在 `string DeviceSN` 与 `DeviceInfo.DeviceSn`，若两处来源不同可能出现不一致。**约束：在 ProcessCoordinator 中只保留单一来源，并加 fallback。**

- 建议写法：
  - `var deviceInfo = execResult?.DeviceInfo;`
  - `var deviceSN = deviceInfo?.DeviceSn?.Trim() ?? execResult?.DeviceSn?.Trim();`（防止未来某个 rule 只返回 SN 而未填 DeviceInfo 时仍能展示 SN）
  - 再调用 `Completed(..., deviceSN, deviceInfo)`。
- 为支持 fallback，**RuleExecutionResult** 需暴露 `DeviceSn`（如 `public string DeviceSn => DeviceInfo?.DeviceSn;`）；若未来 rule 可仅返回 SN，可再扩展为可选构造参数。

### 3.2 不可变与 UI 绑定（微调二：Snapshot 持有 clone）

- **VerificationSnapshot** 为不可变 DTO，若 **DeviceInfo** 直接暴露调用方传入的引用，外部修改会破坏不可变语义并影响 UI。
- **推荐（明确为 Snapshot 侧拷贝）**：在 **VerificationSnapshot** 构造函数/静态工厂内，对传入的 `deviceInfo` 做**浅拷贝**后保存并暴露，Snapshot 仅持有并暴露该拷贝。调用方（ProcessCoordinator）直接传 `execResult?.DeviceInfo` 即可，无需在 Coordinator 内拷贝。
- 拷贝方式二选一即可：
  - 在 Domain 层为 `DeviceInfo` 增加静态方法 `DeviceInfo Clone(DeviceInfo src)`，或
  - 在 Snapshot 内 `new DeviceInfo { DeviceSn = src?.DeviceSn, ChipId = src?.ChipId, ... }` 逐字段赋值。

### 3.3 清空时机与 UI 策略

- 当 Snapshot 变为 **Idle** 或 **Processing** 时，Snapshot 的 **DeviceInfo** 为 null，MainViewModel 通过 fallback 将 **CurrentDeviceInfo** 置为仅含 DeviceSN 或空对象（`new DeviceInfo()`），避免绑定 null。
- **产品策略说明**：Idle 时使用 `new DeviceInfo()` 或仅 DeviceSN 的 fallback 属于实现选择；若产品希望“下一次扫码前保留上一次设备信息以便核对”，可保持“仅在有新 DeviceInfo 时更新、否则保留上次展示”的语义（本方案 fallback 已支持：无 DeviceInfo 时仍可用 DeviceSN 或空对象，不强制清空）。

### 3.4 CurrentDeviceInfo 改为 get-only（已采纳）

- **结论**：**直接采用** get-only 实现，不再保留“setter 内赋值 _currentDeviceInfo”的方案 A。
- **方案 B（采用）**：VerificationSnapshot → CurrentDeviceInfo getter → UI；不维护 `_currentDeviceInfo` 字段。

**推荐实现：**

```csharp
public DeviceInfo CurrentDeviceInfo
{
    get
    {
        if (_verificationSnapshot?.DeviceInfo != null)
            return _verificationSnapshot.DeviceInfo;

        if (!string.IsNullOrWhiteSpace(_verificationSnapshot?.DeviceSN))
            return new DeviceInfo { DeviceSn = _verificationSnapshot.DeviceSN };

        return new DeviceInfo();
    }
}
```

VerificationSnapshot setter 中：

```csharp
set
{
    _verificationSnapshot = value;
    OnPropertyChanged(nameof(VerificationSnapshot));
    OnPropertyChanged(nameof(CurrentDeviceInfo));
}
```

**优势**：消除冗余状态、Snapshot 为唯一事实源、扩展方便（新字段直接绑定）、测试只需测 Snapshot。

**未来扩展**：Phase3 可能新增 IMEI、ModemVersion、ICCID、Battery、Temperature、BuildType 等；CurrentDeviceInfo get-only 时，UI 仅需新增绑定如 `{Binding CurrentDeviceInfo.ModemVersion}`，无需改 ViewModel。

---

## 4. 影响范围

| 文件/层 | 变更 |
|---------|------|
| **Domain/State/VerificationSnapshot.cs** | 增加只读属性 `DeviceInfo`；`Completed(..., deviceSN, DeviceInfo deviceInfo = null)`；构造/工厂内对 `deviceInfo` 做浅拷贝后保存（保证不可变）。 |
| **Services/Coordination/ProcessCoordinator.cs** | Phase3 完成处：`var deviceInfo = execResult?.DeviceInfo;`，`var deviceSN = deviceInfo?.DeviceSn?.Trim() ?? execResult?.DeviceSn?.Trim();`，再 `Completed(..., deviceSN, deviceInfo)`（统一来源 + fallback）。 |
| **ViewModels/MainViewModel.cs** | **已采纳**：`CurrentDeviceInfo` 改为 get-only（见 3.4），移除 `_currentDeviceInfo` 字段；`VerificationSnapshot` setter 内仅 `OnPropertyChanged(nameof(CurrentDeviceInfo))`。 |
| **MainWindow.xaml** | 无需改动，已绑定 `CurrentDeviceInfo.*`。 |
| **Services/Rules/RuleExecutionResult.cs** | 增加只读属性 `DeviceSn => DeviceInfo?.DeviceSn`，供 ProcessCoordinator 中 deviceSN fallback 使用。 |
| **VersionVerificationFlowService.cs** | 无需改参数（继续传 null）。 |
| **单元测试** | 现有 `Completed(...)` 调用保持兼容（新参数默认 null）；可补充“Phase3 完成时 UI 收到完整 DeviceInfo”的用例。 |

---

## 5. 实施步骤建议（评审通过后执行）

推荐实施顺序：**1 → 2 → 3 → 4 → 5**。

1. **VerificationSnapshot**  
   - 增加只读属性 `DeviceInfo`。  
   - `Completed(currentSn, result, failReason, batchId, deviceSN, DeviceInfo deviceInfo = null)`，私有构造/工厂增加参数；**构造时对 deviceInfo 做浅拷贝后保存**（如 `DeviceInfo` 静态 Clone 或 `new DeviceInfo { ... }` 逐字段赋值），Snapshot 仅暴露该拷贝，保证不可变。

2. **ProcessCoordinator**  
   - Phase3 完成处：`var deviceInfo = execResult?.DeviceInfo;`，`var deviceSN = deviceInfo?.DeviceSn?.Trim() ?? execResult?.DeviceSn?.Trim();`，再 `VerificationSnapshot.Completed(..., deviceSN, deviceInfo)`（**统一来源 + fallback**）。

3. **MainViewModel**  
   - **已采纳 get-only**：移除 `_currentDeviceInfo` 字段；`CurrentDeviceInfo` 仅 getter，按 3.4 节实现（DeviceInfo 优先 → DeviceSN fallback → new DeviceInfo()）；`VerificationSnapshot` setter 内 `OnPropertyChanged(nameof(CurrentDeviceInfo))`。

4. **测试**  
   - 确认所有 `VerificationSnapshot.Completed(...)` 调用处编译通过（新参数默认 null）。  
   - 可选：增加用例验证 Phase3 完成后 UI 展示的 6 个字段与规则链返回的 DeviceInfo 一致。

5. **编译与回归**  
   - 按项目约定执行编译与全量测试，确保通过。

---

## 6. 验收标准

- Phase3 产品（如 KM001）完成一次扫码校验后，「设备信息」区域 6 个字段（DeviceSN、ChipId、WifiMac、AndroidVersion、BoardVersion、ChargeBoardVersion）均能显示本次 ADB/规则链返回的对应值。
- 切换为 Idle 或仅 Processing 时，设备信息区不出现错误（可显示为空或仅保留上次 DeviceSN，按产品约定二选一）。
- 现有非 Phase3 路径及 VersionVerificationFlowService 行为不变；所有现有单元测试通过。

---

## 7. 评审结论

**整体方法同意。评审结论：通过，按本方案实施。**

### 7.1 正式评审结论

| 维度 | 评价 |
|------|------|
| 架构方向 | ✅ 正确 |
| 数据流设计 | ✅ 清晰 |
| UI 绑定模型 | ✅ 合理 |
| 兼容性 | ⭐⭐⭐⭐⭐ |
| 实现复杂度 | ⭐ 低 |

**结论：评审通过，建议按本方案实施。**

数据流确认为标准 **Flow Snapshot → ViewModel → UI**：RuleExecutionResult → ProcessCoordinator → VerificationSnapshot（携带 DeviceInfo）→ MainViewModel → CurrentDeviceInfo → UI。单一数据源、无额外事件、状态一致、易测试。

### 7.2 方案微调与采纳项（已纳入上文）

- **微调一**：ProcessCoordinator 中 deviceSN 统一来源并加 fallback：`deviceSN = deviceInfo?.DeviceSn?.Trim() ?? execResult?.DeviceSn?.Trim();`，见 3.1.1；RuleExecutionResult 暴露 `DeviceSn`。
- **微调二**：VerificationSnapshot 在构造/工厂内对 deviceInfo 做浅拷贝后保存并暴露，保证不可变，见 3.2。
- **已采纳**：CurrentDeviceInfo 改为 get-only（方案 B），见 3.4；消除冗余状态，未来 DeviceInfo 扩展（如 IMEI、ModemVersion、Battery 等）时 UI 仅需新增绑定，无需改 ViewModel。

### 7.3 小风险提醒（易忽略）

- **DeviceInfo 未来可能增长**：当前 6 个字段外，Phase3 很可能再增 IMEI、ModemVersion、ICCID、Battery、Temperature、BuildType 等。采用 CurrentDeviceInfo get-only 后，UI 扩展仅需例如 `<TextBlock Text="{Binding CurrentDeviceInfo.ModemVersion}" />`，无需改 ViewModel，这也是强烈建议采纳 3.4 的原因。

---

*文档版本：v3（含评审结论、get-only 采纳、deviceSN fallback、扩展性说明）*  
*放置路径：docs/phase3/Phase3_DeviceInfo_UI_Refresh_Proposal.md*
