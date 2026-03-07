/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：设备信息字段枚举。Domain 层，不依赖 Infrastructure。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 设备信息字段枚举。
    /// </summary>
    public enum DeviceInfoField
    {
        DeviceSn,
        ChipId,
        WifiMac,
        AndroidVersion,
        BoardVersion,
        ChargeBoardVersion
    }
}
