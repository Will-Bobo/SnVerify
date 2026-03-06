/// <author>
/// AI Assistant
/// </author>

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 项目级版本校验参数。
    /// 对应持久化表 VerificationParameter，用于从 UI 配置并在流程中读取期望版本。
    /// </summary>
    public class VerificationParameter
    {
        /// <summary>
        /// 项目标识（通常对应 ProductName / ProjectId）
        /// </summary>
        public string ProjectId { get; set; }

        /// <summary>
        /// 期望的 Android 系统版本
        /// </summary>
        public string ExpectedAndroidVersion { get; set; }

        /// <summary>
        /// 期望的主板版本号
        /// </summary>
        public string ExpectedBoardVersion { get; set; }

        /// <summary>
        /// 期望的充电板版本号
        /// </summary>
        public string ExpectedChargeBoardVersion { get; set; }
    }
}

