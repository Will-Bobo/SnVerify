/// <author>
/// AI Assistant
/// </author>

using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Infrastructure
{
    /// <summary>
    /// 校验流程服务工厂实现，按 SessionId 创建 ProcessCoordinator + VerificationFlowService（Phase 2.5：Batch 退场后以 SessionId 为入口）
    /// </summary>
    public class VerificationFlowServiceFactory : IVerificationFlowServiceFactory
    {
        private readonly IStorageService _storageService;
        private readonly IAdbAccessService _adbAccessService;
        private readonly ILoggingService _loggingService;

        public VerificationFlowServiceFactory(
            IStorageService storageService, 
            IAdbAccessService adbAccessService,
            ILoggingService loggingService = null)
        {
            _storageService = storageService ?? throw new System.ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new System.ArgumentNullException(nameof(adbAccessService));
            _loggingService = loggingService;
        }

        /// <inheritdoc />
        public IVerificationFlowService Create(string sessionId, string orderId = null)
        {
            var coordinator = new ProcessCoordinator(sessionId, _storageService, _adbAccessService, _loggingService, null, null, Services.Mes.Gate.MesMode.Disabled, orderId);
            return new VerificationFlowService(coordinator);
        }
    }
}
