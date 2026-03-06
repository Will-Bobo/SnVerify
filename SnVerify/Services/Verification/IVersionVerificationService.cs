/// <author>AI Assistant</author>
/// <remarks>
/// Phase3：三版本强校验服务接口。
/// 负责对 Android / Board / ChargeBoard 版本字段进行统一的规则校验，
/// 不承担流程编排与持久化职责。
/// </remarks>

using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;

namespace SnVerify.Services.Verification
{
    /// <summary>
    /// 三版本强校验服务。
    /// </summary>
    public interface IVersionVerificationService
    {
        /// <summary>
        /// 执行三版本强校验：
        /// AndroidVersion → BoardVersion → ChargeBoardVersion，任一失败即终止。
        /// </summary>
        /// <param name="deviceInfo">从 ADB 读取到的设备信息快照。</param>
        /// <param name="parameter">项目级版本期望配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>
        /// (success, failReason) 元组；success 为 true 时 failReason 必须为 null。
        /// </returns>
        Task<(bool success, string failReason)> VerifyAsync(
            DeviceInfo deviceInfo,
            VerificationParameter parameter,
            CancellationToken cancellationToken = default);
    }
}

