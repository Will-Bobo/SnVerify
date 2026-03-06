/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：RulePipelineExecutor 单元测试矩阵。
/// </remarks>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Adb;
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
        private Mock<IAdbAccessService> _adbMock;
        private Mock<IVersionVerificationService> _versionMock;
        private IRulePipelineExecutor _executor;

        private static ProductProfile CreatePhase3Profile()
        {
            return new ProductProfile
            {
                ProductCode = "KM001",
                ProductName = "KM001",
                Mode = VerificationMode.Phase3,
                AdbCommands = new DeviceInfoCommandSet(),
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
                ProjectId = "KM001",
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
            _adbMock = new Mock<IAdbAccessService>();
            _versionMock = new Mock<IVersionVerificationService>();

            _storageMock
                .Setup(s => s.GetInternalSessionIdBySessionNameAsync(SessionId))
                .ReturnsAsync(10);

            _storageMock
                .Setup(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Returns(Task.CompletedTask);

            _storageMock
                .Setup(s => s.IsStickerSnPassedInOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            _storageMock
                .Setup(s => s.IsChipIdPassedInOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            _versionMock
                .Setup(v => v.VerifyAsync(It.IsAny<DeviceInfo>(), It.IsAny<VerificationParameter>(), default))
                .ReturnsAsync((true, (string)null));

            _executor = new RulePipelineExecutor(SessionId, _storageMock.Object, _adbMock.Object, _versionMock.Object);
        }

        [Test]
        public async Task ExecuteAsync_WhenStickerSnAlreadyPassedInOrder_ShouldFailFast()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _storageMock
                .Setup(s => s.IsStickerSnPassedInOrderAsync(OrderId, "SN001"))
                .ReturnsAsync(true);

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo("SN_DUPLICATE"));

            _adbMock.Verify(a => a.ReadDeviceInfoAsync(It.IsAny<ProjectProfile>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenAdbReadFails_ShouldFailWithAdbReadFail()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _adbMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProjectProfile>()))
                .ReturnsAsync((DeviceInfo)null);

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo("ADB_READ_FAIL"));
        }

        [Test]
        public async Task ExecuteAsync_WhenChipIdInvalid_ShouldFailWithChipIdInvalid()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _adbMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProjectProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "X123"));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo("CHIPID_INVALID"));

            _storageMock.Verify(s => s.IsChipIdPassedInOrderAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_WhenChipIdDuplicateInOrder_ShouldFailWithChipIdDuplicate()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _adbMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProjectProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            _storageMock
                .Setup(s => s.IsChipIdPassedInOrderAsync(OrderId, "F501234"))
                .ReturnsAsync(true);

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo("CHIPID_DUPLICATE"));
        }

        [Test]
        public async Task ExecuteAsync_WhenVersionMismatch_ShouldFailWithVersionFailReason()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _adbMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProjectProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            _versionMock
                .Setup(v => v.VerifyAsync(It.IsAny<DeviceInfo>(), It.IsAny<VerificationParameter>(), default))
                .ReturnsAsync((false, "ANDROID_VERSION_MISMATCH"));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId);

            Assert.That(result.Result, Is.EqualTo("FAIL"));
            Assert.That(result.FailReason, Is.EqualTo("ANDROID_VERSION_MISMATCH"));
        }

        [Test]
        public async Task ExecuteAsync_WhenAllPass_ShouldReturnPass()
        {
            var profile = CreatePhase3Profile();
            var parameter = CreateParameter();

            _adbMock
                .Setup(a => a.ReadDeviceInfoAsync(It.IsAny<ProjectProfile>()))
                .ReturnsAsync(CreateDeviceInfo(deviceSn: "SN001", chipId: "F501234"));

            var result = await _executor.ExecuteAsync(profile, null, parameter, "SN001", OrderId);

            Assert.That(result.Result, Is.EqualTo("PASS"));
            Assert.That(result.FailReason, Is.Null);

            _storageMock.Verify(s => s.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                r.Result == "PASS" &&
                r.FailReason == null &&
                r.StickerSN == "SN001")), Times.Once);
        }
    }
}

