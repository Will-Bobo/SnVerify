/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：AdbDeviceService 单元测试（Mock IProcessRunner）。</remarks>

using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Product;
using SnVerify.Infrastructure.DeviceAccess.Service;
using SnVerify.Infrastructure.DeviceAccess.Session;
using SnVerify.Infrastructure.DeviceAccess.Command;
using SnVerify.Infrastructure.DeviceAccess.Parser;
using SnVerify.Services.Adb;

namespace SnVerify.Tests.Infrastructure.DeviceAccess
{
    [TestFixture]
    public class AdbDeviceServiceTests
    {
        [Test]
        public void ReadDeviceInfoAsync_WhenProfileNull_Throws()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock.Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));
            var session = new DeviceSessionManager("adb", processRunnerMock.Object);
            var command = new DeviceCommandExecutor("adb", processRunnerMock.Object);
            var parserFactory = new ParserFactory();
            var service = new AdbDeviceService(session, command, parserFactory);

            Assert.That(
                async () => await service.ReadDeviceInfoAsync(null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("profile"));
        }

        [Test]
        public void ReadDeviceInfoAsync_WhenAdbConfigNull_Throws()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            var session = new DeviceSessionManager("adb", processRunnerMock.Object);
            var command = new DeviceCommandExecutor("adb", processRunnerMock.Object);
            var parserFactory = new ParserFactory();
            var service = new AdbDeviceService(session, command, parserFactory);
            var profile = new ProductProfile { ProductCode = "KM001", AdbConfig = null };

            Assert.That(
                async () => await service.ReadDeviceInfoAsync(profile),
                Throws.InvalidOperationException.With.Message.Contains("ADB 命令未配置"));
        }

        [Test]
        public async Task ReadDeviceInfoAsync_WhenFieldCommandsConfigured_ExecutesAndReturnsDeviceInfo()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            processRunnerMock
                .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success("  SN001  "));
            var session = new DeviceSessionManager("adb", processRunnerMock.Object);
            var command = new DeviceCommandExecutor("adb", processRunnerMock.Object);
            var trimParser = new TrimParser();
            var fieldParsers = new Dictionary<string, IDeviceInfoParser>(System.StringComparer.OrdinalIgnoreCase) { { ParserKeys.Field.Trim, trimParser } };
            var parserFactory = new ParserFactory(fieldParsers, null);

            var profile = new ProductProfile
            {
                ProductCode = "KM001",
                AdbConfig = new DeviceAdbConfig
                {
                    BootstrapCommandSpecs = new List<BootstrapCommandSpec>(),
                    Commands = new List<DeviceInfoCommand>
                    {
                        new DeviceInfoCommand { Field = DeviceInfoField.DeviceSn, Command = "shell getprop ro.serialno", ParserKey = ParserKeys.Field.Trim }
                    }
                }
            };

            var service = new AdbDeviceService(session, command, parserFactory);
            var result = await service.ReadDeviceInfoAsync(profile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.DeviceSn, Is.EqualTo("SN001"));
        }

        [Test]
        public async Task ReadDeviceInfoAsync_ForSoltag25Profile_ShouldRunBootstrapAndTwoCommands()
        {
            var processRunnerMock = new Mock<IProcessRunner>();
            var executedArgs = new List<string>();

            processRunnerMock
                .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                .Callback<string, string, int, System.Threading.CancellationToken>((_, args, __, ___) => executedArgs.Add(args))
                .ReturnsAsync((string _, string args, int __, System.Threading.CancellationToken ___) =>
                {
                    if (args == "shell getprop sys.skyroam.osi.sn") return ProcessExecutionResult.Success(" SN_SOL ");
                    if (args == "shell getprop ro.build.display.id") return ProcessExecutionResult.Success(" VER_SOL ");
                    return ProcessExecutionResult.Success(string.Empty);
                });

            var session = new DeviceSessionManager("adb", processRunnerMock.Object);
            var command = new DeviceCommandExecutor("adb", processRunnerMock.Object);
            var trimParser = new TrimParser();
            var fieldParsers = new Dictionary<string, IDeviceInfoParser>(System.StringComparer.OrdinalIgnoreCase) { { ParserKeys.Field.Trim, trimParser } };
            var parserFactory = new ParserFactory(fieldParsers, null);

            var profile = new ProductProfile
            {
                ProductCode = "SOLTAG25",
                Mode = VerificationMode.Legacy,
                AdbConfig = new DeviceAdbConfig
                {
                    BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                    {
                        new BootstrapCommandSpec
                        {
                            Command = "shell ylzero",
                            AcceptableExitCodes = new[] { 127, 255 },
                            TimeoutBehavior = BootstrapTimeoutBehavior.Fail
                        }
                    },
                    AggregateCommand = null,
                    Commands = new List<DeviceInfoCommand>
                    {
                        new DeviceInfoCommand
                        {
                            Field = DeviceInfoField.DeviceSn,
                            Command = "shell getprop sys.skyroam.osi.sn",
                            ParserKey = ParserKeys.Field.Trim
                        },
                        new DeviceInfoCommand
                        {
                            Field = DeviceInfoField.AndroidVersion,
                            Command = "shell getprop ro.build.display.id",
                            ParserKey = ParserKeys.Field.Trim
                        }
                    }
                }
            };

            var service = new AdbDeviceService(session, command, parserFactory);
            var result = await service.ReadDeviceInfoAsync(profile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.DeviceSn, Is.EqualTo("SN_SOL"));
            Assert.That(result.AndroidVersion, Is.EqualTo("VER_SOL"));
            Assert.That(executedArgs, Does.Contain("shell ylzero"));
            Assert.That(executedArgs, Does.Contain("shell getprop sys.skyroam.osi.sn"));
            Assert.That(executedArgs, Does.Contain("shell getprop ro.build.display.id"));
        }
    }
}
