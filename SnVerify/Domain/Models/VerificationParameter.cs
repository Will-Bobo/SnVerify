/// <author>
/// AI Assistant
/// </author>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 会话级版本校验参数快照。
    /// 对应持久化表 VerificationParameter（Session 维度），用于在批次内读取期望版本。
    /// </summary>
    public class VerificationParameter
    {
        /// <summary>
        /// 自增主键。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 会话主键（FK -> TestSession.Id）。
        /// </summary>
        public int SessionId { get; set; }

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

        /// <summary>
        /// 参数快照创建时间。
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}

