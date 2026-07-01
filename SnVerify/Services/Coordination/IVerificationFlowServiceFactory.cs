/// <author>
/// AI Assistant
/// </author>

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 校验流程服务工厂，用于按 SessionId 创建 IVerificationFlowService（Phase 2.5：Batch 退场后以 SessionId 为入口）
    /// </summary>
    public interface IVerificationFlowServiceFactory
    {
        /// <summary>
        /// 创建指定 Session 的校验流程服务
        /// </summary>
        /// <param name="sessionId">会话 ID（SessionId）</param>
        /// <param name="orderId">订单 ID（可选，用于 MES 上下文）</param>
        /// <returns>校验流程服务实例</returns>
        IVerificationFlowService Create(string sessionId, string orderId = null, string productCode = null);
    }
}
