/// <author>
/// AI Assistant
/// </author>

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Adb;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// AdbAccessService 设备信息读取（临时调试接口）单元测试
    /// </summary>
    [TestFixture]
    public class AdbAccessServiceDeviceInfoTests
    {
        private const string TestAdbPath = @"tools\adb\adb.exe";
        private const string TestSn = "TEST_SN_123456";
        private const string TestVersion = "TEST_VER_1.0.0";

        private Mock<IProcessRunner> _processRunnerMock;
        private AdbAccessService _service;

        [SetUp]
        public void SetUp()
        {
            _processRunnerMock = new Mock<IProcessRunner>();
            _service = new AdbAccessService(TestAdbPath, _processRunnerMock.Object);
        }

        [Test]
        public async Task ReadDeviceInfoAsync_ShouldReturnSnAndVersion_WhenAllCommandsSucceed()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(string.Empty));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop ro.build.display.id",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestVersion));

            // Act
            var result = await _service.ReadDeviceInfoAsync();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.DeviceSn, Is.EqualTo(TestSn));
            Assert.That(result.DeviceVersion, Is.EqualTo(TestVersion));
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public async Task ReadDeviceInfoAsync_ShouldSucceed_WhenYlzeroFailsButSnAndVersionSucceed()
        {
            // Arrange - ylzero 失败但允许继续
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("ylzero failed"));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop ro.build.display.id",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestVersion));

            // Act
            var result = await _service.ReadDeviceInfoAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.DeviceSn, Is.EqualTo(TestSn));
            Assert.That(result.DeviceVersion, Is.EqualTo(TestVersion));
        }

        [Test]
        public async Task ReadDeviceInfoAsync_ShouldFail_WhenSnReadFails()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(string.Empty));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("SN read failed"));

            // Version 命令不应被调用

            // Act
            var result = await _service.ReadDeviceInfoAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.DeviceSn, Is.Null);
            Assert.That(result.DeviceVersion, Is.Null);
            Assert.That(result.ErrorMessage, Is.Not.Null.And.Contains("SN"));

            _processRunnerMock.Verify(
                x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop ro.build.display.id",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ReadDeviceInfoAsync_ShouldSucceed_WhenVersionReadFailsButSnSucceeds()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(string.Empty));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop ro.build.display.id",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("version read failed"));

            // Act
            var result = await _service.ReadDeviceInfoAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.DeviceSn, Is.EqualTo(TestSn));
            Assert.That(result.DeviceVersion, Is.Null.Or.Empty);
            Assert.That(result.ErrorMessage, Is.Null.Or.Empty);
        }
    }
}

