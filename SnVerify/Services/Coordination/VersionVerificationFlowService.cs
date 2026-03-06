/// <author>AI Assistant</author>
/// <remarks>
/// 版本匹配检验流程服务实现。
/// </remarks>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 版本匹配检验流程服务
    /// </summary>
    [Obsolete("Replaced by VersionVerificationService in Phase3")]
    public class VersionVerificationFlowService : IVersionVerificationFlowService
    {
        private readonly IAdbAccessService _adbAccessService;
        private readonly IStorageService _storageService;
        private VerificationSnapshot _snapshot = VerificationSnapshot.Idle();

        /// <summary>
        /// 初始化
        /// </summary>
        public VersionVerificationFlowService(
            IAdbAccessService adbAccessService,
            IStorageService storageService)
        {
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        }

        /// <inheritdoc />
        public VerificationSnapshot Snapshot => _snapshot;

        /// <inheritdoc />
        public void ResetToIdle()
        {
            _snapshot = VerificationSnapshot.Idle();
        }

        /// <summary>
        /// Phase3：对三类版本字段执行强校验（Android / Board / ChargeBoard）。
        /// 
        /// 约束：
        /// - Parameter 为空时直接判定为配置缺失（PARAMETER_NOT_CONFIGURED）；
        /// - 对于每个非空 Expected 字段，都执行严格相等校验（忽略大小写与首尾空白）；
        /// - 任一字段不匹配立即返回 FAIL，并携带对应 FailReason 代码；
        /// - 所有已配置字段均匹配时返回 PASS。
        /// 
        /// 说明：该方法不负责 SN / ChipId / 订单唯一性与落库，仅聚焦版本强校验本身。
        /// </summary>
        /// <param name="deviceInfo">从 ADB 读取到的设备信息快照。</param>
        /// <param name="parameter">项目级版本期望配置。</param>
        /// <returns>
        /// (isPass, failReason) 元组；当 isPass 为 true 时 failReason 为 null。
        /// </returns>
        public (bool isPass, string failReason) VerifyVersion(DeviceInfo deviceInfo, VerificationParameter parameter)
        {
            if (parameter == null)
            {
                return (false, "PARAMETER_NOT_CONFIGURED");
            }

            var androidExpected = parameter.ExpectedAndroidVersion?.Trim();
            var boardExpected = parameter.ExpectedBoardVersion?.Trim();
            var chargeExpected = parameter.ExpectedChargeBoardVersion?.Trim();

            var androidActual = (deviceInfo?.AndroidVersion ?? string.Empty).Trim();
            var boardActual = (deviceInfo?.BoardVersion ?? string.Empty).Trim();
            var chargeActual = (deviceInfo?.ChargeBoardVersion ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(androidExpected) &&
                !string.Equals(androidExpected, androidActual, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "ANDROID_VERSION_MISMATCH");
            }

            if (!string.IsNullOrWhiteSpace(boardExpected) &&
                !string.Equals(boardExpected, boardActual, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "BOARD_VERSION_MISMATCH");
            }

            if (!string.IsNullOrWhiteSpace(chargeExpected) &&
                !string.Equals(chargeExpected, chargeActual, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "CHARGE_BOARD_VERSION_MISMATCH");
            }

            return (true, null);
        }

        /// <inheritdoc />
        public async Task<TestRecord> ExecuteVersionCheckAsync(TestSession session, CancellationToken cancellationToken = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (session.VerificationType != VerificationType.VersionMatch)
                throw new ArgumentException("Session.VerificationType must be VersionMatch", nameof(session));

            var expectedVersion = session.ExpectedVersion ?? string.Empty;
            var verifyTime = DateTime.Now;
            var sessionId = session.SessionName ?? string.Empty;

            _snapshot = VerificationSnapshot.Processing("--", sessionId);

            try
            {
                var adbResult = await _adbAccessService.ReadDeviceInfoAsync(cancellationToken);

                if (!adbResult.IsSuccess)
                {
                    var record = BuildRecord(session.Id, expectedVersion, null, "TIMEOUT", adbResult.ErrorMessage ?? "ADB read failed", verifyTime, adbResult.DeviceSn);
                    await _storageService.SaveTestRecordAsync(record);
                    _snapshot = VerificationSnapshot.Completed("--", record.Result, record.FailReason, sessionId, record.ActualVersion);
                    return record;
                }

                var actualVersion = adbResult.DeviceVersion ?? string.Empty;
                var (result, failReason) = string.Equals(expectedVersion.Trim(), actualVersion.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? ("PASS", (string)null)
                    : ("FAIL", $"版本号不匹配: 目标 {expectedVersion}, 实际 {actualVersion}");

                var finalRecord = BuildRecord(session.Id, expectedVersion, actualVersion, result, failReason, verifyTime, adbResult.DeviceSn);
                await _storageService.SaveTestRecordAsync(finalRecord);
                _snapshot = VerificationSnapshot.Completed("--", finalRecord.Result, finalRecord.FailReason, sessionId, finalRecord.ActualVersion);
                return finalRecord;
            }
            catch (Exception ex)
            {
                var record = BuildRecord(session.Id, expectedVersion, null, "TIMEOUT", ex.Message, verifyTime, deviceSn: null);
                await _storageService.SaveTestRecordAsync(record);
                _snapshot = VerificationSnapshot.Completed("--", record.Result, record.FailReason, sessionId, record.ActualVersion);
                return record;
            }
        }

        private static TestRecord BuildRecord(int sessionId, string expectedVersion, string actualVersion, string result, string failReason, DateTime verifyTime, string deviceSn = null)
        {
            return new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "-",
                DeviceSN = deviceSn ?? "-",
                ExpectedVersion = expectedVersion,
                ActualVersion = actualVersion,
                Result = result,
                FailReason = failReason,
                VerifyTime = verifyTime
            };
        }
    }
}
