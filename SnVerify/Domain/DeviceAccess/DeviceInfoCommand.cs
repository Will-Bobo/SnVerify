/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：单字段命令配置。仅存 ParserKey，不存 Parser 实例。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 单字段设备信息读取命令配置。
    /// </summary>
    public class DeviceInfoCommand
    {
        /// <summary>目标字段。</summary>
        public DeviceInfoField Field { get; set; }

        /// <summary>ADB 命令（如 shell getprop ro.serialno）。</summary>
        public string Command { get; set; }

        /// <summary>解析器 Key，由 ParserFactory 按 Key 提供 IDeviceInfoParser。禁止在配置中创建 Parser 实例。</summary>
        public string ParserKey { get; set; }
    }
}
