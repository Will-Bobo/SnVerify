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
    /// 校验流程服务工厂实现，按批次 ID 创建 ProcessCoordinator + VerificationFlowService（仅 Infrastructure，不修改 Service 行为）
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
        public IVerificationFlowService Create(string batchId)
        {
            var coordinator = new ProcessCoordinator(batchId, _storageService, _adbAccessService, _loggingService);
            return new VerificationFlowService(coordinator);
        }
    }
}
