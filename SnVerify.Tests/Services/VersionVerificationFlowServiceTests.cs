/// <author>AI Assistant</author>
/// <remarks>
/// VersionVerificationFlowService 单元测试（TDD）。
/// </remarks>

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Models;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class VersionVerificationFlowServiceTests
    {
        private Mock<IAdbAccessService> _adbMock;
        private Mock<IStorageService> _storageMock;
        private VersionVerificationFlowService _service;

        private static TestSession CreateVersionMatchSession(string expectedVersion = "1.0.0", int id = 1)
        {
            return new TestSession
            {
                Id = id,
                SessionName = "Order1_20260126_143000",
                OrderId = 1,
                StartTime = DateTime.Now,
                VerificationType = VerificationType.VersionMatch,
                ExpectedVersion = expectedVersion
            };
        }

        [SetUp]
        public void SetUp()
        {
            _adbMock = new Mock<IAdbAccessService>();
            _storageMock = new Mock<IStorageService>();
            _storageMock.Setup(x => x.SaveTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);
            _service = new VersionVerificationFlowService(_adbMock.Object, _storageMock.Object);
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_WhenVersionMatch_ReturnsPass()
        {
            var session = CreateVersionMatchSession("1.0.0");
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbDeviceInfoResult.Success("SN001", "1.0.0"));

            var record = await _service.ExecuteVersionCheckAsync(session);

            Assert.That(record, Is.Not.Null);
            Assert.That(record.Result, Is.EqualTo("PASS"));
            Assert.That(record.FailReason, Is.Null);
            Assert.That(record.ExpectedVersion, Is.EqualTo("1.0.0"));
            Assert.That(record.ActualVersion, Is.EqualTo("1.0.0"));
            Assert.That(record.DeviceSN, Is.EqualTo("SN001"), "成功时应收存 ADB 读取的设备 SN");
            Assert.That(record.SessionId, Is.EqualTo(session.Id));
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_WhenVersionMismatch_ReturnsFail()
        {
            var session = CreateVersionMatchSession("1.0.0");
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbDeviceInfoResult.Success("SN001", "1.0.1"));

            var record = await _service.ExecuteVersionCheckAsync(session);

            Assert.That(record.Result, Is.EqualTo("FAIL"));
            Assert.That(record.FailReason, Is.EqualTo("版本号不匹配: 目标 1.0.0, 实际 1.0.1"));
            Assert.That(record.ExpectedVersion, Is.EqualTo("1.0.0"));
            Assert.That(record.ActualVersion, Is.EqualTo("1.0.1"));
            Assert.That(record.DeviceSN, Is.EqualTo("SN001"), "成功读取时应收存设备 SN");
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_WhenAdbFails_ReturnsTimeout()
        {
            var session = CreateVersionMatchSession("1.0.0");
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbDeviceInfoResult.Failure("ADB timeout"));

            var record = await _service.ExecuteVersionCheckAsync(session);

            Assert.That(record.Result, Is.EqualTo("TIMEOUT"));
            Assert.That(record.FailReason, Is.Not.Null.And.Contains("ADB"));
            Assert.That(record.DeviceSN, Is.EqualTo("-"), "ADB 失败时 DeviceSN 为占位符");
        }

        [Test]
        public void ExecuteVersionCheckAsync_WhenSessionNotVersionMatch_Throws()
        {
            var session = CreateVersionMatchSession("1.0.0");
            session.VerificationType = VerificationType.SnMatch;

            var ex = Assert.ThrowsAsync<ArgumentException>(() => _service.ExecuteVersionCheckAsync(session));
            Assert.That(ex.Message, Does.Contain("VersionMatch"));
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_VerifyTime_IsSet()
        {
            var before = DateTime.Now;
            var session = CreateVersionMatchSession("1.0.0");
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbDeviceInfoResult.Success("SN001", "1.0.0"));

            var record = await _service.ExecuteVersionCheckAsync(session);
            var after = DateTime.Now;

            Assert.That(record.VerifyTime, Is.GreaterThanOrEqualTo(before.AddSeconds(-1)));
            Assert.That(record.VerifyTime, Is.LessThanOrEqualTo(after.AddSeconds(1)));
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_SavesRecordToStorage()
        {
            var session = CreateVersionMatchSession("1.0.0");
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbDeviceInfoResult.Success("SN001", "1.0.0"));

            var record = await _service.ExecuteVersionCheckAsync(session);

            _storageMock.Verify(
                x => x.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                    r.SessionId == session.Id &&
                    r.ExpectedVersion == "1.0.0" &&
                    r.ActualVersion == "1.0.0" &&
                    r.DeviceSN == "SN001" &&
                    r.Result == "PASS")),
                Times.Once);
        }

        [Test]
        public void ExecuteVersionCheckAsync_WhenSessionNull_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => _service.ExecuteVersionCheckAsync(null));
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_WhenAdbThrows_ReturnsTimeoutWithExceptionMessage()
        {
            var session = CreateVersionMatchSession("1.0.0");
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("Device not responding"));

            var record = await _service.ExecuteVersionCheckAsync(session);

            Assert.That(record.Result, Is.EqualTo("TIMEOUT"));
            Assert.That(record.FailReason, Is.Not.Null.And.Contains("not responding"));
            Assert.That(record.DeviceSN, Is.EqualTo("-"), "异常时 DeviceSN 为占位符");
        }

        [Test]
        public async Task ExecuteVersionCheckAsync_WhenSessionExpectedVersionNull_TreatsAsEmpty()
        {
            var session = CreateVersionMatchSession("1.0.0");
            session.ExpectedVersion = null;
            _adbMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbDeviceInfoResult.Success("SN001", ""));

            var record = await _service.ExecuteVersionCheckAsync(session);

            Assert.That(record.Result, Is.EqualTo("PASS"));
            Assert.That(record.ExpectedVersion, Is.EqualTo(""));
            Assert.That(record.ActualVersion, Is.EqualTo(""));
            Assert.That(record.DeviceSN, Is.EqualTo("SN001"), "成功时应收存设备 SN");
        }

        [Test]
        public void VerifyVersion_WhenAllThreeMatch_ReturnsPass()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (isPass, failReason) = _service.VerifyVersion(device, parameter);

            Assert.That(isPass, Is.True);
            Assert.That(failReason, Is.Null);
        }

        [Test]
        public void VerifyVersion_WhenAndroidVersionMismatch_ReturnsFailWithAndroidCode()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "AX",
                BoardVersion = "B1",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (isPass, failReason) = _service.VerifyVersion(device, parameter);

            Assert.That(isPass, Is.False);
            Assert.That(failReason, Is.EqualTo("ANDROID_VERSION_MISMATCH"));
        }

        [Test]
        public void VerifyVersion_WhenBoardVersionMismatch_ReturnsFailWithBoardCode()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "BX",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (isPass, failReason) = _service.VerifyVersion(device, parameter);

            Assert.That(isPass, Is.False);
            Assert.That(failReason, Is.EqualTo("BOARD_VERSION_MISMATCH"));
        }

        [Test]
        public void VerifyVersion_WhenChargeBoardVersionMismatch_ReturnsFailWithChargeCode()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "CX"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (isPass, failReason) = _service.VerifyVersion(device, parameter);

            Assert.That(isPass, Is.False);
            Assert.That(failReason, Is.EqualTo("CHARGE_BOARD_VERSION_MISMATCH"));
        }

        [Test]
        public void VerifyVersion_WhenParameterNotConfigured_ReturnsFailWithParameterNotConfigured()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "C1"
            };

            var (isPass, failReason) = _service.VerifyVersion(device, null);

            Assert.That(isPass, Is.False);
            Assert.That(failReason, Is.EqualTo("PARAMETER_NOT_CONFIGURED"));
        }
    }
}
