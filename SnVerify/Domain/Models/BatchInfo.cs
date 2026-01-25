/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 批次信息模型
    /// </summary>
    public class BatchInfo
    {
        /// <summary>
        /// 批次唯一标识
        /// </summary>
        public string BatchId { get; set; }

        /// <summary>
        /// 批次开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 操作员（可选）
        /// </summary>
        public string Operator { get; set; }

        /// <summary>
        /// 备注（可选）
        /// </summary>
        public string Remark { get; set; }
    }
}
