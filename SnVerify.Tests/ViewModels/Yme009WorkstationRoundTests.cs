using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Yme009;
using SnVerify.ViewModels;

namespace SnVerify.Tests.ViewModels
{
    [TestFixture]
    public class Yme009WorkstationRoundTests
    {
        [Test]
        public void SessionStart_IsIdleAndScanAllowed_SessionEndClearsRound()
        {
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out var session);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = "TARGET-1";
            session.Setup(s => s.GetCurrentSessionId()).Returns("S-1");

            vm.SessionSnapshot = SessionSnapshot.Active("S-1", "ORDER", System.DateTime.Now);

            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Idle));
            Assert.That(vm.Yme009Headline, Is.EqualTo(Yme009WorkstationText.WaitingForScan));
            Assert.That(vm.IsYme009ScanAllowed, Is.True);
            Assert.That(vm.Yme009PassCount, Is.EqualTo(0));

            vm.SessionSnapshot = SessionSnapshot.Ended("S-1", "ORDER", System.DateTime.Now, System.DateTime.Now);

            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Idle));
            Assert.That(vm.Yme009Headline, Is.Empty);
            Assert.That(vm.IsYme009ScanAllowed, Is.False);
            Assert.That(vm.ShowYme009SessionRecords, Is.True);
        }

        [Test]
        public async Task BusyScan_IsRejectedBeforeTextIsStored_AndDoesNotStartAnotherRound()
        {
            var gate = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<Yme009RoundResult>();
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    gate.TrySetResult(true);
                    return await release.Task;
                });

            var vm = Yme009ProductAccessTests.CreateForWorkstation(out var session, round.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = "TARGET-1";
            session.Setup(s => s.GetCurrentSessionId()).Returns("S-1");
            vm.SessionSnapshot = SessionSnapshot.Active("S-1", "ORDER", System.DateTime.Now);

            var first = vm.HandleScanInputAsync("SN-KEEP");
            await gate.Task;

            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Running));
            Assert.That(vm.Yme009ScannedSnText, Is.EqualTo("SN-KEEP"));
            Assert.That(vm.IsScanBoxInteractive, Is.True);
            Assert.That(vm.IsYme009ScanAllowed, Is.False);

            vm.ScanInputText = "SN-OTHER\r";

            Assert.That(vm.StatusBarMessage, Is.EqualTo(Yme009WorkstationText.BusyScan));
            Assert.That(vm.ScanInputText, Is.Null.Or.Empty);
            Assert.That(vm.ScanInputText, Is.Not.EqualTo("SN-OTHER"));
            Assert.That(vm.Yme009ScannedSnText, Is.EqualTo("SN-KEEP"));
            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Running));
            round.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

            release.TrySetResult(new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.AtUncertain, false, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "log"));
            await first;

            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.AccessStopped));
            Assert.That(vm.Yme009HasOverallResult, Is.False);
            Assert.That(vm.Yme009ScannedSnText, Is.EqualTo("SN-KEEP"));
            Assert.That(vm.IsYme009ScanAllowed, Is.True);
        }

        [Test]
        public async Task NextRound_ClearsPreviousResult_AndKeepsSessionHistory()
        {
            var release = new TaskCompletionSource<Yme009RoundResult>();
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => release.Task);
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out var session, round.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = "TARGET-1";
            session.Setup(s => s.GetCurrentSessionId()).Returns("S-1");
            vm.SessionSnapshot = SessionSnapshot.Active("S-1", "ORDER", System.DateTime.Now);

            var first = vm.HandleScanInputAsync("SN-1");
            release.TrySetResult(PassRound());
            await first;

            Assert.That(vm.Yme009OverallText, Is.EqualTo("PASS"));
            Assert.That(vm.Yme009StatusTitle, Is.EqualTo("PASS"));
            Assert.That(vm.IsYme009ScanAllowed, Is.True);
            Assert.That(vm.Yme009SessionRecords.Count, Is.EqualTo(0));

            release = new TaskCompletionSource<Yme009RoundResult>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => release.Task);
            var second = vm.HandleScanInputAsync("SN-2");

            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Running));
            Assert.That(vm.Yme009HasOverallResult, Is.False);
            Assert.That(vm.Yme009OverallText, Is.Empty);
            Assert.That(vm.Yme009ScannedSnText, Is.EqualTo("SN-2"));
            Assert.That(vm.Yme009SessionRecords.Count, Is.EqualTo(0));
            Assert.That(vm.ShowYme009SessionRecords, Is.True);

            release.TrySetResult(PassRound());
            await second;
        }

        [Test]
        public async Task Should_ShowUnresolvedHeadline_When_RoundResultIsUnresolved()
        {
            var release = new TaskCompletionSource<Yme009RoundResult>();
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => release.Task);
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out var session, round.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = "TARGET-1";
            session.Setup(s => s.GetCurrentSessionId()).Returns("S-1");
            vm.SessionSnapshot = SessionSnapshot.Active("S-1", "ORDER", DateTime.Now);

            var scan = vm.HandleScanInputAsync("SN-1");
            release.TrySetResult(UnresolvedRound());
            await scan;

            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Completed));
            Assert.That(vm.Yme009HasOverallResult, Is.True);
            Assert.That(vm.Yme009OverallText, Is.EqualTo(Yme009ResultClassifier.UnresolvedResult));
            Assert.That(vm.Yme009HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Unresolved));
            Assert.That(vm.Yme009StatusTitle, Is.EqualTo(Yme009ResultClassifier.UnresolvedResult));
            Assert.That(vm.Yme009StatusTitle, Is.Not.EqualTo("FAIL"));
            Assert.That(vm.Yme009StatusTitle, Is.Not.EqualTo("等待扫码"));
            Assert.That(vm.Yme009ProcessPrompt, Is.Not.EqualTo(Yme009WorkstationText.RoundIncomplete));
        }

        private static Yme009RoundResult PassRound()
        {
            var decision = Yme009InspectionDecision.Judged(
                new Yme009ParsedAttributes(
                    new Yme009ParsedField(Yme009FieldKind.DeviceSn, Yme009ReadStatus.Ok, "DEV"),
                    new Yme009ParsedField(Yme009FieldKind.SoftwareVersion, Yme009ReadStatus.Ok, "VER"),
                    new Yme009ParsedField(Yme009FieldKind.Imei1, Yme009ReadStatus.Ok, "1"),
                    new Yme009ParsedField(Yme009FieldKind.Imei2, Yme009ReadStatus.Ok, "2"),
                    new Yme009ParsedField(Yme009FieldKind.WifiMac, Yme009ReadStatus.Ok, "M"),
                    new Yme009ParsedField(Yme009FieldKind.ProductionTest, Yme009ReadStatus.Ok, "1"),
                    new Yme009ParsedField(Yme009FieldKind.AgingReboot, Yme009ReadStatus.Ok, "1"),
                    new Yme009ParsedField(Yme009FieldKind.AgingReset, Yme009ReadStatus.Ok, "1")),
                "PASS",
                null,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                null,
                null,
                null,
                null,
                null);
            return new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "log",
                true,
                4,
                decision);
        }

        private static Yme009RoundResult UnresolvedRound()
        {
            var pass = PassRound();
            var decision = Yme009ExpectedIdentityDecision.Apply(pass.Decision, Yme009ExpectedIdentityCheck.UnresolvedQuery("InvalidOperationException：expected"));
            return new Yme009RoundResult(pass.Access, pass.Close, pass.CleanedUp, "log", true, 5, decision);
        }

        [Test]
        public void OtherProduct_StillDisablesScanBoxWhileProcessing()
        {
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out _);
            vm.SelectedProductCode = "KM001";
            vm.VerificationSnapshot = VerificationSnapshot.Processing("SN", "batch");

            Assert.That(vm.ShowYme009Workstation, Is.False);
            Assert.That(vm.IsScanBoxInteractive, Is.False);
            Assert.That(vm.IsScanInputEnabled, Is.False);
        }

        [Test]
        public async Task EndingSession_BlocksNewRound_AndLateResultDoesNotReturn()
        {
            var entered = new TaskCompletionSource<bool>();
            var releaseQuery = new TaskCompletionSource<List<TestRecord>>();
            var round = new Mock<IYme009RoundCoordinator>();
            var vm = Prepare(out var session, out var storage, round.Object);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S-1"))
                .Returns(async () =>
                {
                    entered.TrySetResult(true);
                    return await releaseQuery.Task;
                });
            session.Setup(s => s.EndSession()).Callback(() =>
                session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Ended("S-1", "ORDER", DateTime.Now, DateTime.Now)));

            vm.EndBatchCommand.Execute(null);
            await entered.Task;

            Assert.That(vm.IsSessionEnding, Is.True);
            Assert.That(vm.EndBatchCommand.CanExecute(null), Is.False);
            await vm.HandleScanInputAsync("SN-LATE");
            vm.ScanInputText = "SN-LATE\r";
            Assert.That(vm.ScanInputText, Is.Empty);
            round.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            vm.EndBatchCommand.Execute(null);
            releaseQuery.TrySetResult(new List<TestRecord> { new TestRecord() });
            await WaitUntilAsync(() => !vm.IsSessionEnding);

            session.Verify(s => s.EndSession(), Times.Once);
            Assert.That(vm.IsSessionActive, Is.False);
            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Idle));
            Assert.That(vm.Yme009HasOverallResult, Is.False);
        }

        [Test]
        public async Task InFlightRound_DoesNotProjectAfterSessionHasEnded()
        {
            var gate = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<Yme009RoundResult>();
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    gate.TrySetResult(true);
                    return await release.Task;
                });
            var vm = Prepare(out var session, out _, round.Object);
            var running = vm.HandleScanInputAsync("SN-OLD");
            await gate.Task;

            vm.SessionSnapshot = SessionSnapshot.Ended("S-1", "ORDER", DateTime.Now, DateTime.Now);
            release.TrySetResult(PassRound());
            await running;

            Assert.That(vm.IsSessionActive, Is.False);
            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Idle));
            Assert.That(vm.Yme009HasOverallResult, Is.False);
            Assert.That(vm.Yme009Headline, Is.Empty);
            Assert.That(vm.Yme009ScannedSnText, Is.Empty);
        }

        [Test]
        public async Task EndingSession_QueryFailure_ReleasesTheGate()
        {
            var vm = Prepare(out var session, out var storage, null);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S-1"))
                .Returns(async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("query");
                });

            vm.EndBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.IsSessionEnding);
            await WaitUntilAsync(() => !vm.IsSessionEnding);

            Assert.That(vm.IsSessionActive, Is.True);
            Assert.That(vm.IsYme009ScanAllowed, Is.True);
            session.Verify(s => s.EndSession(), Times.Never);
        }

        [Test]
        public async Task EndingSession_EndFailure_ReleasesTheGate()
        {
            var vm = Prepare(out var session, out var storage, null);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S-1"))
                .ReturnsAsync(new List<TestRecord> { new TestRecord() });
            session.Setup(s => s.EndSession()).Throws(new InvalidOperationException("end"));

            vm.EndBatchCommand.Execute(null);
            await WaitUntilAsync(() => !vm.IsSessionEnding && vm.IsYme009ScanAllowed);

            Assert.That(vm.IsSessionActive, Is.True);
            session.Verify(s => s.EndSession(), Times.Once);
        }

        [Test]
        public async Task BusyScan_NotifiesWhenSourceIsAlreadyEmpty_AndDoesNotRemainForNextRound()
        {
            var gate = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<Yme009RoundResult>();
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    gate.TrySetResult(true);
                    return await release.Task;
                });
            var vm = Prepare(out _, out _, round.Object);
            var notifications = 0;
            vm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.ScanInputText))
                    notifications++;
            };

            var first = vm.HandleScanInputAsync("SN-KEEP");
            await gate.Task;
            notifications = 0;
            vm.ScanInputText = "SN-OTHER\r";

            Assert.That(notifications, Is.GreaterThan(0));
            Assert.That(vm.ScanInputText, Is.Empty);
            vm.ScanInputText = "";
            round.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

            release.TrySetResult(PassRound());
            await first;
            await vm.HandleScanInputAsync(vm.ScanInputText);

            round.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(vm.Yme009ScannedSnText, Is.EqualTo("SN-KEEP"));
        }

        [Test]
        public void OtherProduct_StillAcceptsScanTextWhileProcessing()
        {
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out _);
            foreach (var code in new[] { "KM001", "KM008", "SOLTAG25" })
            {
                vm.SelectedProductCode = code;
                vm.VerificationSnapshot = VerificationSnapshot.Processing("SN", "batch");
                vm.ScanInputText = "";
                vm.ScanInputText = code + "-SN\r";

                Assert.That(vm.ScanInputText, Is.EqualTo(code + "-SN"));
                Assert.That(vm.IsYme009DebugActionEnabled, Is.True);
                Assert.That(vm.SelfCheckCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public async Task Yme009Running_BlocksSelfCheckAndDeviceInfo_ThenRestores()
        {
            var gate = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<Yme009RoundResult>();
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    gate.TrySetResult(true);
                    return await release.Task;
                });
            var adb = new Mock<IAdbAccessService>();
            List<string> ids = null;
            adb.Setup(a => a.CheckMultipleDevices(out ids)).Returns(false);
            adb.Setup(a => a.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((AdbDeviceInfoResult)null);
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out var session, out _, round.Object, adb);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = "TARGET-1";
            session.Setup(s => s.GetCurrentSessionId()).Returns("S-1");
            vm.SessionSnapshot = SessionSnapshot.Active("S-1", "ORDER", DateTime.Now);
            var running = vm.HandleScanInputAsync("SN-1");
            await gate.Task;

            Assert.That(vm.IsYme009DebugActionEnabled, Is.False);
            Assert.That(vm.SelfCheckCommand.CanExecute(null), Is.False);
            vm.SelfCheckCommand.Execute(null);
            await vm.ReadDeviceInfoForDebugAsync();
            adb.Verify(a => a.CheckMultipleDevices(out ids), Times.Never);
            adb.Verify(a => a.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()), Times.Never);

            release.TrySetResult(PassRound());
            await running;

            Assert.That(vm.IsYme009DebugActionEnabled, Is.True);
            Assert.That(vm.SelfCheckCommand.CanExecute(null), Is.True);
            vm.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => adb.Invocations.Count > 0);
            await vm.ReadDeviceInfoForDebugAsync();
            adb.Verify(a => a.CheckMultipleDevices(out ids), Times.Once);
            adb.Verify(a => a.ReadDeviceInfoAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        private static MainViewModel Prepare(
            out Mock<ISessionLifecycleService> session,
            out Mock<IStorageService> storage,
            IYme009RoundCoordinator round)
        {
            var vm = Yme009ProductAccessTests.CreateForWorkstation(out session, out storage, round);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = "TARGET-1";
            session.Setup(s => s.GetCurrentSessionId()).Returns("S-1");
            vm.SessionSnapshot = SessionSnapshot.Active("S-1", "ORDER", DateTime.Now);
            return vm;
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000)
        {
            var start = DateTime.UtcNow;
            while (!predicate())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");
                await Task.Delay(20);
            }
        }
    }
}
