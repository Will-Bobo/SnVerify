/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 4：MES 事件参数（仅通知用途，不得影响本站 PASS/FAIL）。
/// </remarks>

using System;

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 事件参数。用于将“非业务失败”的 MES 健康态信息通知到 UI（弱提示）与日志。
    /// </summary>
    public sealed class MesEventArgs : EventArgs
    {
        /// <summary>事件类型</summary>
        public MesEventType EventType { get; }

        /// <summary>事件消息（用于日志/状态栏）</summary>
        public string Message { get; }

        /// <summary>关联 SessionId（可选）</summary>
        public string SessionId { get; }

        /// <summary>关联 OrderId（可选）</summary>
        public string OrderId { get; }

        public MesEventArgs(MesEventType eventType, string message, string sessionId = null, string orderId = null)
        {
            EventType = eventType;
            Message = message ?? "";
            SessionId = sessionId;
            OrderId = orderId;
        }
    }
}

