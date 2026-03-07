/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：单字段解析器接口。Domain 层。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 单字段设备信息解析器（由 ParserFactory 按 Key 提供，不存储在配置中）。
    /// </summary>
    public interface IDeviceInfoParser
    {
        /// <summary>解析命令输出为单字段值。</summary>
        string Parse(string output);
    }
}
