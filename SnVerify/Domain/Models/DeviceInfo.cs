/// <author>
/// AI Assistant
/// </author>

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 设备信息模型，封装一次 ADB 读取到的核心字段。
    /// 仅包含数据属性，不包含任何业务方法，便于绑定到 Snapshot / UI。
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>
        /// 设备 SN（从设备内部读取）
        /// </summary>
        public string DeviceSn { get; set; }

        /// <summary>
        /// WiFi MAC 地址
        /// </summary>
        public string WifiMac { get; set; }

        /// <summary>
        /// 芯片 ID（ChipId）
        /// </summary>
        public string ChipId { get; set; }

        /// <summary>
        /// 主板版本号（BoardVersion）
        /// </summary>
        public string BoardVersion { get; set; }

        /// <summary>
        /// 充电小板版本号（ChargeBoardVersion）
        /// </summary>
        public string ChargeBoardVersion { get; set; }

        /// <summary>
        /// Android 系统版本号
        /// </summary>
        public string AndroidVersion { get; set; }
    }
}

