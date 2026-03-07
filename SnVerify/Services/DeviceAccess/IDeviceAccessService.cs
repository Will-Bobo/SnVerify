/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：设备访问服务接口。Phase3 设备读取由此接口提供。</remarks>

using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;

namespace SnVerify.Services.DeviceAccess
{
    /// <summary>
    /// 设备访问服务：按 ProductProfile.AdbConfig 读取设备信息。规则链仅依赖此接口，与 ADB 协议解耦。
    /// </summary>
    public interface IDeviceAccessService
    {
        /// <summary>
        /// 按产品配置读取设备信息。profile.AdbConfig 为 null 或未配置有效命令时抛异常。
        /// </summary>
        /// <param name="profile">产品配置（含 AdbConfig）。</param>
        /// <returns>设备信息；读取失败时抛异常或部分字段为 null。</returns>
        Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile);
    }
}
