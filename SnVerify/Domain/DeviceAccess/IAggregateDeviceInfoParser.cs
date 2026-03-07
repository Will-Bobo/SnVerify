/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：聚合解析器接口。Domain 层。</remarks>

using System.Threading.Tasks;
using SnVerify.Domain.Models;

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 聚合设备信息解析器（一条命令输出解析为 DeviceInfo）。由 ParserFactory 按 Key 提供。
    /// </summary>
    public interface IAggregateDeviceInfoParser
    {
        /// <summary>解析聚合命令输出为 DeviceInfo。</summary>
        DeviceInfo Parse(string output);
    }
}
