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
                    var record = BuildRecord(session.Id, expectedVersion, null, "TIMEOUT", adbResult.ErrorMessage ?? "ADB read failed", verifyTime);
                    await _storageService.SaveTestRecordAsync(record);
                    _snapshot = VerificationSnapshot.Completed("--", record.Result, record.FailReason, sessionId, record.ActualVersion);
                    return record;
                }

                var actualVersion = adbResult.DeviceVersion ?? string.Empty;
                var (result, failReason) = string.Equals(expectedVersion.Trim(), actualVersion.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? ("PASS", (string)null)
                    : ("FAIL", $"Version mismatch: expected {expectedVersion}, actual {actualVersion}");

                var finalRecord = BuildRecord(session.Id, expectedVersion, actualVersion, result, failReason, verifyTime);
                await _storageService.SaveTestRecordAsync(finalRecord);
                _snapshot = VerificationSnapshot.Completed("--", finalRecord.Result, finalRecord.FailReason, sessionId, finalRecord.ActualVersion);
                return finalRecord;
            }
            catch (Exception ex)
            {
                var record = BuildRecord(session.Id, expectedVersion, null, "TIMEOUT", ex.Message, verifyTime);
                await _storageService.SaveTestRecordAsync(record);
                _snapshot = VerificationSnapshot.Completed("--", record.Result, record.FailReason, sessionId, record.ActualVersion);
                return record;
            }
        }

        private static TestRecord BuildRecord(int sessionId, string expectedVersion, string actualVersion, string result, string failReason, DateTime verifyTime)
        {
            return new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "-",
                DeviceSN = "-",
                ExpectedVersion = expectedVersion,
                ActualVersion = actualVersion,
                Result = result,
                FailReason = failReason,
                VerifyTime = verifyTime
            };
        }
    }
}
