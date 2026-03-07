/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：设备 ADB 配置。Domain 层。</remarks>

using System.Collections.Generic;

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 设备访问 ADB 配置（Bootstrap、聚合命令或字段命令）。
    /// 执行策略：若 AggregateCommand != null 则仅走聚合路径，否则仅走 FieldCommands。
    /// </summary>
    public class DeviceAdbConfig
    {
        /// <summary>设备初始化命令规格（Session 级，首次读取时执行；含退出码宽容与超时策略）。</summary>
        public List<BootstrapCommandSpec> BootstrapCommandSpecs { get; set; }

        /// <summary>聚合命令（可选）。若配置则仅使用聚合路径，禁止与 Commands 混用。</summary>
        public AggregateDeviceInfoCommand AggregateCommand { get; set; }

        /// <summary>按字段执行的命令列表。仅当 AggregateCommand 为 null 时使用。</summary>
        public List<DeviceInfoCommand> Commands { get; set; }
    }
}
