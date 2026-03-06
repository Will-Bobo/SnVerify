/// <author>AI Assistant</author>
/// <remarks>
/// Stage3：设备信息 ADB 命令集合。
/// 不包含任何业务逻辑，仅描述不同字段的读取命令。
/// </remarks>

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 设备信息读取命令集合。
    /// </summary>
    public class DeviceInfoCommandSet
    {
        /// <summary>
        /// 读取设备 SN 的 ADB 命令。
        /// </summary>
        public string ReadDeviceSn { get; set; }

        /// <summary>
        /// 读取 ChipId 的 ADB 命令。
        /// </summary>
        public string ReadChipId { get; set; }

        /// <summary>
        /// 读取 WifiMac 的 ADB 命令。
        /// </summary>
        public string ReadWifiMac { get; set; }

        /// <summary>
        /// 读取 AndroidVersion 的 ADB 命令。
        /// </summary>
        public string ReadAndroidVersion { get; set; }

        /// <summary>
        /// 读取 BoardVersion 的 ADB 命令。
        /// </summary>
        public string ReadBoardVersion { get; set; }

        /// <summary>
        /// 读取 ChargeBoardVersion 的 ADB 命令。
        /// </summary>
        public string ReadChargeBoardVersion { get; set; }
    }
}

