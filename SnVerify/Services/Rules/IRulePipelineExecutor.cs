/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：规则链执行器抽象。
/// ProcessCoordinator 仅做流程编排与状态更新，所有规则判断必须外移到该执行器内。
/// </remarks>

using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;

namespace SnVerify.Services.Rules
{
    /// <summary>
    /// 规则链执行器：执行固定顺序的校验 Pipeline。
    /// 注意：该执行器仅负责规则判断与 Fail Fast，不得执行持久化；落库由 ProcessCoordinator 统一调度。
    /// </summary>
    public interface IRulePipelineExecutor
    {
        /// <summary>
        /// 执行校验规则链（失败立即终止）。
        /// </summary>
        /// <param name="profile">产品 Profile（唯一规则入口）。</param>
        /// <param name="deviceInfo">可选：预读取的设备信息；为 null 时由执行器内部读取。</param>
        /// <param name="parameter">版本参数（Expected*）；为 null 时返回 PARAMETER_NOT_CONFIGURED。</param>
        /// <param name="stickerSn">扫码 SN（StickerSN）。</param>
        /// <param name="orderId">订单业务标识（OrderName）。</param>
        /// <param name="projectName">
        /// 批次项目名（ProjectName），用于 SN/ChipId 唯一性检查。
        /// 语义为当前 Session 对应的项目个体名（Storage 中 Product.ProductName），而非 ProductProfile 的展示名。
        /// </param>
        Task<RuleExecutionResult> ExecuteAsync(
            ProductProfile profile,
            DeviceInfo deviceInfo,
            VerificationParameter parameter,
            string stickerSn,
            string orderId,
            string projectName);
    }
}

