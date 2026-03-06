/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：规则链执行器实现（工业级 Pipeline 骨架）。
/// 固定顺序执行规则链，失败立即终止，并在内部完成 TestRecord 落库。
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
        private readonly string _sessionId;
        private readonly IStorageService _storageService;
        private readonly IAdbAccessService _adbAccessService;
        private readonly IVersionVerificationService _versionVerificationService;

        public RulePipelineExecutor(
            string sessionId,
            IStorageService storageService,
            IAdbAccessService adbAccessService,
            IVersionVerificationService versionVerificationService)
        {
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
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

            // 参数缺失：按既有 Phase3 行为 FailFast（不访问 ADB）。
            if (parameter == null)
            {
                await SaveResultAsync(stickerSn.Trim(), "FAIL", "PARAMETER_NOT_CONFIGURED", deviceInfo: null, parameter: null)
                    .ConfigureAwait(false);
                return RuleExecutionResult.Fail("PARAMETER_NOT_CONFIGURED");
            }

            var sticker = stickerSn.Trim();

            // Step1：SN 历史 PASS 检查（订单维度）
            var snExists = await _storageService.IsStickerSnPassedInOrderAsync(orderId, sticker).ConfigureAwait(false);
            if (snExists)
            {
                const string failReason = "SN_DUPLICATE";
                await SaveResultAsync(sticker, "FAIL", failReason, deviceInfo: null, parameter).ConfigureAwait(false);
                return RuleExecutionResult.Fail(failReason);
            }

            // Step2：ADB 读取校验（deviceInfo 可由外部预读；为空则由执行器内部读取）
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
                await SaveResultAsync(sticker, "FAIL", failReason, di, parameter).ConfigureAwait(false);
                return RuleExecutionResult.Fail(failReason, di);
            }

            var deviceSn = di.DeviceSn.Trim();

            // Step3：SN 匹配
            if (!string.Equals(sticker, deviceSn, StringComparison.Ordinal))
            {
                const string failReason = "SN_NOT_MATCH";
                await SaveResultAsync(sticker, "FAIL", failReason, di, parameter).ConfigureAwait(false);
                return RuleExecutionResult.Fail(failReason, di);
            }

            // Step4：ChipId 格式校验（按产品开关决定是否启用）
            if (profile.EnableChipIdCheck)
            {
                var chipId = di.ChipId;
                if (string.IsNullOrWhiteSpace(chipId) || !chipId.StartsWith("F50", StringComparison.OrdinalIgnoreCase))
                {
                    const string failReason = "CHIPID_INVALID";
                    await SaveResultAsync(sticker, "FAIL", failReason, di, parameter).ConfigureAwait(false);
                    return RuleExecutionResult.Fail(failReason, di);
                }

                // Step5：ChipId 订单唯一性（PASS 记录）
                var chipExists = await _storageService.IsChipIdPassedInOrderAsync(orderId, chipId).ConfigureAwait(false);
                if (chipExists)
                {
                    const string failReason = "CHIPID_DUPLICATE";
                    await SaveResultAsync(sticker, "FAIL", failReason, di, parameter).ConfigureAwait(false);
                    return RuleExecutionResult.Fail(failReason, di);
                }
            }

            // Step6：三版本强校验（统一由 VersionVerificationService 负责）
            var (verOk, verFail) = await _versionVerificationService.VerifyAsync(di, parameter).ConfigureAwait(false);
            if (!verOk)
            {
                await SaveResultAsync(sticker, "FAIL", verFail, di, parameter).ConfigureAwait(false);
                return RuleExecutionResult.Fail(verFail, di);
            }

            // Step7：写 TestRecord（PASS）
            await SaveResultAsync(sticker, "PASS", null, di, parameter).ConfigureAwait(false);
            return RuleExecutionResult.Pass(di);
        }

        private async Task SaveResultAsync(string stickerSn, string result, string failReason, DeviceInfo deviceInfo, VerificationParameter parameter)
        {
            var internalSessionId = await _storageService.GetInternalSessionIdBySessionNameAsync(_sessionId).ConfigureAwait(false);
            if (!internalSessionId.HasValue)
            {
                return;
            }

            var record = new TestRecord
            {
                SessionId = internalSessionId.Value,
                StickerSN = stickerSn,
                DeviceSN = deviceInfo?.DeviceSn,
                WifiMac = deviceInfo?.WifiMac,
                ChipId = deviceInfo?.ChipId,
                BoardVersion = deviceInfo?.BoardVersion,
                ChargeBoardVersion = deviceInfo?.ChargeBoardVersion,
                ExpectedVersion = parameter?.ExpectedAndroidVersion,
                ActualVersion = deviceInfo?.AndroidVersion,
                Result = result,
                FailReason = failReason,
                VerifyTime = DateTime.Now
            };

            await _storageService.SaveTestRecordAsync(record).ConfigureAwait(false);
        }
    }
}

