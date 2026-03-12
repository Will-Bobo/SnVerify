DeviceAccess_Subsystem_Implementation_Guide.md
1. 文档目的

本文档定义 DeviceAccess 子系统的实现步骤与开发约束。

目标：

指导 Cursor / AI / 开发者 按正确顺序实施架构

避免中途编译错误

避免架构被破坏

保证每一步可运行、可回滚

本文档必须与：

DeviceAccess_Subsystem_Architecture_v1.md

配合使用。

2. 实施总体策略

实施原则：

原则	说明
小步提交	每一步必须可编译
不同时修改多个子系统	避免大规模破坏
先建结构	再迁移调用
先兼容	再删除旧代码

实施顺序：

Step1 删除旧 Profile
Step2 建立 Domain.DeviceAccess
Step3 建立 Infrastructure.DeviceAccess
Step4 实现 DeviceAccessService
Step5 修改 RulePipelineExecutor
Step6 迁移 ProductRegistry
Step7 清理旧代码
3. Step1 删除旧 Profile

删除以下文件：

Domain/Models/ProjectProfile.cs

删除接口：

IProductProfileFactory
ProductProfileFactory

修改：

ProcessCoordinator.ProcessScanAsync

原：

ProcessScanAsync(string sn, string projectId, ProjectProfile profile)

改为：

ProcessScanAsync(string sn, string projectId)

并删除所有 ProjectProfile 引用。

完成检查：

项目 必须可编译

不得残留 ProjectProfile

4. Step2 建立 Domain.DeviceAccess

创建目录：

Domain
 └── DeviceAccess

新增类型：

DeviceAdbConfig
public class DeviceAdbConfig
{
    public List<string> BootstrapCommands { get; set; }

    public AggregateDeviceInfoCommand AggregateCommand { get; set; }

    public List<DeviceInfoCommand> Commands { get; set; }
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
public enum DeviceInfoField
{
    DeviceSn,
    ChipId,
    WifiMac,
    AndroidVersion,
    BoardVersion,
    ChargeBoardVersion
}
Parser Interfaces
public interface IDeviceInfoParser
{
    string Parse(string output);
}
public interface IAggregateDeviceInfoParser
{
    DeviceInfo Parse(string output);
}
5. Step3 修改 ProductProfile

修改：

Domain/Product/ProductProfile.cs

新增：

public DeviceAdbConfig AdbConfig { get; set; }

删除旧字段：

DeviceInfoCommandSet AdbCommands

完成检查：

编译成功

ProductRegistry 暂时可以返回 AdbConfig = null

6. Step4 建立 Infrastructure.DeviceAccess

创建目录：

Infrastructure
 └── DeviceAccess
      ├── Session
      ├── Command
      ├── Parser
      └── Service
7. Step5 实现 DeviceSessionManager

位置：

Infrastructure/DeviceAccess/Session

职责：

ADB 连接管理

Bootstrap 执行

Shell warmup

设备数量检查

基本结构：

public class DeviceSessionManager
{
    private bool _sessionReady;

    public async Task EnsureSessionReady(DeviceAdbConfig config)
    {
        if (_sessionReady)
            return;

        await ExecuteBootstrap(config);

        _sessionReady = true;
    }
}
8. Step6 实现 DeviceCommandExecutor

位置：

Infrastructure/DeviceAccess/Command

职责：

执行 ADB shell 命令。

public class DeviceCommandExecutor
{
    public async Task<string> ExecuteAsync(string command)
    {
        // 调用 adb shell
    }
}
9. Step7 实现 ParserFactory

位置：

Infrastructure/DeviceAccess/Parser

接口：

public interface IParserFactory
{
    IDeviceInfoParser Get(string key);

    IAggregateDeviceInfoParser GetAggregate(string key);
}

示例实现：

public class ParserFactory : IParserFactory
{
    private readonly Dictionary<string, IDeviceInfoParser> _parsers;

    public IDeviceInfoParser Get(string key)
    {
        return _parsers[key];
    }
}
10. Step8 实现 Parser 示例
TrimParser
public class TrimParser : IDeviceInfoParser
{
    public string Parse(string output)
    {
        return output.Trim();
    }
}
11. Step9 实现 DeviceAccessService

位置：

Infrastructure/DeviceAccess/Service

接口：

public interface IDeviceAccessService
{
    Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile);
}

实现：

public class AdbDeviceService : IDeviceAccessService
{
    public async Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile)
    {
        // 1 Session
        // 2 Bootstrap
        // 3 Aggregate or Field
        // 4 Parser
    }
}
12. Step10 修改 RulePipelineExecutor

替换：

旧：

IAdbAccessService.ReadDeviceInfoAsync(ProjectProfile)

新：

IDeviceAccessService.ReadDeviceInfoAsync(ProductProfile)

删除：

new ProjectProfile()
13. Step11 修改 ProductRegistry

示例：

AdbConfig = new DeviceAdbConfig
{
    BootstrapCommands = new List<string>
    {
        "shell ylzero"
    },
    Commands = new List<DeviceInfoCommand>
    {
        new DeviceInfoCommand
        {
            Field = DeviceInfoField.DeviceSn,
            Command = "shell getprop ro.serialno",
            ParserKey = ParserKeys.Field.Trim
        }
    }
}

注意：

配置只允许使用 ParserKey，且必须引用 **ParserKeys** 常量（Domain/DeviceAccess/Parsing/ParserKeys.cs），禁止魔法字符串。

禁止：

Parser = new TrimParser()
14. Step12 DI 注册

在 Startup / Program 注册：

services.AddSingleton<IParserFactory, ParserFactory>();

services.AddSingleton<IDeviceAccessService, AdbDeviceService>();

services.AddSingleton<IDeviceInfoParser, TrimParser>();
15. Step13 最终清理

删除：

DeviceInfoCommandSet

删除：

RulePipelineExecutor 中所有 ProjectProfile 代码

删除：

IProductProfileFactory
16. 验证清单

必须通过以下检查：

编译
dotnet build

无错误。

架构检查

确认：

Domain 不依赖 Infrastructure
Parser检查

确认：

配置没有 new Parser
Bootstrap检查

确认：

Bootstrap 只执行一次
17. 回滚策略

若实施失败：

回滚到：

Step1 之前

重新执行步骤。

18. 最终状态

完成后系统结构：

DeviceAccess Subsystem
    ├── Session
    ├── Command
    ├── Parser
    └── Service

业务层仅依赖：

IDeviceAccessService

新增产品仅需：

ProductProfile + Parser

无需修改：

RulePipeline
ProcessCoordinator
UI
19. 实施完成标志

当满足以下条件：

ProjectProfile 已删除

ProductProfile 统一配置

RulePipeline 使用 IDeviceAccessService

DeviceAccess 子系统存在

ProductRegistry 使用 DeviceAdbConfig

则实施完成。