/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// Phase 2.5 Step 6：TestSession 模型，使用 INT 主键和业务可读 SessionName。
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 运行级会话模型。一次「开始 → 测试 → 停止」的独立会话；同订单可多 Session、可跨天。
    /// SessionName 建议为：OrderName_yyyyMMdd_HHmmss。
    /// </summary>
    public class TestSession
    {
        /// <summary>
        /// 会话主键，自增 Id。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 业务可读的会话名称，例如 OrderName_yyyyMMdd_HHmmss，要求唯一。
        /// </summary>
        public string SessionName { get; set; }

        /// <summary>
        /// 所属订单 Id（FK -> Order.Id）。
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// 开始时间。
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间，未结束则为 null。
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 当前状态（可选），例如：Pending / Running / Completed。
        /// </summary>
        public string Status { get; set; }
    }
}

