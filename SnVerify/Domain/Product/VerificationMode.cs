/// <author>AI Assistant</author>
/// <remarks>
/// Stage3：产品级校验模式枚举。
/// </remarks>

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 产品级校验模式。
    /// </summary>
    public enum VerificationMode
    {
        /// <summary>
        /// 兼容旧版产线（Legacy）模式。
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// Phase3 扩展校验模式（启用 ChipId / WifiMac / 多版本校验）。
        /// </summary>
        Phase3 = 1
    }
}

