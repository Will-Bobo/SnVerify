/// <author>
/// AI Assistant
/// </author>

using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;
using SnVerify.Services.Parameter;
using SnVerify.Services.Verification;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Rules;
using SnVerify.Services.DeviceAccess;

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
        private readonly IParameterService _parameterService;
        private readonly IVersionVerificationService _versionVerificationService;
        private readonly IProductRegistry _productRegistry;
        private readonly IRulePipelineExecutor _rulePipelineExecutor;
        private readonly IDeviceAccessService _deviceAccessService;

        public VerificationFlowServiceFactory(
            IStorageService storageService, 
            IAdbAccessService adbAccessService,
            ILoggingService loggingService = null,
            IParameterService parameterService = null,
            IVersionVerificationService versionVerificationService = null,
            IProductRegistry productRegistry = null,
            IRulePipelineExecutor rulePipelineExecutor = null,
            IDeviceAccessService deviceAccessService = null)
        {
            _storageService = storageService ?? throw new System.ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new System.ArgumentNullException(nameof(adbAccessService));
            _loggingService = loggingService;
            _parameterService = parameterService;
            _versionVerificationService = versionVerificationService;
            _productRegistry = productRegistry;
            _rulePipelineExecutor = rulePipelineExecutor;
            _deviceAccessService = deviceAccessService;
        }

        /// <inheritdoc />
        public IVerificationFlowService Create(string sessionId, string orderId = null, string productCode = null)
        {
            var coordinator = new ProcessCoordinator(
                sessionId,
                _storageService,
                _adbAccessService,
                _loggingService,
                mesPreCheck: null,
                mesReporter: null,
                mesMode: Services.Mes.Gate.MesMode.Disabled,
                orderId: orderId,
                parameterService: _parameterService,
                versionVerificationService: _versionVerificationService,
                productRegistry: _productRegistry,
                deviceAccessService: _deviceAccessService,
                rulePipelineExecutor: _rulePipelineExecutor,
                sessionProductCode: productCode);
            return new VerificationFlowService(coordinator);
        }
    }
}
