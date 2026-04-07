/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：规则链执行器实现（工业级 Pipeline 骨架）。
/// 固定顺序执行规则链，失败立即终止，仅负责规则判断与 Fail Fast 执行，
/// 不负责任何持久化操作（落库由 ProcessCoordinator 统一调度）。
/// </remarks>

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;
using SnVerify.Services.Verification;

namespace SnVerify.Services.Rules
{
    /// <summary>
    /// 默认规则链执行器实现。
    /// </summary>
    public class RulePipelineExecutor : IRulePipelineExecutor
    {
        private readonly IStorageService _storageService;
        private readonly IDeviceAccessService _deviceAccessService;
        private readonly IVersionVerificationService _versionVerificationService;
        private readonly IFileLogger _logger;

        public RulePipelineExecutor(
            IStorageService storageService,
            IDeviceAccessService deviceAccessService,
            IVersionVerificationService versionVerificationService,
            IFileLogger logger = null)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _deviceAccessService = deviceAccessService ?? throw new ArgumentNullException(nameof(deviceAccessService));
            _versionVerificationService = versionVerificationService ?? throw new ArgumentNullException(nameof(versionVerificationService));
            _logger = logger;
        }

        public async Task<RuleExecutionResult> ExecuteAsync(
            ProductProfile profile,
            DeviceInfo deviceInfo,
            VerificationParameter parameter,
            string stickerSn,
            string orderId,
            string projectName)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(stickerSn)) throw new ArgumentException("stickerSn 不能为空", nameof(stickerSn));
            if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("orderId 不能为空", nameof(orderId));
            if (string.IsNullOrWhiteSpace(projectName)) throw new ArgumentException("projectName 不能为空", nameof(projectName));

            // ① Parameter 非空检查（最优先，Fail Fast，禁止访问 ADB）
            if (parameter == null)
            {
                return RuleExecutionResult.Fail(RuleFailReasonCodes.ParameterNotConfigured);
            }

            var sticker = stickerSn.Trim();
            
            // ② 设备信息读取（deviceInfo 可由外部预读；为空则由 IDeviceAccessService 按 profile.AdbConfig 读取）
            var di = deviceInfo;
            if (di == null)
            {
                try
                {
                    di = await _deviceAccessService.ReadDeviceInfoAsync(profile).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) when (ex.Message?.Contains("ADB 命令") == true || ex.Message?.Contains("ADB 命令未配置") == true)
                {
                    return RuleExecutionResult.Fail(RuleFailReasonCodes.AdbCommandEmpty, null);
                }
                catch (AggregateProtocolException ex)
                {
                    Debug.WriteLine($"[RulePipelineExecutor] ADB protocol invalid: {ex.Message}");
                    _logger?.LogWarning($"[RulePipelineExecutor] ADB protocol invalid: {ex.Message}");
                    return RuleExecutionResult.Fail(RuleFailReasonCodes.AdbProtocolInvalid, null);
                }
                catch (FormatException ex)
                {
                    Debug.WriteLine($"[RulePipelineExecutor] ADB protocol invalid: {ex.Message}");
                    _logger?.LogWarning($"[RulePipelineExecutor] ADB protocol invalid: {ex.Message}");
                    return RuleExecutionResult.Fail(RuleFailReasonCodes.AdbProtocolInvalid, null);
                }
            }

            if (di == null || string.IsNullOrWhiteSpace(di.DeviceSn))
            {
                const string failReason = RuleFailReasonCodes.AdbReadFail;
                return RuleExecutionResult.Fail(failReason, di);
            }

            if (!profile.EnableChipIdCheck)
            {
                di.ChipId = null;
            }

            var deviceSn = di.DeviceSn.Trim();

            // ③ StickerSN 与 DeviceSN 物理匹配
            if (!string.Equals(sticker, deviceSn, StringComparison.Ordinal))
            {
                const string failReason = RuleFailReasonCodes.SnNotMatch;
                return RuleExecutionResult.Fail(failReason, di);
            }

            // ④ SN 历史 PASS 检查（Phase3 批次维度：ProjectName + OrderName + StickerSN）
            // 此处的 ProjectName 为当前 Session 对应的项目个体名（Storage 中 Product.ProductName），由调用方提供。
            var snExists = await _storageService.IsStickerSnPassedInBatchAsync(projectName, orderId, sticker).ConfigureAwait(false);
            if (snExists)
            {
                const string failReason = RuleFailReasonCodes.SnDuplicate;
                return RuleExecutionResult.Fail(failReason, di);
            }

            if (profile.EnableChipIdCheck)
            {
                // ⑤ ChipId 格式检查（必须以 F50 开头）
                var chipIdValue = di.ChipId;
                if (string.IsNullOrWhiteSpace(chipIdValue) || !chipIdValue.StartsWith("F50", StringComparison.OrdinalIgnoreCase))
                {
                    const string failReason = RuleFailReasonCodes.ChipIdInvalid;
                    return RuleExecutionResult.Fail(failReason, di);
                }

                // ⑥ ChipId 批次唯一检查（Phase3：ProjectName + OrderName 维度，PASS 记录）
                var chipExists = await _storageService.IsChipIdPassedInBatchAsync(projectName, orderId, chipIdValue).ConfigureAwait(false);
                if (chipExists)
                {
                    const string failReason = RuleFailReasonCodes.ChipIdDuplicate;
                    return RuleExecutionResult.Fail(failReason, di);
                }
            }

            // ⑦ 三版本强校验（统一由 VersionVerificationService 负责；Board/Charge 是否比对由 Profile 开关决定）
            var (verOk, verFail) = await _versionVerificationService.VerifyAsync(di, parameter, profile).ConfigureAwait(false);
            if (!verOk)
            {
                return RuleExecutionResult.Fail(verFail, di);
            }

            return RuleExecutionResult.Pass(di);
        }
    }
}

