/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：规则链执行器实现（工业级 Pipeline 骨架）。
/// 固定顺序执行规则链，失败立即终止，仅负责规则判断与 Fail Fast 执行，
/// 不负责任何持久化操作（落库由 ProcessCoordinator 统一调度）。
/// </remarks>

using System;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Adb;
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
        private readonly IAdbAccessService _adbAccessService;
        private readonly IVersionVerificationService _versionVerificationService;

        public RulePipelineExecutor(
            IStorageService storageService,
            IAdbAccessService adbAccessService,
            IVersionVerificationService versionVerificationService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
            _versionVerificationService = versionVerificationService ?? throw new ArgumentNullException(nameof(versionVerificationService));
        }

        public async Task<RuleExecutionResult> ExecuteAsync(
            ProductProfile profile,
            DeviceInfo deviceInfo,
            VerificationParameter parameter,
            string stickerSn,
            string orderId)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(stickerSn)) throw new ArgumentException("stickerSn 不能为空", nameof(stickerSn));
            if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("orderId 不能为空", nameof(orderId));

            // ① Parameter 非空检查（最优先，Fail Fast，禁止访问 ADB）
            if (parameter == null)
            {
                return RuleExecutionResult.Fail("PARAMETER_NOT_CONFIGURED");
            }

            var sticker = stickerSn.Trim();
            
            // ② ADB 读取设备信息（deviceInfo 可由外部预读；为空则由执行器内部读取）
            var di = deviceInfo;
            if (di == null)
            {
                var projectProfile = new ProjectProfile
                {
                    ProjectId = profile.ProductCode,
                    AggregateDeviceInfoCommand = null
                };

                di = await _adbAccessService.ReadDeviceInfoAsync(projectProfile).ConfigureAwait(false);
            }

            if (di == null || string.IsNullOrWhiteSpace(di.DeviceSn))
            {
                const string failReason = "ADB_READ_FAIL";
                return RuleExecutionResult.Fail(failReason, di);
            }

            var deviceSn = di.DeviceSn.Trim();

            // ③ StickerSN 与 DeviceSN 物理匹配
            if (!string.Equals(sticker, deviceSn, StringComparison.Ordinal))
            {
                const string failReason = "SN_NOT_MATCH";
                return RuleExecutionResult.Fail(failReason, di);
            }

            // ④ SN 历史 PASS 检查（Order 维度）
            var snExists = await _storageService.IsStickerSnPassedInOrderAsync(orderId, sticker).ConfigureAwait(false);
            if (snExists)
            {
                const string failReason = "SN_DUPLICATE";
                return RuleExecutionResult.Fail(failReason, di);
            }

            // ⑤ ChipId 格式检查（必须以 F50 开头）
            var chipIdValue = di.ChipId;
            if (string.IsNullOrWhiteSpace(chipIdValue) || !chipIdValue.StartsWith("F50", StringComparison.OrdinalIgnoreCase))
            {
                const string failReason = "CHIPID_INVALID";
                return RuleExecutionResult.Fail(failReason, di);
            }

            // ⑥ ChipId 订单唯一检查（PASS 记录）
            var chipExists = await _storageService.IsChipIdPassedInOrderAsync(orderId, chipIdValue).ConfigureAwait(false);
            if (chipExists)
            {
                const string failReason = "CHIPID_DUPLICATE";
                return RuleExecutionResult.Fail(failReason, di);
            }

            // ⑦ 三版本强校验（统一由 VersionVerificationService 负责）
            var (verOk, verFail) = await _versionVerificationService.VerifyAsync(di, parameter).ConfigureAwait(false);
            if (!verOk)
            {
                return RuleExecutionResult.Fail(verFail, di);
            }

            return RuleExecutionResult.Pass(di);
        }
    }
}

