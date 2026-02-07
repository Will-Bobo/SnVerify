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

        private void SetupShellExitWarmup()
        {
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell exit",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(string.Empty));
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnSuccess_WhenBothCommandsSucceed()
        {
            // Arrange
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
            // ylzero 命令失败，ExitCode = 127（命令不存在，debug 版机器会出现，可继续 SN 读取）
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "shell ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("Command not found", null, 127));
            
            // SN 读取命令也失败（这样才能测试完整的失败流程）
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
            Assert.That(result.Sn, Is.Null);
            // ExitCode == 127 时，ylzero 失败不会导致返回错误，会继续执行 SN 读取命令
            // 实际返回的是 SN 读取命令失败的错误
            Assert.That(result.ErrorReason, Is.Not.Null);
            Assert.That(result.IsTimeout, Is.False);

            // 验证执行了 SN 读取命令（即使 ylzero 失败但 ExitCode == 127）
            _processRunnerMock.Verify(
                x => x.RunAsync(TestAdbPath, "shell getprop sys.skyroam.osi.sn", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Exactly(3)); // MaxRetries = 3
        }

        [Test]
        public async Task ReadDeviceSnAsync_ShouldReturnFailure_WhenSnIsEmpty()
        {
            // Arrange
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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
            SetupShellExitWarmup();
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

        [Test]
        public async Task EnsureAdbShellWarmedUpAsync_ShouldExecuteOnlyOnce_WhenReadDeviceSnAsyncCalledMultipleTimes()
        {
            // Arrange: 多次调用 ReadDeviceSnAsync 应只执行一次 shell exit 预热
            SetupShellExitWarmup();
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

            // Act: 连续调用 3 次
            var r1 = await _adbAccessService.ReadDeviceSnAsync();
            var r2 = await _adbAccessService.ReadDeviceSnAsync();
            var r3 = await _adbAccessService.ReadDeviceSnAsync();

            // Assert
            Assert.That(r1.IsSuccess, Is.True);
            Assert.That(r2.IsSuccess, Is.True);
            Assert.That(r3.IsSuccess, Is.True);
            _processRunnerMock.Verify(
                x => x.RunAsync(TestAdbPath, "shell exit", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "ADB shell 预热应只执行一次");
        }
    }
}
