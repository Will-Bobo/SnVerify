DeviceAccess_Subsystem_Architecture_v1.md
1. 文档目的

本文档定义 DeviceAccess 子系统架构规范 v1.0。

目标：

合并 ProjectProfile 与 ProductProfile

将 ADB 访问升级为独立 DeviceAccess 子系统

设备访问与业务规则 完全解耦

新增产品 无需修改业务代码

建立长期稳定的设备访问架构

适用范围：

Phase3 设备信息读取

未来所有 Android / ADB 设备访问

2. 总体架构
UI
 ↓
VerificationFlowService
 ↓
ProcessCoordinator
 ↓
RulePipelineExecutor
 ↓
IDeviceAccessService
 ↓
DeviceAccess Subsystem
    ├── Session
    ├── Command
    ├── Parser
    └── Service
 ↓
ADB

核心原则：

原则	说明
业务与设备协议解耦	RulePipeline 不关心 ADB
配置驱动	产品行为由配置决定
Parser 解耦	每种设备输出独立 Parser
单一事实源	ProductProfile 为唯一产品配置
稳定扩展	新产品 = 配置 + Parser
3. Profile 合并策略
删除类型

删除以下类型：

Domain.Models.ProjectProfile
IProductProfileFactory
ProductProfileFactory

原因：

与 ProductProfile 语义重复

Phase3 流程未使用

导致两套配置形状

唯一配置类型

系统仅保留：

Domain.Product.ProductProfile

新增属性：

DeviceAdbConfig AdbConfig

ProductProfile 示例：

public class ProductProfile
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public ProductMode Mode { get; set; }

    public DeviceAdbConfig AdbConfig { get; set; }

    public bool EnableChipIdCheck { get; set; }
    public bool EnableWifiMacCheck { get; set; }
}
4. 分层结构

必须遵守以下依赖方向：

Domain
   ↑
Infrastructure

禁止：

Domain → Infrastructure
Domain 层结构
Domain
 ├── Product
 │    ProductProfile
 │
 └── DeviceAccess
      DeviceAdbConfig
      BootstrapTimeoutBehavior
      BootstrapCommandSpec
      DeviceInfoCommand
      AggregateDeviceInfoCommand
      DeviceInfoField
      IDeviceInfoParser
      IAggregateDeviceInfoParser

Domain 只包含：

配置 DTO

Parser 接口

枚举

不包含任何实现。

Infrastructure 层结构
Infrastructure
 └── DeviceAccess
      ├── Session
      │     DeviceSessionManager
      │
      ├── Command
      │     DeviceCommandExecutor
      │
      ├── Parser
      │     ParserFactory
      │     TrimParser
      │     SoltagParser
      │
      └── Service
            AdbDeviceService
5. DeviceAccess 配置模型
DeviceAdbConfig
public class DeviceAdbConfig
{
    public List<BootstrapCommandSpec> BootstrapCommandSpecs { get; set; }

    public AggregateDeviceInfoCommand AggregateCommand { get; set; }

    public List<DeviceInfoCommand> Commands { get; set; }
}
BootstrapCommandSpec（退出码宽容 + 超时策略）
public class BootstrapCommandSpec
{
    public string Command { get; set; }

    public int[] AcceptableExitCodes { get; set; }

    public BootstrapTimeoutBehavior TimeoutBehavior { get; set; } = BootstrapTimeoutBehavior.Fail;
}
BootstrapTimeoutBehavior
public enum BootstrapTimeoutBehavior
{
    Fail,   // 超时视为失败（默认）
    Ignore, // 超时视为通过，Warmup 宽容
    Retry   // 超时后重试（次数上限由实现约定）
}
DeviceInfoCommand
public class DeviceInfoCommand
{
    public DeviceInfoField Field { get; set; }

    public string Command { get; set; }

    public string ParserKey { get; set; }
}
AggregateDeviceInfoCommand
public class AggregateDeviceInfoCommand
{
    public string Command { get; set; }

    public string ParserKey { get; set; }
}
DeviceInfoField
enum DeviceInfoField
{
    DeviceSn,
    ChipId,
    WifiMac,
    AndroidVersion,
    BoardVersion,
    ChargeBoardVersion
}
6. Parser 体系

Parser 不存储在配置中。

配置只存：

ParserKey

Parser 由 ParserFactory 提供。

Parser 接口
字段 Parser
public interface IDeviceInfoParser
{
    string Parse(string output);
}
聚合 Parser
public interface IAggregateDeviceInfoParser
{
    DeviceInfo Parse(string output);
}
ParserFactory
public interface IParserFactory
{
    IDeviceInfoParser Get(string key);

    IAggregateDeviceInfoParser GetAggregate(string key);
}

Parser 实现通过 DI 注册。

示例：

TrimParser
SoltagParser
Km001Parser
7. 执行策略
Aggregate 与 Field 执行规则

执行策略必须确定：

if AggregateCommand != null
    使用 Aggregate
else
    使用 FieldCommands

禁止：

Aggregate + Field 混合执行

原因：

避免字段覆盖

避免重复读取

保证行为确定

8. Bootstrap 机制与 Session 模型

**Session 模型**：采用 **Environment Session Model（上位机环境会话模型）**。SessionReady 仅表示上位机环境下 Shell 通道是否已建立，**不**绑定设备身份，**不**引入设备探测或额外 IO。

**Warmup（环境级）**：进程生命周期内只执行一次，用于防止 adb shell 首次执行失败。由 _warmupDone 屏蔽重复执行。

**BootstrapCommandSpecs（协议初始化）**：**每检测批次执行**（每次检测流程触发时执行），不依赖设备状态，不增加额外 IO。

规则：

| 情况 | 行为 |
|------|------|
| 每次检测流程触发 | 若 Warmup 未执行 → 执行 Shell warmup 并标记；然后执行 BootstrapCommandSpecs |
| Warmup 已完成 | 跳过 Shell warmup；仍执行 BootstrapCommandSpecs |
| 单条命令 **超时** | 按 **TimeoutBehavior**：Fail → 失败；Ignore → 视为通过；Retry → 重试（上限由实现约定，如 2 次） |
| 单条命令 **非超时** | IsSuccess 或 ExitCode ∈ AcceptableExitCodes → 通过；否则失败 |

退出码宽容与超时策略详见：`docs/Bootstrap_Tolerant_ExitCodes_Proposal.md`。SessionReady 语义详见：`docs/DeviceAccess_SessionReady_PerDevice_Proposal.md`。

9. DeviceSessionManager

职责：

| 职责 | 说明 |
|------|------|
| ADB 连接管理 | 新增 |
| 设备数量检查 | CheckMultipleDevices |
| Shell warmup | 环境级，进程内只执行一次（_warmupDone） |
| Bootstrap 执行 | 每检测批次执行 BootstrapCommandSpecs，支持 AcceptableExitCodes、TimeoutBehavior |

Session 生命周期（环境级）：

每次 EnsureSessionReadyAsync 调用
    ↓
若 !_warmupDone → Shell warmup → _warmupDone = true
    ↓
若配置了 BootstrapCommandSpecs → 逐条执行（每批次都执行）
    ↓
继续后续 Aggregate / Field 命令
10. DeviceAccessService

接口：

public interface IDeviceAccessService
{
    Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile);
}

实现：

AdbDeviceService

执行流程：

EnsureSessionReady（Warmup 若未执行则执行一次；BootstrapCommandSpecs 每次调用都执行）
        ↓
Aggregate 或 Field 命令
        ↓
ParserFactory
        ↓
DeviceInfo
11. ProductRegistry 配置

新增产品仅需配置：

- ProductProfile
- DeviceAdbConfig（含 BootstrapCommandSpecs、AggregateCommand 或 Commands）
- ParserKey

示例：

**KM001（Phase3，ylzero 宽容 127/255 + 超时 Ignore）**

- BootstrapCommandSpecs: `shell ylzero`，AcceptableExitCodes = [127, 255]，TimeoutBehavior = Ignore
- FieldCommands: SN → getprop ro.serialno → ParserKeys.Field.Trim；AndroidVersion → getprop ro.build.display.id → ParserKeys.Field.Trim（配置与注册处须使用 Domain/DeviceAccess/Parsing/ParserKeys 常量，禁止魔法字符串）

**SOLTAG25（Legacy）**

- AdbConfig = null（走 IAdbAccessService 等 Legacy 路径）

**若为 Phase3 + 聚合命令**

- BootstrapCommandSpecs: 按需配置
- AggregateCommand: 单条命令 → SoltagParser
12. 新产品接入流程

新增产品步骤：

1️⃣ 新增 ProductProfile
2️⃣ 配置 DeviceAdbConfig
3️⃣ 新增 Parser（如需要）

无需修改：

RulePipeline

ProcessCoordinator

UI

DeviceAccessService

13. Legacy 兼容策略

Legacy 与调试路径继续使用：

IAdbAccessService

Phase3 使用：

IDeviceAccessService

两者并存。

14. 迁移步骤

实施顺序：

Step1

删除：

ProjectProfile
IProductProfileFactory
ProductProfileFactory
Step2

新增：

Domain.DeviceAccess

类型：

DeviceAdbConfig（BootstrapCommandSpecs）

BootstrapTimeoutBehavior

BootstrapCommandSpec

DeviceInfoCommand

AggregateDeviceInfoCommand

DeviceInfoField

Parser 接口

Step3

实现：

DeviceSessionManager
DeviceCommandExecutor
ParserFactory
Parser实现
AdbDeviceService
Step4

RulePipelineExecutor：

使用 IDeviceAccessService
Step5

ProductRegistry：

迁移为：

DeviceAdbConfig
15. 架构约束（强制）

以下规则必须遵守：

1 Domain 不得依赖 Infrastructure
Domain → Infrastructure  ❌
Infrastructure → Domain  ✅
2 Parser 不得在配置中实例化

禁止：

Parser = new TrimParser()

必须：

ParserKey = ParserKeys.Field.Trim（或其它 ParserKeys 常量；禁止魔法字符串，见 Domain/DeviceAccess/Parsing/ParserKeys.cs）
3 Aggregate 与 Field 不可混用

只能选择一种。

4 Session 为环境级（Environment Session Model）

SessionReady 仅屏蔽 Warmup 成本；BootstrapCommandSpecs 每检测批次执行。禁止按设备身份绑定 Session、禁止 adb devices 探测与额外 IO。

5 Bootstrap 行为由 BootstrapCommandSpecs 配置

支持退出码宽容（AcceptableExitCodes）与超时策略（TimeoutBehavior：Fail / Ignore / Retry）。

16. 架构版本
DeviceAccess Subsystem Architecture
Version: v1.0
Status: Approved