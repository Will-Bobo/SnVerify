/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// SN 校验结果模型
    /// </summary>
    public class SnVerifyResult
    {
        /// <summary>
        /// 自增 ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 所属批次 ID
        /// </summary>
        public string BatchId { get; set; }

        /// <summary>
        /// 扫码 / 设备 SN
        /// </summary>
        public string SN { get; set; }

        /// <summary>
        /// 设备 SN（从设备内部读取，如 ADB）
        /// 当 PASS 时，DeviceSN 与 SN（StickerSN）相同
        /// 当 FAIL 时，DeviceSN 记录实际读取到的设备 SN（如果读取成功）
        /// </summary>
        public string DeviceSN { get; set; }

        /// <summary>
        /// 校验结果：PASS / FAIL / TIMEOUT
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// 失败原因（不一致 / 超时 / 重复 SN）
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// 校验完成时间
        /// </summary>
        public DateTime VerifyTime { get; set; }
    }
}
