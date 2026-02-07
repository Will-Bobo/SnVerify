/// <author>AI Assistant</author>
/// <remarks>
/// 版本匹配检验流程服务接口。
/// </remarks>

using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 版本匹配检验流程服务
    /// </summary>
    public interface IVersionVerificationFlowService
    {
        /// <summary>
        /// 当前校验快照（VersionMatch 模式下的事实来源，不含 UI 拼接）
        /// </summary>
        VerificationSnapshot Snapshot { get; }

        /// <summary>
        /// 重置快照为 Idle（结束测试时调用）
        /// </summary>
        void ResetToIdle();

        /// <summary>
        /// 执行版本检验：读取设备版本 → 与目标版本对比 → 生成并保存 TestRecord
        /// </summary>
        /// <param name="session">VersionMatch 类型 Session，必须含 ExpectedVersion</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>生成的 TestRecord</returns>
        Task<TestRecord> ExecuteVersionCheckAsync(TestSession session, CancellationToken cancellationToken = default);
    }
}
