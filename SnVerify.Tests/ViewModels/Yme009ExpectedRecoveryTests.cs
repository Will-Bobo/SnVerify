using System;
using System.Collections.Concurrent;
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

        [Test]
        public async Task Should_EnableStart_When_NewTaskHasNoRunningSession()
        {
            using (var host = await Host.CreateAsync())
            {
                Assert.That(host.Vm.StartBatchCommand.CanExecute(null), Is.True);
                Assert.That(host.Vm.IsStartBatchLocked, Is.False);

                await host.StageFileAsync("a.xlsx", "SN-A");
                Assert.That(host.Vm.StartBatchCommand.CanExecute(null), Is.True);
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(host.Vm.StartBatchCommand.CanExecute(null), Is.False);
                Assert.That(host.Vm.IsStartBatchLocked, Is.True);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Scalar(host, "SELECT FileName FROM Yme009ExpectedDataSet"), Is.EqualTo("a.xlsx"));
            }
        }

        [Test]
        public async Task Should_RestoreOriginalSessionAndDataSet_When_ProcessRestarts()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("expected.xlsx", "SN-1");
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                var running = await host.Storage.GetYme009RunningSessionsAsync();
                Assert.That(running, Has.Count.EqualTo(1));
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.EqualTo(Yme009SessionStatus.Running));
                var sessionName = running[0].SessionName;
                var sessionId = running[0].SessionId;
                host.Parameter.Setup(item => item.GetParameterAsync(sessionId))
                    .ReturnsAsync(new VerificationParameter
                    {
                        SessionId = sessionId,
                        ExpectedAndroidVersion = "SW-1"
                    });

                var restarted = host.OpenRestarted();
                await WaitUntilAsync(() =>
                    restarted.IsSessionActive
                    && !restarted.IsSessionReadyForTest
                    && restarted.StartBatchCommand.CanExecute(null)
                    && !restarted.IsStartBatchLocked, 5000);

                Assert.That(restarted.SelectYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.That(restarted.ClearYme009ExpectedFileCommand.CanExecute(null), Is.False);
                restarted.VerificationSnapshot = VerificationSnapshot.Processing("SN-1");
                Assert.That(restarted.StartBatchCommand.CanExecute(null), Is.False);
                Assert.That(restarted.IsStartBatchLocked, Is.True);
                restarted.VerificationSnapshot = VerificationSnapshot.Idle();
                Assert.That(restarted.StartBatchCommand.CanExecute(null), Is.True);

                restarted.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => restarted.IsSessionReadyForTest, 5000);

                Assert.That(restarted.StartBatchCommand.CanExecute(null), Is.False);
                Assert.That(restarted.IsStartBatchLocked, Is.True);
                Assert.That(restarted.SelectYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.That(restarted.ClearYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.EqualTo(Yme009SessionStatus.Running));
                Assert.That(host.Lifecycle.GetCurrentSessionId(), Is.EqualTo(sessionName));
                Assert.That(restarted.SessionSnapshot.SessionId, Is.EqualTo(sessionName));
                Assert.That(restarted.ProjectIdInput, Is.EqualTo("P"));
                Assert.That(restarted.OrderIdInput, Is.EqualTo("ORD"));
                Assert.That(restarted.ExpectedAndroidVersion, Is.EqualTo("SW-1"));
                Assert.That(restarted.Yme009ExpectedSummary, Does.Contain("expected.xlsx"));
                Assert.That(restarted.SelectYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.That(restarted.ClearYme009ExpectedFileCommand.CanExecute(null), Is.False);

                var restoredId = await host.Storage.GetInternalSessionIdBySessionNameAsync(sessionName);
                Assert.That(restoredId, Is.EqualTo(sessionId));
                Assert.That(await host.Storage.SessionHasYme009ExpectedDataSetAsync(restoredId.Value), Is.True);
                var device = await host.Storage.FindYme009ExpectedDeviceAsync(restoredId.Value, "SN-1");
                Assert.That(device.Found, Is.True);
                Assert.That(device.Device.WifiMac, Is.EqualTo("48:C8:62:16:BF:1F"));

                restarted.StartBatchCommand.Execute(null);
                await Task.Delay(200);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Should_NotRestoreCompletedOrHistoricalNullSession()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("expected.xlsx", "SN-1");
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);
                var sessionId = Convert.ToInt32(Scalar(host, "SELECT Id FROM TestSession"));

                host.Vm.EndBatchCommand.Execute(null);
                await WaitUntilAsync(() => !host.Vm.IsSessionActive);
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.EqualTo(Yme009SessionStatus.Completed));
                Assert.That(Scalar(host, "SELECT EndTime FROM TestSession"), Is.Not.Null.And.Not.EqualTo(DBNull.Value));

                var afterEnd = host.OpenRestarted();
                await AssertStaysInactiveAsync(afterEnd);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.EqualTo(Yme009SessionStatus.Completed));

                Execute(host, "UPDATE TestSession SET Status = NULL, EndTime = NULL WHERE Id = " + sessionId);
                var historical = host.OpenRestarted();
                await AssertStaysInactiveAsync(historical);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.Null.Or.EqualTo(DBNull.Value));
                Assert.That(Scalar(host, "SELECT EndTime FROM TestSession"), Is.Null.Or.EqualTo(DBNull.Value));
            }
        }

        [Test]
        public async Task Should_StopAutoRestore_When_MultipleRunningSessionsExist()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("expected.xlsx", "SN-1");
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);
                Execute(host, @"
INSERT INTO TestSession (SessionName, OrderId, StartTime, EndTime, Status)
SELECT 'ORD_EXTRA', OrderId, StartTime, NULL, 'Running' FROM TestSession LIMIT 1");
                var warnings = host.WarningCount;

                var restarted = host.OpenRestarted();
                await WaitUntilAsync(() => host.WarningCount > warnings);
                await AssertStaysInactiveAsync(restarted);

                Assert.That(host.LastWarning, Does.Contain("多个未结束的检测任务"));
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(2));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
                Assert.That(await host.Storage.GetYme009RunningSessionsAsync(), Has.Count.EqualTo(2));
            }
        }

        [Test]
        public async Task Should_ImportLastSuccessfulPrecheck_When_FileIsReplacedBeforeStart()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("a.xlsx", "SN-A");
                await host.StageFileAsync("b.xlsx", "SN-B");
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(Scalar(host, "SELECT FileName FROM Yme009ExpectedDataSet"), Is.EqualTo("b.xlsx"));
                var sessionId = Convert.ToInt32(Scalar(host, "SELECT Id FROM TestSession"));
                Assert.That((await host.Storage.FindYme009ExpectedDeviceAsync(sessionId, "SN-B")).Found, Is.True);
                Assert.That((await host.Storage.FindYme009ExpectedDeviceAsync(sessionId, "SN-A")).Found, Is.False);
                Assert.That(host.Vm.SelectYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.That(host.Vm.ClearYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await host.Storage.EnsureYme009ExpectedDataReplaceAllowedAsync(sessionId));
            }
        }

        [Test]
        public async Task Should_KeepLastSuccessfulFile_When_NextPrecheckFails()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("a.xlsx", "SN-A");
                var summary = host.Vm.Yme009ExpectedSummary;
                await host.StageInvalidAsync("bad.xlsx");

                Assert.That(host.Vm.Yme009ExpectedSummary, Is.EqualTo(summary));
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(Scalar(host, "SELECT FileName FROM Yme009ExpectedDataSet"), Is.EqualTo("a.xlsx"));
                var sessionId = Convert.ToInt32(Scalar(host, "SELECT Id FROM TestSession"));
                Assert.That((await host.Storage.FindYme009ExpectedDeviceAsync(sessionId, "SN-A")).Found, Is.True);
            }
        }

        [Test]
        public async Task Should_NotImportFailedFile_When_NoPrecheckSucceeded()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageInvalidAsync("bad.xlsx");

                Assert.That(host.Vm.Yme009ExpectedSummary, Is.EqualTo("未选择"));
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);

                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Count(host, "SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(0));
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.EqualTo(Yme009SessionStatus.Running));
                Assert.That(host.Vm.SelectYme009ExpectedFileCommand.CanExecute(null), Is.False);
                Assert.That(host.Vm.ClearYme009ExpectedFileCommand.CanExecute(null), Is.False);
            }
        }

        [Test, Timeout(8000)]
        public async Task Should_FinishRestoreOnPumpedContext_When_OneRunningSessionExists()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("expected.xlsx", "SN-1");
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);
                var sessionName = host.Lifecycle.GetCurrentSessionId();
                var sessionId = Convert.ToInt32(Scalar(host, "SELECT Id FROM TestSession"));

                var context = new QueueSynchronizationContext();
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                MainViewModel restarted = null;
                try
                {
                    restarted = host.OpenRestarted(false);
                    var restore = restarted.RestoreYme009RunningSessionAsync();
                    PumpUntil(context, restore);

                    Assert.That(restore.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    Assert.That(restarted.IsSessionActive, Is.True);
                    Assert.That(restarted.IsSessionReadyForTest, Is.False);
                    Assert.That(restarted.StartBatchCommand.CanExecute(null), Is.True);
                    Assert.That(restarted.IsStartBatchLocked, Is.False);
                    Assert.That(restarted.SessionSnapshot.SessionId, Is.EqualTo(sessionName));
                    Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }

                var restoredId = await host.Storage.GetInternalSessionIdBySessionNameAsync(sessionName);
                Assert.That(restoredId, Is.EqualTo(sessionId));
                Assert.That(await host.Storage.SessionHasYme009ExpectedDataSetAsync(restoredId.Value), Is.True);
                Assert.That((await host.Storage.FindYme009ExpectedDeviceAsync(restoredId.Value, "SN-1")).Found, Is.True);
            }
        }

        [Test, Timeout(8000)]
        public async Task Should_ReportMultipleRunningSessionsWithoutBlockingDispatcher()
        {
            using (var host = await Host.CreateAsync())
            {
                await host.StageFileAsync("expected.xlsx", "SN-1");
                host.Vm.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => host.Vm.IsSessionReadyForTest, 5000);
                Execute(host, @"
INSERT INTO TestSession (SessionName, OrderId, StartTime, EndTime, Status)
SELECT 'ORD_EXTRA', OrderId, StartTime, NULL, 'Running' FROM TestSession LIMIT 1");
                var warnings = host.WarningCount;
                var context = new QueueSynchronizationContext();
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var restarted = host.OpenRestarted(false);
                    var restore = restarted.RestoreYme009RunningSessionAsync();
                    PumpUntil(context, restore);

                    Assert.That(restore.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    Assert.That(restarted.IsSessionActive, Is.False);
                    Assert.That(host.WarningCount, Is.GreaterThan(warnings));
                    Assert.That(host.LastWarning, Does.Contain("多个未结束的检测任务"));
                    Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(2));
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }
        }

        [Test, Timeout(8000)]
        public async Task Should_LeaveNewTaskAvailable_When_PumpedRestoreFindsNoRunningSession()
        {
            using (var host = await Host.CreateAsync())
            {
                var context = new QueueSynchronizationContext();
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                MainViewModel restarted = null;
                try
                {
                    restarted = host.OpenRestarted(false);
                    var restore = restarted.RestoreYme009RunningSessionAsync();
                    PumpUntil(context, restore);
                    Assert.That(restore.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    Assert.That(restarted.IsSessionActive, Is.False);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }

                restarted.ProjectIdInput = "P";
                restarted.OrderIdInput = "ORD";
                restarted.ExpectedAndroidVersion = "SW-1";
                restarted.StartBatchCommand.Execute(null);
                await WaitUntilAsync(() => restarted.IsSessionReadyForTest, 5000);
                Assert.That(Count(host, "SELECT COUNT(*) FROM TestSession"), Is.EqualTo(1));
                Assert.That(Scalar(host, "SELECT Status FROM TestSession"), Is.EqualTo(Yme009SessionStatus.Running));
            }
        }

        [Test, Timeout(8000)]
        public void Should_ShowRestoreErrorWithoutBlockingDispatcher_When_ActivationThrows()
        {
            var session = new Mock<ISessionLifecycleService>();
            var dialog = new Mock<IUserDialogService>();
            var storage = new Mock<IStorageService>();
            var parameter = new Mock<IParameterService>();
            var logging = new Mock<ILoggingService>();
            var flowFactory = new Mock<IVerificationFlowServiceFactory>();
            var versionFlow = new Mock<IVersionVerificationFlowService>();
            logging.Setup(item => item.Snapshot).Returns(LoggingSnapshot.Idle());
            versionFlow.Setup(item => item.Snapshot).Returns(VerificationSnapshot.Idle());
            flowFactory.Setup(item => item.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Mock.Of<IVerificationFlowService>(item => item.Snapshot == VerificationSnapshot.Idle()));
            session.Setup(item => item.Snapshot).Returns(SessionSnapshot.Idle());
            storage.Setup(item => item.GetYme009RunningSessionsAsync())
                .ReturnsAsync(new[]
                {
                    new Yme009RunningSession(8, "S-FAIL", "ORD", "P", Yme009Product.Code, "expected.xlsx", 1)
                });
            parameter.Setup(item => item.GetParameterAsync(8)).ReturnsAsync((VerificationParameter)null);
            session.Setup(item => item.StartSessionAsync("S-FAIL", "ORD"))
                .ThrowsAsync(new InvalidOperationException("数据库不可用"));
            string warning = null;
            dialog.Setup(item => item.ShowWarning(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((message, title) => warning = message);

            var vm = new MainViewModel(
                session.Object,
                flowFactory.Object,
                logging.Object,
                storage.Object,
                Mock.Of<IAdbAccessService>(),
                Mock.Of<IExportAggregationService>(),
                Mock.Of<IOrderNameValidator>(),
                dialog.Object,
                versionFlow.Object,
                Path.GetTempPath(),
                new ProductRegistryAdapter(),
                parameter.Object,
                Mock.Of<IDeviceAccessService>(),
                Mock.Of<IYme009RoundCoordinator>());
            vm.SuppressAutomaticYme009Restore = true;
            vm.SelectedProductCode = Yme009Product.Code;

            var context = new QueueSynchronizationContext();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var restore = vm.RestoreYme009RunningSessionAsync();
                PumpUntil(context, restore);
                Assert.That(restore.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Assert.That(vm.IsSessionActive, Is.False);
            Assert.That(warning, Is.EqualTo("无法恢复未结束的检测任务。"));
            session.Verify(item => item.StartSession(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        private static void PumpUntil(QueueSynchronizationContext context, Task task, int timeoutMs = 5000)
        {
            var start = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    Assert.Fail("恢复流程没有在可泵送的同步上下文中完成，调用链仍在阻塞 Dispatcher。");
                if (!context.Drain())
                    Thread.Sleep(10);
            }
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

        private static async Task AssertStaysInactiveAsync(MainViewModel viewModel)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < 1000)
            {
                if (viewModel.IsSessionActive)
                    Assert.Fail("不应自动恢复该 Session。");
                await Task.Delay(20);
            }
        }

        private static void Execute(Host host, string sql)
        {
            using (var connection = new SQLiteConnection("Data Source=" + host.DatabasePath + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
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
            public string LastWarning { get; private set; }
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
                    .Callback<string, string>((message, title) =>
                    {
                        host.LastWarning = message;
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

            public Task<string> StageAsync()
            {
                return StageFileAsync("expected.xlsx", "SN-1");
            }

            public async Task<string> StageFileAsync(string fileName, string sn)
            {
                _stagedPath = Path.Combine(_directory, fileName);
                Yme009ExpectedWorkbookFiles.Write(_stagedPath, Yme009ExpectedWorkbookFiles.RequiredHeaders, Row(sn));
                Dialog.Setup(item => item.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(_stagedPath);
                Vm.SelectYme009ExpectedFileCommand.Execute(null);
                await WaitUntilAsync(() => Vm.Yme009ExpectedSummary.Contains(fileName));
                return _stagedPath;
            }

            public async Task StageInvalidAsync(string fileName)
            {
                var path = Path.Combine(_directory, fileName);
                File.WriteAllText(path, "not an xlsx");
                var warnings = WarningCount;
                Dialog.Setup(item => item.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(path);
                Vm.SelectYme009ExpectedFileCommand.Execute(null);
                await WaitUntilAsync(() => WarningCount > warnings);
            }

            public MainViewModel OpenRestarted(bool startRestore = true)
            {
                Lifecycle = new SessionLifecycleService(Storage);
                var logging = new Mock<ILoggingService>();
                var versionFlow = new Mock<IVersionVerificationFlowService>();
                logging.Setup(item => item.Snapshot).Returns(LoggingSnapshot.Idle());
                versionFlow.Setup(item => item.Snapshot).Returns(VerificationSnapshot.Idle());
                var validator = new Mock<IOrderNameValidator>();
                string message = null;
                validator.Setup(item => item.Validate(It.IsAny<string>(), out message)).Returns(true);
                var vm = new MainViewModel(
                    Lifecycle,
                    Flow.Object,
                    logging.Object,
                    Storage,
                    Mock.Of<IAdbAccessService>(),
                    Mock.Of<IExportAggregationService>(),
                    validator.Object,
                    Dialog.Object,
                    versionFlow.Object,
                    Path.GetTempPath(),
                    new ProductRegistryAdapter(),
                    Parameter.Object,
                    Mock.Of<IDeviceAccessService>(),
                    Round.Object,
                    new Yme009ExpectedDataService(Storage));
                vm.SuppressAutomaticYme009Restore = true;
                vm.SelectedProductCode = Yme009Product.Code;
                Vm = vm;
                if (startRestore)
                    vm.RestoreYme009RunningSessionAsync();
                return vm;
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

    internal sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Tuple<SendOrPostCallback, object>> _queue =
            new ConcurrentQueue<Tuple<SendOrPostCallback, object>>();

        public override void Post(SendOrPostCallback callback, object state)
        {
            _queue.Enqueue(Tuple.Create(callback, state));
        }

        public override void Send(SendOrPostCallback callback, object state)
        {
            callback(state);
        }

        public bool Drain()
        {
            var ran = false;
            Tuple<SendOrPostCallback, object> item;
            while (_queue.TryDequeue(out item))
            {
                ran = true;
                item.Item1(item.Item2);
            }

            return ran;
        }
    }
}
