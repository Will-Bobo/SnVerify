/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：DeviceSessionManager 单元测试（Mock IProcessRunner）。</remarks>

using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Infrastructure.DeviceAccess.Session;
using SnVerify.Services.Adb;

namespace SnVerify.Tests.Infrastructure.DeviceAccess
{
    [TestFixture]
    public class DeviceSessionManagerTests
    {
        [Test]
        public async Task EnsureSessionReadyAsync_WhenConfigNull_CompletesWithoutRunningBootstrap()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);

            await manager.EnsureSessionReadyAsync(null);

            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public async Task EnsureSessionReadyAsync_WhenBootstrapSpecsNullOrEmpty_CompletesWithoutRunningBootstrapCommand()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var configNullSpecs = new DeviceAdbConfig { BootstrapCommandSpecs = null };
            var configEmptySpecs = new DeviceAdbConfig { BootstrapCommandSpecs = new List<BootstrapCommandSpec>() };

            await manager.EnsureSessionReadyAsync(configNullSpecs);
            await manager.EnsureSessionReadyAsync(configEmptySpecs);

            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task EnsureSessionReadyAsync_WhenBootstrapSpecsConfigured_RunsBootstrapEveryCall_WarmupOnlyOnce()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec { Command = "shell ylzero" }
                }
            };

            await manager.EnsureSessionReadyAsync(config);
            await manager.EnsureSessionReadyAsync(config);

            // Warmup (shell exit): process lifetime once.
            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell exit", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            // Bootstrap (shell ylzero): every detection batch.
            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task EnsureSessionReadyAsync_WhenExitCode127_WithAcceptableExitCodes_Passes()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))  // warmup
                .ReturnsAsync(ProcessExecutionResult.Failure("cmd not found", exitCode: 127));
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        AcceptableExitCodes = new[] { 127, 255 }
                    }
                }
            };

            await manager.EnsureSessionReadyAsync(config);

            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task EnsureSessionReadyAsync_WhenExitCode255_WithAcceptableExitCodes_Passes()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))
                .ReturnsAsync(ProcessExecutionResult.Failure("user version", exitCode: 255));
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        AcceptableExitCodes = new[] { 127, 255 }
                    }
                }
            };

            await manager.EnsureSessionReadyAsync(config);

            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Test]
        public void EnsureSessionReadyAsync_WhenTimeout_WithFail_Throws()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))
                .ReturnsAsync(ProcessExecutionResult.Timeout());
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        TimeoutBehavior = BootstrapTimeoutBehavior.Fail
                    }
                }
            };

            Assert.That(
                async () => await manager.EnsureSessionReadyAsync(config),
                Throws.InvalidOperationException.With.Message.Contains("Bootstrap").And.Message.Contains("超时"));
        }

        [Test]
        public async Task EnsureSessionReadyAsync_WhenTimeout_WithIgnore_Passes()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))
                .ReturnsAsync(ProcessExecutionResult.Timeout());
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        TimeoutBehavior = BootstrapTimeoutBehavior.Ignore
                    }
                }
            };

            await manager.EnsureSessionReadyAsync(config);

            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task EnsureSessionReadyAsync_WhenTimeout_WithRetry_SecondSuccess_Passes()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))
                .ReturnsAsync(ProcessExecutionResult.Timeout())
                .ReturnsAsync(ProcessExecutionResult.Success(""));
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        TimeoutBehavior = BootstrapTimeoutBehavior.Retry
                    }
                }
            };

            await manager.EnsureSessionReadyAsync(config);

            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public void EnsureSessionReadyAsync_WhenTimeout_WithRetry_StillTimeoutAfterMax_Throws()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))
                .ReturnsAsync(ProcessExecutionResult.Timeout())
                .ReturnsAsync(ProcessExecutionResult.Timeout())
                .ReturnsAsync(ProcessExecutionResult.Timeout());
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        TimeoutBehavior = BootstrapTimeoutBehavior.Retry
                    }
                }
            };

            Assert.That(
                async () => await manager.EnsureSessionReadyAsync(config),
                Throws.InvalidOperationException.With.Message.Contains("Bootstrap").And.Message.Contains("超时"));
            processRunnerMock.Verify(p => p.RunAsync(It.IsAny<string>(), "shell ylzero", It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(3));
        }

        [Test]
        public void EnsureSessionReadyAsync_WhenNonAcceptableExitCode_Throws()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .SetupSequence(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""))
                .ReturnsAsync(ProcessExecutionResult.Failure("error", exitCode: 1));
            var manager = new DeviceSessionManager("adb", processRunnerMock.Object);
            var config = new DeviceAdbConfig
            {
                BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                {
                    new BootstrapCommandSpec
                    {
                        Command = "shell ylzero",
                        AcceptableExitCodes = new[] { 127, 255 }
                    }
                }
            };

            Assert.That(
                async () => await manager.EnsureSessionReadyAsync(config),
                Throws.InvalidOperationException.With.Message.Contains("Bootstrap").And.Message.Contains("1"));
        }
    }
}
