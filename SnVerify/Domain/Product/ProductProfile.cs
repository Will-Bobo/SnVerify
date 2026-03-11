/// <author>AI Assistant</author>
/// <remarks>
/// Stage3：产品级 Profile 描述。
/// 用于将 ProductCode 映射到校验模式、ADB 配置与规则开关。
/// </remarks>

using SnVerify.Domain.DeviceAccess;
using System.Collections.Generic;

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
        /// 产品展示名称（用于 UI 展示/日志；可与 ProductCode 相同）。
        /// 仅表示“产品类型的可读名称”，不参与批次唯一性判断。
        /// </summary>
        public string ProductDisplayName { get; set; }

        /// <summary>
        /// 校验模式（Legacy / Phase3）。
        /// </summary>
        public VerificationMode Mode { get; set; }

        /// <summary>
        /// 设备访问 ADB 配置（Bootstrap、聚合或字段命令）。Phase3 设备读取由此配置驱动；为 null 时视为未配置。
        /// </summary>
        public DeviceAdbConfig AdbConfig { get; set; }

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

        /// <summary>
        /// 字段显示标签映射（按产品差异化语义定义）。
        /// 例如：KM001 的 BoardVersion 可映射为“芯片版本号”。
        /// </summary>
        public Dictionary<DeviceInfoField, string> FieldLabels { get; set; }
    }
}

