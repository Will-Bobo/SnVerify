/// <author>AI Assistant</author>
/// <remarks>
/// Stage3：产品级 Profile 描述。
/// 用于将 ProductCode 映射到校验模式、ADB 命令集与规则开关。
/// </remarks>

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 产品级 Profile。
    /// </summary>
    public class ProductProfile
    {
        /// <summary>
        /// 产品代码（唯一标识，例如 SOLTAG25、KM001）。
        /// </summary>
        public string ProductCode { get; set; }

        /// <summary>
        /// 产品名称（用于 UI 展示/日志；可与 ProductCode 相同）。
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// 校验模式（Legacy / Phase3）。
        /// </summary>
        public VerificationMode Mode { get; set; }

        /// <summary>
        /// ADB 设备信息读取命令集合。
        /// </summary>
        public DeviceInfoCommandSet AdbCommands { get; set; }

        /// <summary>
        /// 是否启用 ChipId 校验。
        /// </summary>
        public bool EnableChipIdCheck { get; set; }

        /// <summary>
        /// 是否启用 WifiMac 校验。
        /// </summary>
        public bool EnableWifiMacCheck { get; set; }

        /// <summary>
        /// 是否启用主板版本校验。
        /// </summary>
        public bool EnableBoardVersionCheck { get; set; }

        /// <summary>
        /// 是否启用充电板版本校验。
        /// </summary>
        public bool EnableChargeBoardVersionCheck { get; set; }
    }
}

