/// <author>
/// AI Assistant
/// </author>

using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
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

        public VerificationFlowServiceFactory(IStorageService storageService, IAdbAccessService adbAccessService)
        {
            _storageService = storageService ?? throw new System.ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new System.ArgumentNullException(nameof(adbAccessService));
        }

        /// <inheritdoc />
        public IVerificationFlowService Create(string batchId)
        {
            var coordinator = new ProcessCoordinator(batchId, _storageService, _adbAccessService);
            return new VerificationFlowService(coordinator);
        }
    }
}
