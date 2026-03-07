/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：聚合命令配置。仅存 ParserKey，不存 Parser 实例。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 聚合设备信息命令配置（一条命令返回多字段）。
    /// </summary>
    public class AggregateDeviceInfoCommand
    {
        /// <summary>ADB 命令（如 shell get_device_info）。</summary>
        public string Command { get; set; }

        /// <summary>解析器 Key，由 ParserFactory 按 Key 提供 IAggregateDeviceInfoParser。</summary>
        public string ParserKey { get; set; }
    }
}
