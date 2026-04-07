/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：RulePipelineExecutor 单元测试矩阵。
/// </remarks>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Logging;
using SnVerify.Services.Rules;
using SnVerify.Services.Storage;
using SnVerify.Services.Verification;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class RulePipelineExecutorTests
    {
        private const string SessionId = "ORDER001_20260305_120000";
        private const string OrderId = "ORDER001";

        private Mock<IStorageService> _storageMock;
        private Mock<IDeviceAccessService> _deviceAccessMock;
        private Mock<IVersionVerificationService> _versionMock;
        private Mock<IFileLogger> _loggerMock;
        private IRulePipelineExecutor _executor;

        private static ProductProfile CreatePhase3Profile()
        {
            return new ProductProfile
            {
                ProductCode = "KM001",
                ProductDisplayName = "KM001",
                Mode = VerificationMode.Phase3,
                AdbConfig = null,
                EnableChipIdCheck = true,
                EnableWifiMacCheck = true,
                EnableBoardVersionCheck = true,
                EnableChargeBoardVersionCheck = true
            };
        }

        private static VerificationParameter CreateParameter()
        {
            return new VerificationParameter
            {
                SessionId = 1,
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };
        }

        private static DeviceInfo CreateDeviceInfo(
            string deviceSn = "SN001",
            string chipId = "F501234",
            string android = "A1",
            string board = "B1",
            string charge = "C1")
        {
            return new DeviceInfo
            {
                DeviceSn = deviceSn,
                ChipId = chipId,
                AndroidVersion = android,
                BoardVersion = board,
                ChargeBoardVersion = charge
            };
        }

        [SetUp]
        public void SetUp()
        {
            _storageMock = new Mock<IStorageService>();
            _deviceAccessMock = new Mock<IDeviceAccessService>();
            _versionMock = new Mock<IVersionVerificationService>();
            _loggerMock = new Mock<IFileLogger>();

            _storageMock
                .Setup(s => s.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            _storageMock
                .Setup(s => s.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            _versionMock
                .Setup(v => v.VerifyAsync(It.IsAny<DeviceInfo>(), It.IsAny<VerificationParameter>(), It.IsAny<ProductProfile>(), default))
                .ReturnsAsync((true, (string)null));

            _executor = new RulePipelineExecutor(_storageMock.Object, _deviceAccessMock.Object, _versionMock.Object, _loggerMock.Object);
        }

        [Test]
        public async Task ExecuteAsync_WhenStickerSnAlreadyPassedInOrder_ShouldFailFast()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();
            var projectName = "P_PHASE3";

            // Frozen Pipeline: ①Parameter → ②ADB → ③SN匹配 → ④SN历史PASS
            // 因此此用例必须提供可用的 ADB 读取结果，否则会提前在 ② 失败为 ADB_READ_FAIL。
            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            _storageMock
                .Setup(s => s.IsStickerSnPassedInBatchAsync(projectName, OrderId, "SN001"))
                .ReturnsAsync(true);

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, projectName);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.SnDuplicate));

            // 按 Stage3 Frozen Pipeline：Parameter → ADB → SN 匹配 → SN 历史 PASS，
            // 即使 SN 已在订单内 PASS，仍会先读取设备信息并做物理 SN 匹配。
            _deviceAccessMock.Verify(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Once);
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenAdbReadFails_ShouldFailWithAdbReadFail()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync((DeviceInfo)null);

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, "P1");

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.AdbReadFail));
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenAdbProtocolInvalid_ShouldFailWithAdbProtocolInvalid()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ThrowsAsync(new AggregateProtocolException("聚合协议错误：第二行字段不足6列"));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, "P1");

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.AdbProtocolInvalid));
            _loggerMock.Verify(
                x => x.LogWarning(It.Is<string>(msg => msg.Contains("ADB protocol invalid"))),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_WhenChipIdInvalid_ShouldFailWithChipIdInvalid()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "X123"));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, "P1");

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.ChipIdInvalid));

            _storageMock.Verify(s => s.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenChipIdDuplicateInOrder_ShouldFailWithChipIdDuplicate()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            var projectName = "P_PHASE3";
            _storageMock
                .Setup(s => s.IsChipIdPassedInBatchAsync(projectName, OrderId, "F501234"))
                .ReturnsAsync(true);

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, projectName);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.ChipIdDuplicate));
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenVersionMismatch_ShouldFailWithVersionFailReason()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            _versionMock
                .Setup(v => v.VerifyAsync(It.IsAny<DeviceInfo>(), It.IsAny<VerificationParameter>(), It.IsAny<ProductProfile>(), default))
                .ReturnsAsync((false, RuleFailReasonCodes.AndroidVersionMismatch));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, "P1");

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.AndroidVersionMismatch));
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenAllPass_ShouldReturnPass()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _deviceAccessMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId, "P1");

            Assert.That(result.Result, Is.EqualTo("PASS"));
            Assert.That(result.FailReason, Is.Null);
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
            _versionMock.Verify(v => v.VerifyAsync(It.IsAny<DeviceInfo>(), It.IsAny<VerificationParameter>(), It.IsAny<ProductProfile>(), default), Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_WhenParameterNotConfigured_ShouldFailAndSkipAdb()
        {
            var profile = CreatePhase3Profile();

            var result = await _executor.ExecuteAsync(profile, null, parameter: null, stickerSn: "SN001", orderId: OrderId, projectName: "P1");

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo(RuleFailReasonCodes.ParameterNotConfigured));

            _deviceAccessMock.Verify(a => a.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Never);
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
        }

        private static ProductProfile CreateKm008Profile()
        {
            return new ProductProfile
            {
                ProductCode = "KM008",
                Mode = VerificationMode.Phase3,
                EnableChipIdCheck = false,
                EnableWifiMacCheck = true,
                EnableBoardVersionCheck = false,
                EnableChargeBoardVersionCheck = false
            };
        }

        private static VerificationParameter CreateAndroidOnlyParameter()
        {
            return new VerificationParameter
            {
                SessionId = 1,
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = null,
                ExpectedChargeBoardVersion = null
            };
        }

        [Test]
        public async Task ExecuteAsync_WhenChipIdCheckDisabled_ShouldClearChip_AndPass_WhenAndroidMatches()
        {
            var profile = CreateKm008Profile();
            var parameter = CreateAndroidOnlyParameter();
            var di = CreateDeviceInfo(deviceSn: "SN001", chipId: "BADNOTF50", android: "A1", board: "", charge: "");

            var result = await _executor.ExecuteAsync(profile, di, parameter, "SN001", OrderId, "P1");

            Assert.That(result.Result, Is.EqualTo("PASS"));
            Assert.That(result.DeviceInfo, Is.Not.Null);
            Assert.That(result.DeviceInfo.ChipId, Is.Null);
            _storageMock.Verify(s => s.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenChipIdCheckDisabled_ShouldNotInvokeChipDuplicateCheck_EvenWhenChipWouldDuplicate()
        {
            var profile = CreateKm008Profile();
            var parameter = CreateAndroidOnlyParameter();
            var di = CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234", android: "A1");

            _storageMock
                .Setup(s => s.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), "F501234"))
                .ReturnsAsync(true);

            var result = await _executor.ExecuteAsync(profile, di, parameter, "SN001", OrderId, "P_KM008");

            Assert.That(result.Result, Is.EqualTo("PASS"));
            Assert.That(result.DeviceInfo.ChipId, Is.Null);
            _storageMock.Verify(s => s.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// KM008：Profile 关闭 Board/Charge 校验时，parameter 中残留的期望不得触发版本失败（真实 VersionVerificationService）。
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Km008_StaleBoardChargeExpected_ShouldPass_WithRealVersionVerificationService()
        {
            var realVersion = new VersionVerificationService();
            var executor = new RulePipelineExecutor(_storageMock.Object, _deviceAccessMock.Object, realVersion, _loggerMock.Object);

            var profile = CreateKm008Profile();
            var parameter = new VerificationParameter
            {
                SessionId = 1,
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "STALE_BOARD",
                ExpectedChargeBoardVersion = "STALE_CHARGE"
            };
            var di = CreateDeviceInfo(deviceSn: "SN001", chipId: "X", android: "A1", board: "", charge: "");

            var result = await executor.ExecuteAsync(profile, di, parameter, "SN001", OrderId, "P_KM008");

            Assert.That(result.Result, Is.EqualTo("PASS"));
            Assert.That(result.FailReason, Is.Null);
        }
    }
}

