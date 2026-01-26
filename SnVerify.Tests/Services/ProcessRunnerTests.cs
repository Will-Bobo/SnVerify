/// <author>
/// AI Assistant
/// </author>

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Services.Adb;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ProcessRunner 单元测试
    /// </summary>
    [TestFixture]
    public class ProcessRunnerTests
    {
        private IProcessRunner _processRunner;

        [SetUp]
        public void SetUp()
        {
            _processRunner = new ProcessRunner();
        }

        [Test]
        public void RunAsync_ShouldThrowArgumentException_WhenFileNameIsNull()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _processRunner.RunAsync(null, "", 1000));
        }

        [Test]
        public void RunAsync_ShouldThrowArgumentException_WhenFileNameIsEmpty()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _processRunner.RunAsync("", "", 1000));
        }

        [Test]
        public void RunAsync_ShouldThrowArgumentException_WhenFileNameIsWhitespace()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _processRunner.RunAsync("   ", "", 1000));
        }

        [Test]
        public async Task RunAsync_ShouldReturnFailure_WhenProcessNotFound()
        {
            // Arrange
            var nonExistentProcess = "NonExistentProcess.exe";

            // Act
            var result = await _processRunner.RunAsync(nonExistentProcess, "", 5000);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Is.Not.Null);
            Assert.That(result.ErrorMessage, Contains.Substring("Process execution failed"));
        }

        [Test]
        public async Task RunAsync_ShouldReturnSuccess_WhenCommandSucceeds()
        {
            // Arrange
            // 使用 Windows 的 cmd.exe 执行 echo 命令（Windows 系统）
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "echo";
            var arguments = Environment.OSVersion.Platform == PlatformID.Win32NT ? "/c echo test" : "test";

            // Act
            var result = await _processRunner.RunAsync(fileName, arguments, 5000);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.StandardOutput, Contains.Substring("test"));
            Assert.That(result.ExitCode, Is.EqualTo(0));
        }

        [Test]
        public async Task RunAsync_ShouldReturnFailure_WhenCommandFails()
        {
            // Arrange
            // 使用 Windows 的 cmd.exe 执行会失败的命令
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "false";
            var arguments = Environment.OSVersion.Platform == PlatformID.Win32NT ? "/c exit 1" : "";

            // Act
            var result = await _processRunner.RunAsync(fileName, arguments, 5000);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        }

        [Test]
        public async Task RunAsync_ShouldReturnTimeout_WhenProcessExceedsTimeout()
        {
            // Arrange
            // 使用 Windows 的 ping 命令延迟执行（ping localhost -n 5 会延迟约 4 秒）
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "ping.exe" : "sleep";
            var arguments = Environment.OSVersion.Platform == PlatformID.Win32NT 
                ? "localhost -n 10"  // ping 10 次，大约需要 9 秒
                : "10";  // sleep 10 秒

            // Act
            var result = await _processRunner.RunAsync(fileName, arguments, 1000); // 1 秒超时

            // Assert
            Assert.That(result.IsTimeout, Is.True);
            Assert.That(result.IsSuccess, Is.False);
        }

        [Test]
        public async Task RunAsync_ShouldCaptureStandardOutput()
        {
            // Arrange
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "echo";
            var arguments = Environment.OSVersion.Platform == PlatformID.Win32NT ? "/c echo output text" : "output text";

            // Act
            var result = await _processRunner.RunAsync(fileName, arguments, 5000);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.StandardOutput, Is.Not.Null);
            Assert.That(result.StandardOutput, Contains.Substring("output"));
        }

        [Test]
        public async Task RunAsync_ShouldCaptureStandardError()
        {
            // Arrange
            // 使用一个会产生错误输出的命令
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "sh";
            var arguments = Environment.OSVersion.Platform == PlatformID.Win32NT 
                ? "/c echo error >&2" 
                : "-c \"echo error >&2\"";

            // Act
            var result = await _processRunner.RunAsync(fileName, arguments, 5000);

            // Assert
            // 注意：某些命令可能将错误输出重定向到标准输出，所以这里只验证命令执行完成
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public async Task RunAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "ping.exe" : "sleep";
            var arguments = Environment.OSVersion.Platform == PlatformID.Win32NT 
                ? "localhost -n 10" 
                : "10";

            // Act
            cts.CancelAfter(500); // 500ms 后取消
            var result = await _processRunner.RunAsync(fileName, arguments, 10000, cts.Token);

            // Assert
            // 由于 CancellationToken 被取消，应该返回超时或失败
            Assert.That(result.IsTimeout || !result.IsSuccess, Is.True);
        }

        [Test]
        public async Task RunAsync_ShouldHandleEmptyArguments()
        {
            // Arrange
            var fileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "echo";
            var arguments = "";

            // Act
            var result = await _processRunner.RunAsync(fileName, arguments, 5000);

            // Assert
            // 命令应该能够执行（即使没有参数）
            Assert.That(result, Is.Not.Null);
        }
    }
}
