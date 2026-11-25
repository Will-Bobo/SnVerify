using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Domain.State;
using SnVerify.Domain.Validation;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.ExpectedData;
using SnVerify.Services.Logging;
using SnVerify.Services.Parameter;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
using SnVerify.Services.Yme009;
using SnVerify.Tests.Services;
using SnVerify.ViewModels;

namespace SnVerify.Tests.ViewModels
{
    [TestFixture]
    public class Yme009ExpectedRecoveryTests
    {
        [Test]
        public async Task Should_ResumeOriginalSession_When_InitializationFailsAfterCommit()
        {
            using (var host = await Host.CreateAsync())
            {
                var creates = 0;
                var flow = Mock.Of<IVerificationFlowService>(item => item.Snapshot == VerificationSnapshot.Idle());
                host.Flow.Setup(item => item.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(() =>
                    {
                        creates++;
                        if (creates == 1)
                            throw new InvalidOperationException("flow");
                        return flow;
                    });
                await host.StageAsync();

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Warned);
                var sessionName = Scalar(host, "SELECT SessionName FROM TestSession") as string;

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(host.Lifecycle.Snapshot.SessionId, Is.EqualTo(sessionName));
                Assert.That(host.Lifecycle.Snapshot.OrderId, Is.EqualTo("ORD"));
                Assert.That(host.Vm.SessionSnapshot.SessionId, Is.EqualTo(sessionName));
                Assert.That(host.Vm.Yme009StatusTitle, Is.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
                Assert.That(creates, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task Should_StayUndetectable_When_ResumeAlsoFails()
        {
            using (var host = await Host.CreateAsync())
            {
                host.Flow.Setup(item => item.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Throws(new InvalidOperationException("flow"));
                await host.StageAsync();

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Warned);
                host.Warned = false;
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Warned);

                await host.Vm.HandleScanInputAsync("SN-1");

                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(host.Vm.IsSessionReadyForTest, Is.False);
                Assert.That(host.Vm.IsYme009ScanAllowed, Is.False);
                Assert.That(host.Vm.IsStartBatchLocked, Is.False);
                Assert.That(host.Lifecycle.Snapshot.IsActive, Is.True);
                Assert.That(host.Vm.Yme009StatusTitle, Is.Not.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
                host.Round.Verify(item => item.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Test]
        public async Task Should_RejectScan_When_ParameterSaveFailsAfterSnapshot()
        {
            using (var host = await Host.CreateAsync())
            {
                host.Parameter.Setup(item => item.SaveParameterAsync(It.IsAny<VerificationParameter>()))
                    .ThrowsAsync(new IOException("parameter"));
                await host.StageAsync();

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Warned);
                await host.Vm.HandleScanInputAsync("SN-1");

                Assert.That(host.Vm.IsSessionActive, Is.True);
                Assert.That(host.Vm.IsSessionReadyForTest, Is.False);
                Assert.That(host.Vm.IsYme009ScanAllowed, Is.False);
                Assert.That(host.Vm.IsStartBatchLocked, Is.False);
                Assert.That(host.Lifecycle.Snapshot.IsActive, Is.True);
                Assert.That(host.Vm.Yme009StatusTitle, Is.Not.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                host.Round.Verify(item => item.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.WarningCount >= 2);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Should_RejectScan_When_SettingsSaveFails()
        {
            using (var host = await Host.CreateAsync())
            {
                host.Vm.SaveOperatorSettings = () => throw new IOException("settings");
                await host.StageAsync();

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Warned);
                await host.Vm.HandleScanInputAsync("SN-1");

                Assert.That(host.Vm.IsSessionActive, Is.True);
                Assert.That(host.Vm.IsSessionReadyForTest, Is.False);
                Assert.That(host.Vm.IsYme009ScanAllowed, Is.False);
                Assert.That(host.Vm.Yme009StatusTitle, Is.Not.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
                host.Round.Verify(item => item.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

                host.Vm.SaveOperatorSettings = null;
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(host.Vm.IsYme009ScanAllowed, Is.True);
            }
        }

        [Test]
        public async Task Should_AllowScan_When_InitializationCompletes()
        {
            using (var host = await Host.CreateAsync())
            {
                host.Round.Setup(item => item.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Yme009RoundResult(null, null, true, "ok"));
                await host.StageAsync();

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(host.Vm.Yme009StatusTitle, Is.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
                Assert.That(host.Vm.IsYme009ScanAllowed, Is.True);
                await host.Vm.HandleScanInputAsync("SN-1");
                host.Round.Verify(item => item.RunAsync("SN-1", It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Test]
        public async Task Should_ReimportIsolatedDataSet_When_NextSessionReusesStaging()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageAsync();
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);
                var summary = host.Vm.Yme009ExpectedSummary;

                host.Vm.EndBatchCommand.Execute(null);
                await WaitUntilAsync(() => !host.Vm.IsSessionActive);

                Assert.That(host.Vm.Yme009ExpectedSummary, Is.EqualTo(summary));
                Assert.That(host.Vm.IsSessionReadyForTest, Is.False);
                Assert.That(summary, Does.Contain("expected.xlsx"));

                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(2));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(2));
                Assert.That(
                    Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet d JOIN TestSession s ON s.Id = d.SessionId"),
                    Is.EqualTo(2));
                Assert.That(
                    Count(host, "SELECT COUNT(DISTINCT SessionId) FROM Yme009ExpectedDataSet"),
                    Is.EqualTo(2));
            }
        }

        [Test]
        public async Task Should_StopNextSession_When_StagedFileChangesOrDisappears()
        {
            using (var host = await Host.CreateAsync())
            {
                var path = await host.StageAsync();
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);
                host.Vm.EndBatchCommand.Execute(null);
                await WaitUntilAsync(() => !host.Vm.IsSessionActive);

                Yme009ExpectedWorkbookFiles.Write(path, Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-2"));
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.WarningCount >= 1);

                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(host.Vm.IsSessionReadyForTest, Is.False);
                host.Dialog.Verify(item => item.ShowWarning(
                    Yme009ExpectedOperatorText.FingerprintMismatch,
                    Yme009ExpectedOperatorText.StartFailedTitle), Times.Once);

                File.Delete(path);
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.WarningCount >= 2);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                host.Dialog.Verify(item => item.ShowWarning(
                    Yme009ExpectedOperatorText.FileUnavailable,
                    Yme009ExpectedOperatorText.StartFailedTitle), Times.Once);
            }
        }

        [Test]
        public async Task Should_UseCreateAndStart_When_NoExcelAndNoActiveSession()
        {
            var session = new Mock<ISessionLifecycleService>();
            var round = new Mock<IYme009RoundCoordinator>();
            session.Setup(item => item.Snapshot).Returns(SessionSnapshot.Idle());
            session.Setup(item => item.CreateAndStartSession("ORD", "ORD", "P", Yme009Product.Code))
                .Callback(() => session.Setup(item => item.Snapshot).Returns(SessionSnapshot.Active("S-1", "ORD", DateTime.Now)))
                .Returns("S-1");
            var vm = CreateNoExcelViewModel(session.Object, round.Object, null);

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.IsSessionReadyForTest, 5000);

            session.Verify(item => item.CreateAndStartSession("ORD", "ORD", "P", Yme009Product.Code), Times.Once);
            Assert.That(vm.IsYme009ScanAllowed, Is.True);
            Assert.That(vm.Yme009StatusTitle, Is.Not.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
        }

        [Test]
        public async Task Should_RejectLegacyScanAndAvoidSecondSession_When_InitializationFails()
        {
            var session = new Mock<ISessionLifecycleService>();
            var round = new Mock<IYme009RoundCoordinator>();
            var parameter = new Mock<IParameterService>();
            session.Setup(item => item.Snapshot).Returns(SessionSnapshot.Idle());
            session.Setup(item => item.CreateAndStartSession("ORD", "ORD", "P", Yme009Product.Code))
                .Callback(() => session.Setup(item => item.Snapshot).Returns(SessionSnapshot.Active("S-1", "ORD", DateTime.Now)))
                .Returns("S-1");
            parameter.Setup(item => item.SaveParameterAsync(It.IsAny<VerificationParameter>()))
                .ThrowsAsync(new IOException("parameter"));
            var vm = CreateNoExcelViewModel(session.Object, round.Object, parameter.Object);

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.IsSessionActive);
            await vm.HandleScanInputAsync("SN-1");

            Assert.That(vm.IsSessionReadyForTest, Is.False);
            Assert.That(vm.IsYme009ScanAllowed, Is.False);
            round.Verify(item => item.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            vm.StartBatchCommand.Execute(null);
            await Task.Delay(200);
            session.Verify(item => item.CreateAndStartSession(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        private static MainViewModel CreateNoExcelViewModel(
            ISessionLifecycleService session,
            IYme009RoundCoordinator round,
            IParameterService parameter)
        {
            var dialog = new Mock<IUserDialogService>();
            var storage = new Mock<IStorageService>();
            var flowFactory = new Mock<IVerificationFlowServiceFactory>();
            var logging = new Mock<ILoggingService>();
            var versionFlow = new Mock<IVersionVerificationFlowService>();
            logging.Setup(item => item.Snapshot).Returns(LoggingSnapshot.Idle());
            versionFlow.Setup(item => item.Snapshot).Returns(VerificationSnapshot.Idle());
            flowFactory.Setup(item => item.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Mock.Of<IVerificationFlowService>(item => item.Snapshot == VerificationSnapshot.Idle()));
            storage.Setup(item => item.ProjectNameExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            storage.Setup(item => item.GetAllProductsAsync()).ReturnsAsync(new System.Collections.Generic.List<Product>());
            storage.Setup(item => item.GetAllOrdersAsync()).ReturnsAsync(new System.Collections.Generic.List<Order>());
            storage.Setup(item => item.GetTestRecordsBySessionAsync(It.IsAny<string>()))
                .ReturnsAsync(new System.Collections.Generic.List<TestRecord>());
            storage.Setup(item => item.GetInternalSessionIdBySessionNameAsync(It.IsAny<string>())).ReturnsAsync(5);
            var validator = new Mock<IOrderNameValidator>();
            string message = null;
            validator.Setup(item => item.Validate(It.IsAny<string>(), out message)).Returns(true);

            var vm = new MainViewModel(
                session,
                flowFactory.Object,
                logging.Object,
                storage.Object,
                Mock.Of<IAdbAccessService>(),
                Mock.Of<IExportAggregationService>(),
                validator.Object,
                dialog.Object,
                versionFlow.Object,
                Path.GetTempPath(),
                new ProductRegistryAdapter(),
                parameter,
                Mock.Of<IDeviceAccessService>(),
                round);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ProjectIdInput = "P";
            vm.OrderIdInput = "ORD";
            vm.ExpectedAndroidVersion = "SW-1";
            return vm;
        }

        private static int Count(Host host, string sql)
        {
            return Convert.ToInt32(Scalar(host, sql));
        }

        private static object Scalar(Host host, string sql)
        {
            using (var connection = new SQLiteConnection("Data Source=" + host.DatabasePath + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteScalar();
                }
            }
        }

        private static string[] Row(string sn)
        {
            return new[] { sn, "48:C8:62:16:BF:1F", "111111111111111", "222222222222222" };
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 3000)
        {
            var start = DateTime.UtcNow;
            while (!predicate())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");
                await Task.Delay(20);
            }
        }

        private sealed class Host : IDisposable
        {
            public StorageService Storage { get; private set; }
            public SessionLifecycleService Lifecycle { get; private set; }
            public MainViewModel Vm { get; private set; }
            public Mock<IVerificationFlowServiceFactory> Flow { get; private set; }
            public Mock<IUserDialogService> Dialog { get; private set; }
            public Mock<IYme009RoundCoordinator> Round { get; private set; }
            public Mock<IParameterService> Parameter { get; private set; }
            public string DatabasePath { get; private set; }
            public bool Warned { get; set; }
            public int WarningCount { get; private set; }
            private string _directory;
            private string _stagedPath;

            public static async Task<Host> CreateAsync()
            {
                var host = new Host();
                var token = Guid.NewGuid().ToString("N");
                host.DatabasePath = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Recovery_" + token + ".db");
                host._directory = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Recovery_" + token);
                Directory.CreateDirectory(host._directory);
                host.Storage = new StorageService(host.DatabasePath);
                await host.Storage.InitializeAsync();
                host.Lifecycle = new SessionLifecycleService(host.Storage);
                host.Flow = new Mock<IVerificationFlowServiceFactory>();
                host.Dialog = new Mock<IUserDialogService>();
                host.Round = new Mock<IYme009RoundCoordinator>();
                host.Parameter = new Mock<IParameterService>();
                var logging = new Mock<ILoggingService>();
                var versionFlow = new Mock<IVersionVerificationFlowService>();
                logging.Setup(item => item.Snapshot).Returns(LoggingSnapshot.Idle());
                versionFlow.Setup(item => item.Snapshot).Returns(VerificationSnapshot.Idle());
                host.Flow.Setup(item => item.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(Mock.Of<IVerificationFlowService>(item => item.Snapshot == VerificationSnapshot.Idle()));
                host.Parameter.Setup(item => item.SaveParameterAsync(It.IsAny<VerificationParameter>()))
                    .Returns(Task.CompletedTask);
                host.Dialog.Setup(item => item.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
                host.Dialog.Setup(item => item.ShowWarning(It.IsAny<string>(), It.IsAny<string>()))
                    .Callback(() =>
                    {
                        host.Warned = true;
                        host.WarningCount++;
                    });
                var validator = new Mock<IOrderNameValidator>();
                string message = null;
                validator.Setup(item => item.Validate(It.IsAny<string>(), out message)).Returns(true);
                host.Vm = new MainViewModel(
                    host.Lifecycle,
                    host.Flow.Object,
                    logging.Object,
                    host.Storage,
                    Mock.Of<IAdbAccessService>(),
                    Mock.Of<IExportAggregationService>(),
                    validator.Object,
                    host.Dialog.Object,
                    versionFlow.Object,
                    Path.GetTempPath(),
                    new ProductRegistryAdapter(),
                    host.Parameter.Object,
                    Mock.Of<IDeviceAccessService>(),
                    host.Round.Object,
                    new Yme009ExpectedDataService(host.Storage));
                host.Vm.SelectedProductCode = Yme009Product.Code;
                host.Vm.ProjectIdInput = "P";
                host.Vm.OrderIdInput = "ORD";
                host.Vm.ExpectedAndroidVersion = "SW-1";
                return host;
            }

            public async Task<string> StageAsync()
            {
                _stagedPath = Path.Combine(_directory, "expected.xlsx");
                Yme009ExpectedWorkbookFiles.Write(_stagedPath, Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-1"));
                Dialog.Setup(item => item.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(_stagedPath);
                Vm.SelectYme009ExpectedFileCommand.Execute(null);
                await WaitUntilAsync(() => Vm.Yme009ExpectedSummary.Contains("expected.xlsx"));
                return _stagedPath;
            }

            public void Dispose()
            {
                Storage?.Dispose();
                if (File.Exists(DatabasePath))
                    File.Delete(DatabasePath);
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, true);
            }
        }
    }
}
