/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：Session 生命周期状态快照，替代 BatchSnapshot 语义。Batch 退场后用此快照。
/// </remarks>

using System;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// Session 生命周期状态快照（不可变对象）
    /// </summary>
    public class SessionSnapshot
    {
        /// <summary>当前会话 ID（SessionId）</summary>
        public string SessionId { get; }

        /// <summary>当前订单 ID（OrderId）</summary>
        public string OrderId { get; }

        /// <summary>是否处于活动状态（已 Start 未 End）</summary>
        public bool IsActive { get; }

        /// <summary>错误消息（若有）</summary>
        public string ErrorMessage { get; }

        /// <summary>Session 开始时间</summary>
        public DateTime? StartTime { get; }

        /// <summary>Session 结束时间</summary>
        public DateTime? EndTime { get; }

        /// <summary>状态更新时间戳</summary>
        public DateTime Timestamp { get; }

        /// <summary>创建初始状态（无活动 Session）</summary>
        public static SessionSnapshot Idle()
        {
            return new SessionSnapshot(null, null, false, null, null, null, DateTime.Now);
        }

        /// <summary>创建活动 Session 状态</summary>
        public static SessionSnapshot Active(string sessionId, string orderId, DateTime startTime)
        {
            return new SessionSnapshot(sessionId, orderId, true, null, startTime, null, DateTime.Now);
        }

        /// <summary>创建已结束 Session 状态</summary>
        public static SessionSnapshot Ended(string sessionId, string orderId, DateTime startTime, DateTime endTime)
        {
            return new SessionSnapshot(sessionId, orderId, false, null, startTime, endTime, DateTime.Now);
        }

        /// <summary>创建错误状态</summary>
        public static SessionSnapshot Error(string errorMessage, string sessionId = null)
        {
            return new SessionSnapshot(sessionId, null, false, errorMessage, null, null, DateTime.Now);
        }

        private SessionSnapshot(string sessionId, string orderId, bool isActive, string errorMessage, DateTime? startTime, DateTime? endTime, DateTime timestamp)
        {
            SessionId = sessionId;
            OrderId = orderId;
            IsActive = isActive;
            ErrorMessage = errorMessage;
            StartTime = startTime;
            EndTime = endTime;
            Timestamp = timestamp;
        }
    }
}
