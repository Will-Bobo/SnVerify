/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// Phase 2.5 Step 6：TestRecord 模型，使用 INT SessionId 关联 TestSession。
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// SN 粒度测试记录。不冗余 Product/Order，通过 SessionId 关联 TestSession。
    /// </summary>
    public class TestRecord
    {
        /// <summary>
        /// 自增主键 Id。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 所属会话 Id（FK -> TestSession.Id）。
        /// </summary>
        public int SessionId { get; set; }

        /// <summary>
        /// 贴纸 SN（扫码输入）。
        /// </summary>
        public string StickerSN { get; set; }

        /// <summary>
        /// 设备 SN（从设备读取），允许为 null（例如 ADB 失败）。
        /// </summary>
        public string DeviceSN { get; set; }

        /// <summary>
        /// 校验结果：PASS / FAIL / TIMEOUT。
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// 失败原因（可选）。
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// 校验完成时间。
        /// </summary>
        public DateTime VerifyTime { get; set; }
    }
}

