/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 校验流程服务实现，对外提供统一接口给 UI / ViewModel
    /// </summary>
    public class VerificationFlowService : IVerificationFlowService
    {
        private readonly IProcessCoordinator _processCoordinator;
        private readonly IFileLogger _logger;

        /// <summary>
        /// 当前流程状态快照（只读）
        /// </summary>
        public VerificationSnapshot Snapshot => _processCoordinator.Snapshot;

        /// <summary>
        /// 初始化校验流程服务
        /// </summary>
        /// <param name="processCoordinator">流程编排服务</param>
        /// <param name="logger">日志记录器（可选，占位接口）</param>
        public VerificationFlowService(
            IProcessCoordinator processCoordinator,
            IFileLogger logger = null)
        {
            _processCoordinator = processCoordinator ?? throw new ArgumentNullException(nameof(processCoordinator));
            _logger = logger ?? new NullFileLogger();
        }

        /// <summary>
        /// 启动校验流程
        /// </summary>
        public async Task StartVerificationAsync(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            // 委托给 ProcessCoordinator
            await _processCoordinator.StartVerificationAsync(sn);
        }

        /// <summary>
        /// 重置流程状态，允许下一次扫描
        /// </summary>
        public void Reset()
        {
            // 委托给 ProcessCoordinator
            _processCoordinator.Reset();
        }
    }
}
