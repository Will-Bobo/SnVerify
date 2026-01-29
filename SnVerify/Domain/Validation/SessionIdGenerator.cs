/// <author>AI Assistant</author>
/// <remarks>
/// SessionId 规则。契约见 Phase2.5_Technical_Refactor_Checklist.md §1.2。
/// </remarks>

using System;

namespace SnVerify.Domain.Validation
{
    /// <summary>
    /// SessionId 生成规则：OrderId + "_" + yyyyMMdd_HHmmss。应用层保证同 Order 同秒不重复。
    /// </summary>
    public static class SessionIdGenerator
    {
        /// <summary>
        /// 生成 SessionId。
        /// </summary>
        /// <param name="orderId">订单 ID，不可为空。</param>
        /// <param name="at">会话开始时间，用于生成时间戳部分。</param>
        /// <returns>格式为 OrderId_yyyyMMdd_HHmmss 的字符串。</returns>
        public static string Format(string orderId, DateTime at)
        {
            if (orderId == null)
                throw new ArgumentNullException(nameof(orderId));
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));

            return orderId + "_" + at.ToString("yyyyMMdd_HHmmss");
        }
    }
}
