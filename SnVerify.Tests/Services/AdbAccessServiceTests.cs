/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Adb;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// AdbAccessService 单元测试
    /// </summary>
    [TestFixture]
    public class AdbAccessServiceTests
    {
        private Mock<IProcessRunner> _processRunnerMock;
        private IAdbAccessService _adbAccessService;
        private const string TestAdbPath = @"tools\adb\adb.exe";
        private const string TestSn = "TEST123456789";

        [SetUp]
        public void SetUp()
        {
            _processRunnerMock = new Mock<IProcessRunner>();
            _adbAccessService = new AdbAccessService(TestAdbPath, _processRunnerMock.Object);
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnSuccess_WhenBothCommandsSucceed()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Sn, Is.EqualTo(TestSn));
            Assert.That(result.IsTimeout, Is.False);
            Assert.That(result.ErrorReason, Is.Null);

            // 验证命令执行顺序
            _processRunnerMock.Verify(
                x => x.RunAsync(TestAdbPath, "shell ylzero", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _processRunnerMock.Verify(
                x => x.RunAsync(TestAdbPath, "shell getprop sys.skyroam.osi.sn", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnFailure_WhenYlzeroCommandFails()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("Permission denied"));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Sn, Is.Null);
            Assert.That(result.ErrorReason, Is.Not.Null.And.Contains("ylzero"));
            Assert.That(result.IsTimeout, Is.False);

            // 验证未执行 SN 读取命令
            _processRunnerMock.Verify(
                x => x.RunAsync(TestAdbPath, "shell getprop sys.skyroam.osi.sn", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnFailure_WhenSnIsEmpty()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Sn, Is.Null);
            Assert.That(result.ErrorReason, Is.Not.Null.And.Contains("empty"));
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnFailure_WhenSnIsWhitespace()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success("   \t\n  "));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorReason, Is.Not.Null.And.Contains("empty"));
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldRetry_WhenFirstAttemptFails()
        {
            // Arrange
            var attemptCount = 0;
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount == 1)
                    {
                        return Task.FromResult(ProcessExecutionResult.Failure("First attempt failed"));
                    }
                    return Task.FromResult(ProcessExecutionResult.Success(""));
                });

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Sn, Is.EqualTo(TestSn));
            Assert.That(attemptCount, Is.EqualTo(2));
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldRetryUpToThreeTimes_WhenAllAttemptsFail()
        {
            // Arrange
            var attemptCount = 0;
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    attemptCount++;
                    return Task.FromResult(ProcessExecutionResult.Failure($"Attempt {attemptCount} failed"));
                });

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(attemptCount, Is.EqualTo(3));
            Assert.That(result.ErrorReason, Is.Not.Null);
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnTimeout_WhenProcessTimesOut()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Timeout());

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsTimeout, Is.True);
            Assert.That(result.ErrorReason, Is.Not.Null);
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                _processRunnerMock
                    .Setup(x => x.RunAsync(
                        TestAdbPath,
                        "shell ylzero",
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new OperationCanceledException());

                // Act
                var result = await _adbAccessService.ReadDeviceSnAsync(cts.Token);

                // Assert - 验证返回失败结果而不是抛出异常
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorReason, Is.Not.Null);
            }
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnFailure_WhenSnReadCommandFails()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("Property not found"));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorReason, Is.Not.Null);
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldTrimSnOutput()
        {
            // Arrange
            var snWithWhitespace = $"  {TestSn}  \n";
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(snWithWhitespace));

            // Act
            var result = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Sn, Is.EqualTo(TestSn));
        }
    }
}
