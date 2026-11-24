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
        Phase3 = 1,

        /// <summary>
        /// 独立产品轮次。扫码不进入 Phase3 规则链，也不进入 Legacy 决策树。
        /// 具体产品在扫码入口进入自己的挂载点。
        /// </summary>
        Independent = 2
    }
}

