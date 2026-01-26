/// <author>
/// AI Assistant
/// </author>

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 校验流程服务工厂，用于按批次 ID 创建 IVerificationFlowService（UI 辅助，不修改 Service 行为）
    /// </summary>
    public interface IVerificationFlowServiceFactory
    {
        /// <summary>
        /// 创建指定批次的校验流程服务
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <returns>校验流程服务实例</returns>
        IVerificationFlowService Create(string batchId);
    }
}
