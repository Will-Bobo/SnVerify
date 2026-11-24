using System;
using System.Collections.Generic;
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
using SnVerify.Properties;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Logging;
using SnVerify.Services.Parameter;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
using SnVerify.Services.Yme009;
using SnVerify.ViewModels;

namespace SnVerify.Tests.ViewModels
{
    [TestFixture]
    public class Yme009ProductAccessTests
    {
        private string _backupLastProjectId;
        private string _backupLastOrderId;
        private string _backupLastExportFolder;
        private string _backupLastProductCode;
        private string _backupLastExpectedAndroidVersion;
        private string _backupLastExpectedBoardVersion;
        private string _backupLastExpectedChargeBoardVersion;

        [SetUp]
        public void SetUp()
        {
            _backupLastProjectId = Settings.Default.LastProjectId;
            _backupLastOrderId = Settings.Default.LastOrderId;
            _backupLastExportFolder = Settings.Default.LastExportFolder;
            _backupLastProductCode = Settings.Default.LastProductCode;
            _backupLastExpectedAndroidVersion = Settings.Default.LastExpectedAndroidVersion;
            _backupLastExpectedBoardVersion = Settings.Default.LastExpectedBoardVersion;
            _backupLastExpectedChargeBoardVersion = Settings.Default.LastExpectedChargeBoardVersion;
            Settings.Default.LastProjectId = "";
            Settings.Default.LastOrderId = "";
            Settings.Default.LastExportFolder = "";
            Settings.Default.LastProductCode = "";
            Settings.Default.LastExpectedAndroidVersion = "";
            Settings.Default.LastExpectedBoardVersion = "";
            Settings.Default.LastExpectedChargeBoardVersion = "";
            Settings.Default.Save();
        }

        [TearDown]
        public void TearDown()
        {
            Settings.Default.LastProjectId = _backupLastProjectId ?? "";
            Settings.Default.LastOrderId = _backupLastOrderId ?? "";
            Settings.Default.LastExportFolder = _backupLastExportFolder ?? "";
            Settings.Default.LastProductCode = _backupLastProductCode ?? "";
            Settings.Default.LastExpectedAndroidVersion = _backupLastExpectedAndroidVersion ?? "";
            Settings.Default.LastExpectedBoardVersion = _backupLastExpectedBoardVersion ?? "";
            Settings.Default.LastExpectedChargeBoardVersion = _backupLastExpectedChargeBoardVersion ?? "";
            Settings.Default.Save();
        }

        [Test]
        public void Should_ExposeYme009_When_ProductListIsLoaded()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _);

            Assert.That(vm.AvailableProducts, Does.Contain(Yme009Product.Code));

            vm.SelectedProductCode = Yme009Product.Code;

            Assert.That(vm.IsPhase3Product, Is.False);
            Assert.That(vm.IsLegacyProduct, Is.False);
            Assert.That(vm.CurrentProductDisplay, Is.EqualTo("YME009 [独立轮次]"));
            Assert.That(vm.ShowAndroidVersionSection, Is.True);
            Assert.That(vm.ShowBoardVersion, Is.False);
            Assert.That(vm.ShowChargeVersion, Is.False);
            Assert.That(vm.ShowChipIdColumn, Is.False);
            Assert.That(vm.ShowWifiMac, Is.False);
        }

        [Test]
        public void Should_DisableStart_When_Yme009TargetSoftwareVersionMissing()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ExpectedAndroidVersion = " ";
            vm.ExpectedBoardVersion = "BOARD-ONLY";

            Assert.That(vm.StartBatchCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task Should_RejectStart_When_Yme009TargetSoftwareVersionMissing()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out var dialog, out var storage, out _);
            PrepareStart(vm, session, storage, "P-YME", "ORD-YME", "ORD-YME_20261123_170000");
            vm.ExpectedAndroidVersion = "";
            vm.ExpectedBoardVersion = "BOARD-ONLY";

            vm.StartBatchCommand.Execute(null);
            await Task.Delay(200);

            dialog.Verify(d => d.ShowWarning(
                It.Is<string>(s => s != null && s.Contains("目标软件版本") && !s.Contains("Board Version")),
                It.IsAny<string>()), Times.Once);
            session.Verify(s => s.CreateAndStartSession(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_CreateSessionWithYme009Identity_When_TargetSoftwareVersionProvided()
        {
            var parameter = new Mock<IParameterService>();
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out var flowFactory, parameter.Object);
            const string sessionName = "ORD-YME_20261123_170000";
            PrepareStart(vm, session, storage, "P-YME", "ORD-YME", sessionName);
            vm.ExpectedAndroidVersion = " SW-1 ";
            vm.ExpectedBoardVersion = "BOARD-SHOULD-NOT-SAVE";
            vm.ExpectedChargeBoardVersion = "CHARGE-SHOULD-NOT-SAVE";
            storage.Setup(s => s.GetInternalSessionIdBySessionNameAsync(sessionName)).ReturnsAsync(42);
            session.Setup(s => s.CreateAndStartSession("ORD-YME", "ORD-YME", "P-YME", Yme009Product.Code))
                .Callback(() => session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD-YME", DateTime.Now)))
                .Returns(sessionName);

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.SessionSnapshot != null && vm.SessionSnapshot.IsActive);

            session.Verify(s => s.CreateAndStartSession("ORD-YME", "ORD-YME", "P-YME", Yme009Product.Code), Times.Once);
            storage.Verify(s => s.GetInternalSessionIdBySessionNameAsync(sessionName), Times.Once);
            parameter.Verify(p => p.SaveParameterAsync(It.Is<VerificationParameter>(v =>
                v.SessionId == 42 &&
                v.ExpectedAndroidVersion == "SW-1" &&
                v.ExpectedBoardVersion == null &&
                v.ExpectedChargeBoardVersion == null)), Times.Once);
            flowFactory.Verify(f => f.Create(sessionName, "ORD-YME", Yme009Product.Code), Times.Once);
        }

        [Test]
        public async Task Should_RejectStart_When_OrderTargetSoftwareVersionConflicts()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out var dialog, out var storage, out _);
            PrepareStart(vm, session, storage, "P-YME", "ORD-YME", "ORD-YME_20261123_170001");
            vm.ExpectedAndroidVersion = "SW-2";
            SetupExistingVersion(storage, "P-YME", "ORD-YME", "SW-1");

            vm.StartBatchCommand.Execute(null);
            await Task.Delay(250);

            dialog.Verify(d => d.ShowWarning(
                It.Is<string>(s => s != null && s.Contains("SW-1") && s.Contains("新的订单号")),
                It.IsAny<string>()), Times.Once);
            session.Verify(s => s.CreateAndStartSession(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_CreateSession_When_OrderTargetSoftwareVersionMatches()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            const string sessionName = "ORD-YME_20261123_170002";
            PrepareStart(vm, session, storage, "P-YME", "ORD-YME", sessionName);
            vm.ExpectedAndroidVersion = "SW-1";
            SetupExistingVersion(storage, "P-YME", "ORD-YME", "SW-1");
            session.Setup(s => s.CreateAndStartSession("ORD-YME", "ORD-YME", "P-YME", Yme009Product.Code))
                .Callback(() => session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD-YME", DateTime.Now)))
                .Returns(sessionName);

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.SessionSnapshot != null && vm.SessionSnapshot.IsActive);

            session.Verify(s => s.CreateAndStartSession("ORD-YME", "ORD-YME", "P-YME", Yme009Product.Code), Times.Once);
        }

        [Test]
        public async Task Should_CreateSession_When_SameOrderNameBelongsToAnotherProject()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            const string sessionName = "ORD-YME_20261123_170003";
            PrepareStart(vm, session, storage, "P-YME", "ORD-YME", sessionName);
            vm.ExpectedAndroidVersion = "SW-2";
            storage.Setup(s => s.GetAllProductsAsync()).ReturnsAsync(new List<Product>
            {
                new Product { Id = 1, ProductName = "P-KM", ProductCode = "KM001" }
            });
            storage.Setup(s => s.GetAllOrdersAsync()).ReturnsAsync(new List<Order>
            {
                new Order { Id = 10, OrderName = "ORD-YME", ProductId = 1 }
            });
            storage.Setup(s => s.GetSessionsByOrderIdAsync(10)).ReturnsAsync(new List<TestSession>
            {
                new TestSession { Id = 100, OrderId = 10, SessionName = "OLD" }
            });
            storage.Setup(s => s.GetVerificationParameterAsync(100)).ReturnsAsync(new VerificationParameter
            {
                SessionId = 100,
                ExpectedAndroidVersion = "SW-1"
            });
            session.Setup(s => s.CreateAndStartSession("ORD-YME", "ORD-YME", "P-YME", Yme009Product.Code))
                .Callback(() => session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD-YME", DateTime.Now)))
                .Returns(sessionName);

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.SessionSnapshot != null && vm.SessionSnapshot.IsActive);

            session.Verify(s => s.CreateAndStartSession("ORD-YME", "ORD-YME", "P-YME", Yme009Product.Code), Times.Once);
        }

        [Test]
        public async Task Should_EnterYme009Mount_When_ScanDuringActiveSession()
        {
            var flow = new Mock<IVerificationFlowService>();
            flow.Setup(f => f.Snapshot).Returns(VerificationSnapshot.Idle());
            var logging = new Mock<ILoggingService>();
            logging.Setup(l => l.Snapshot).Returns(LoggingSnapshot.Idle());
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow.Object, logging.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);
            vm.ScanInputText = "SN-YME-1";

            await vm.HandleScanInputAsync("SN-YME-1");

            flow.Verify(f => f.StartPhase3VerificationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            flow.Verify(f => f.StartVerificationAsync(It.IsAny<string>()), Times.Never);
            logging.Verify(l => l.LogInfo(It.Is<string>(s => s != null && s.Contains("专属轮次") && s.Contains("SN-YME-1"))), Times.Once);
            Assert.That(vm.StatusBarMessage, Does.Contain("YME009").And.Contain("检测入口"));
            Assert.That(vm.ScanInputText, Is.Empty);
            Assert.That(vm.IsProcessing, Is.False);
            Assert.That(vm.LastResult, Is.Empty);
        }

        [Test]
        public async Task Should_ProjectYme009Inspection_When_RoundIsMounted()
        {
            var round = new Mock<IYme009RoundCoordinator>();
            var access = new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty);
            var summary = "扫描SN=SN-YME-3" + Environment.NewLine + "综合=PASS" + Environment.NewLine + "记录已保存 Id=9";
            var decision = Yme009InspectionDecision.Judged(
                new Yme009ParsedAttributes(
                    new Yme009ParsedField(Yme009FieldKind.DeviceSn, Yme009ReadStatus.Ok, "DEV-3"),
                    new Yme009ParsedField(Yme009FieldKind.SoftwareVersion, Yme009ReadStatus.Ok, "SW-1"),
                    new Yme009ParsedField(Yme009FieldKind.Imei1, Yme009ReadStatus.Ok, "111111111111111"),
                    new Yme009ParsedField(Yme009FieldKind.Imei2, Yme009ReadStatus.Ok, "222222222222222"),
                    new Yme009ParsedField(Yme009FieldKind.WifiMac, Yme009ReadStatus.Ok, "AA:BB:CC:DD:EE:FF"),
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
            var result = new Yme009RoundResult(access, Yme009CloseAttempt.NotAttempted(), true, summary, true, 9, decision);
            MainViewModel vm = null;
            round.Setup(r => r.RunAsync("SN-YME-3", It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Assert.That(vm.IsProcessing, Is.True);
                    return Task.FromResult(result);
                });
            var flow = new Mock<IVerificationFlowService>();
            flow.Setup(f => f.Snapshot).Returns(VerificationSnapshot.Idle());
            vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _, null, flow.Object, null, round.Object);
            session.Setup(s => s.GetCurrentSessionId()).Returns("S1");
            storage.Setup(s => s.GetInternalSessionIdBySessionNameAsync("S1")).ReturnsAsync(9);
            vm.ExpectedAndroidVersion = "SW-1";
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);

            await vm.HandleScanInputAsync("SN-YME-3");

            round.Verify(r => r.UseInspection(It.Is<Yme009InspectionRequest>(item => item.SessionId == 9 && item.TargetSoftwareVersion == "SW-1")), Times.Once);
            round.Verify(r => r.RunAsync("SN-YME-3", It.IsAny<CancellationToken>()), Times.Once);
            flow.Verify(f => f.StartPhase3VerificationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            flow.Verify(f => f.StartVerificationAsync(It.IsAny<string>()), Times.Never);
            Assert.That(vm.Yme009RoundSummary, Is.EqualTo(summary));
            Assert.That(vm.Yme009RoundPhase, Is.EqualTo(Yme009RoundPhase.Completed));
            Assert.That(vm.Yme009OverallText, Is.EqualTo("PASS"));
            Assert.That(vm.StatusBarMessage, Is.EqualTo("PASS"));
            Assert.That(vm.Yme009SaveStatusText, Is.EqualTo(Yme009WorkstationText.Saved));
            Assert.That(vm.IsProcessing, Is.False);
            Assert.That(vm.LastResult, Is.Empty);
            Assert.That(vm.ScanInputText, Is.Empty);
        }

        [Test]
        public async Task Should_NotCallProcessScanOrLegacy_When_Yme009ScanUsesRealFlowService()
        {
            var coordinator = new Mock<IProcessCoordinator>();
            coordinator.Setup(c => c.Snapshot).Returns(VerificationSnapshot.Idle());
            var flow = new VerificationFlowService(coordinator.Object);
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);

            await vm.HandleScanInputAsync("SN-YME-2");

            coordinator.Verify(c => c.ProcessScanAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            coordinator.Verify(c => c.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_CallProcessScanAsync_When_Km001ScanUsesRealFlowService()
        {
            var coordinator = new Mock<IProcessCoordinator>();
            coordinator.Setup(c => c.Snapshot).Returns(VerificationSnapshot.Idle());
            var flow = new VerificationFlowService(coordinator.Object);
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow);
            vm.SelectedProductCode = "KM001";
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-KM", DateTime.Now);

            await vm.HandleScanInputAsync("SN-KM-1");

            coordinator.Verify(c => c.ProcessScanAsync("SN-KM-1", "KM001"), Times.Once);
            coordinator.Verify(c => c.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_CallStartVerificationAsync_When_Soltag25ScanUsesRealFlowService()
        {
            var coordinator = new Mock<IProcessCoordinator>();
            coordinator.Setup(c => c.Snapshot).Returns(VerificationSnapshot.Idle());
            var flow = new VerificationFlowService(coordinator.Object);
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow);
            vm.SelectedProductCode = "SOLTAG25";
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-SOL", DateTime.Now);

            await vm.HandleScanInputAsync("SN-SOL-1");

            coordinator.Verify(c => c.StartVerificationAsync("SN-SOL-1"), Times.Once);
            coordinator.Verify(c => c.ProcessScanAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_CallProcessScanAsync_When_Km008ScanUsesRealFlowService()
        {
            var coordinator = new Mock<IProcessCoordinator>();
            coordinator.Setup(c => c.Snapshot).Returns(VerificationSnapshot.Idle());
            var flow = new VerificationFlowService(coordinator.Object);
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow);
            vm.SelectedProductCode = "KM008";
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-KM8", DateTime.Now);

            await vm.HandleScanInputAsync("SN-KM8-1");

            coordinator.Verify(c => c.ProcessScanAsync("SN-KM8-1", "KM008"), Times.Once);
            coordinator.Verify(c => c.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_IgnoreScan_When_Yme009SessionIsNotActive()
        {
            var flow = new Mock<IVerificationFlowService>();
            flow.Setup(f => f.Snapshot).Returns(VerificationSnapshot.Idle());
            var logging = new Mock<ILoggingService>();
            logging.Setup(l => l.Snapshot).Returns(LoggingSnapshot.Idle());
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow.Object, logging.Object);
            vm.SelectedProductCode = Yme009Product.Code;

            await vm.HandleScanInputAsync("SN-YME-3");

            flow.Verify(f => f.StartPhase3VerificationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            flow.Verify(f => f.StartVerificationAsync(It.IsAny<string>()), Times.Never);
            logging.Verify(l => l.LogInfo(It.Is<string>(s => s != null && s.Contains("专属轮次"))), Times.Never);
        }

        [Test]
        public async Task Should_IgnoreScan_When_Yme009IsProcessing()
        {
            var flow = new Mock<IVerificationFlowService>();
            flow.Setup(f => f.Snapshot).Returns(VerificationSnapshot.Idle());
            var logging = new Mock<ILoggingService>();
            logging.Setup(l => l.Snapshot).Returns(LoggingSnapshot.Idle());
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow.Object, logging.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);
            vm.VerificationSnapshot = VerificationSnapshot.Processing("BUSY");

            await vm.HandleScanInputAsync("SN-YME-4");

            flow.Verify(f => f.StartPhase3VerificationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            flow.Verify(f => f.StartVerificationAsync(It.IsAny<string>()), Times.Never);
            logging.Verify(l => l.LogInfo(It.Is<string>(s => s != null && s.Contains("专属轮次"))), Times.Never);
        }

        [Test]
        public async Task Should_IgnoreScan_When_Yme009InputIsEmpty()
        {
            var flow = new Mock<IVerificationFlowService>();
            flow.Setup(f => f.Snapshot).Returns(VerificationSnapshot.Idle());
            var vm = CreateViewModel(new ProductRegistryAdapter(), out _, out _, out _, out _, null, flow.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);

            await vm.HandleScanInputAsync("   ");

            flow.Verify(f => f.StartPhase3VerificationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            flow.Verify(f => f.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_NotEnterLegacyOrPhase3_When_IndependentProductIsNotYme009()
        {
            var registry = new Mock<IProductRegistry>();
            registry.Setup(r => r.GetProductCodes()).Returns(new[] { "OTHER" });
            registry.Setup(r => r.Get("OTHER")).Returns(new ProductProfile
            {
                ProductCode = "OTHER",
                Mode = VerificationMode.Independent
            });
            var flow = new Mock<IVerificationFlowService>();
            flow.Setup(f => f.Snapshot).Returns(VerificationSnapshot.Idle());
            var vm = CreateViewModel(registry.Object, out _, out _, out _, out _, null, flow.Object);
            vm.SelectedProductCode = "OTHER";
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD", DateTime.Now);

            await vm.HandleScanInputAsync("SN-OTHER");

            flow.Verify(f => f.StartPhase3VerificationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            flow.Verify(f => f.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_ReloadSavedRows_When_RecordIsSaved()
        {
            var saved = SessionRow(1, "SN-1", new DateTime(2026, 11, 23, 12, 0, 0));
            var added = SessionRow(2, "SN-1", new DateTime(2026, 11, 23, 12, 0, 1));
            var calls = 0;
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync("SN-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Yme009RoundResult(
                    new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                    Yme009CloseAttempt.NotAttempted(),
                    true,
                    "综合=FAIL",
                    true,
                    2));
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _, null, null, null, round.Object);
            session.Setup(s => s.GetCurrentSessionId()).Returns("S1");
            storage.Setup(s => s.GetInternalSessionIdBySessionNameAsync("S1")).ReturnsAsync(9);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S1")).Returns(() =>
            {
                calls++;
                IReadOnlyList<TestRecord> rows = calls == 1
                    ? new List<TestRecord> { saved }
                    : new List<TestRecord> { saved, added };
                return Task.FromResult(rows);
            });
            vm.ExpectedAndroidVersion = "SW-1";
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ProjectIdInput = "P-YME";
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);
            await WaitUntilAsync(() => vm.Yme009SessionRecords.Count == 1);

            await vm.HandleScanInputAsync("SN-1");

            Assert.That(calls, Is.EqualTo(2));
            Assert.That(vm.Yme009SessionRecords.Count, Is.EqualTo(2));
            Assert.That(vm.Yme009SessionRecords[1].Id, Is.EqualTo(2));
            Assert.That(vm.Yme009SessionRecords[1].StickerSn, Is.EqualTo("SN-1"));
            Assert.That(vm.Yme009PassCount, Is.EqualTo(2));
            Assert.That(vm.Yme009BusinessFailCount, Is.EqualTo(0));
            Assert.That(vm.Yme009ReadExceptionCount, Is.EqualTo(0));
            Assert.That(vm.Yme009PassRateText, Is.EqualTo("100.00%"));
            Assert.That(vm.Yme009RoundSummary, Is.EqualTo("综合=FAIL"));
            Assert.That(vm.Yme009SessionRecordHeader, Does.Contain("会话=S1").And.Contain("项目=P-YME").And.Contain("订单=ORD-YME"));
        }

        [Test]
        public async Task Should_KeepSavedRows_When_RecordIsNotSaved()
        {
            var saved = SessionRow(1, "SN-OLD", new DateTime(2026, 11, 23, 12, 0, 0));
            var calls = 0;
            var round = new Mock<IYme009RoundCoordinator>();
            round.Setup(r => r.RunAsync("SN-NEW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Yme009RoundResult(
                    new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                    Yme009CloseAttempt.NotAttempted(),
                    true,
                    "记录未保存",
                    false,
                    0));
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _, null, null, null, round.Object);
            session.Setup(s => s.GetCurrentSessionId()).Returns("S1");
            storage.Setup(s => s.GetInternalSessionIdBySessionNameAsync("S1")).ReturnsAsync(9);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S1")).Returns(() =>
            {
                calls++;
                return Task.FromResult((IReadOnlyList<TestRecord>)new List<TestRecord> { saved });
            });
            vm.ExpectedAndroidVersion = "SW-1";
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);
            await WaitUntilAsync(() => vm.Yme009SessionRecords.Count == 1);

            await vm.HandleScanInputAsync("SN-NEW");

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(vm.Yme009SessionRecords.Count, Is.EqualTo(1));
            Assert.That(vm.Yme009SessionRecords[0].StickerSn, Is.EqualTo("SN-OLD"));
            Assert.That(vm.Yme009PassCount, Is.EqualTo(1));
            Assert.That(vm.Yme009PassRateText, Is.EqualTo("100.00%"));
            Assert.That(vm.Yme009RoundSummary, Is.EqualTo("记录未保存"));
        }

        [Test]
        public async Task Should_IgnoreLateSessionQuery_When_NewerSessionIsShown()
        {
            var oldGate = new TaskCompletionSource<IReadOnlyList<TestRecord>>();
            var newer = SessionRow(8, "SN-NEW", new DateTime(2026, 11, 23, 12, 1, 0), "FAIL", Yme009FailReasonCodes.CitFail);
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            session.Setup(s => s.GetCurrentSessionId()).Returns("SESS-A");
            storage.Setup(s => s.GetTestRecordsBySessionAsync("SESS-A")).Returns(() => oldGate.Task);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("SESS-B"))
                .ReturnsAsync(new List<TestRecord> { newer });
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ProjectIdInput = "P-YME";
            vm.SessionSnapshot = SessionSnapshot.Active("SESS-A", "ORD-A", DateTime.Now);

            session.Setup(s => s.GetCurrentSessionId()).Returns("SESS-B");
            vm.SessionSnapshot = SessionSnapshot.Active("SESS-B", "ORD-B", DateTime.Now);
            await WaitUntilAsync(() => vm.Yme009SessionRecords.Count == 1 && vm.Yme009SessionRecords[0].Id == 8);

            oldGate.SetResult(new List<TestRecord> { SessionRow(1, "SN-OLD", new DateTime(2026, 11, 23, 12, 0, 0)) });
            await Task.Delay(50);

            Assert.That(vm.Yme009SessionRecords.Count, Is.EqualTo(1));
            Assert.That(vm.Yme009SessionRecords[0].StickerSn, Is.EqualTo("SN-NEW"));
            Assert.That(vm.Yme009PassCount, Is.EqualTo(0));
            Assert.That(vm.Yme009BusinessFailCount, Is.EqualTo(1));
            Assert.That(vm.Yme009PassRateText, Is.EqualTo("0.00%"));
            Assert.That(vm.Yme009SessionRecordHeader, Does.Contain("会话=SESS-B").And.Contain("订单=ORD-B"));
            Assert.That(vm.Yme009SessionRecordHeader, Does.Not.Contain("SESS-A"));
        }

        [Test]
        public async Task Should_DropLateQuery_When_RecordListWasCleared()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<TestRecord>>();
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            session.Setup(s => s.GetCurrentSessionId()).Returns("SESS-A");
            storage.Setup(s => s.GetTestRecordsBySessionAsync("SESS-A")).Returns(() => gate.Task);
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("SESS-A", "ORD-A", DateTime.Now);
            await Task.Delay(30);

            session.Setup(s => s.GetCurrentSessionId()).Returns((string)null);
            vm.SessionSnapshot = SessionSnapshot.Idle();
            vm.SelectedProductCode = "KM001";
            gate.SetResult(new List<TestRecord> { SessionRow(1, "SN-OLD", new DateTime(2026, 11, 23, 12, 0, 0)) });
            await Task.Delay(50);

            Assert.That(vm.ShowYme009SessionRecords, Is.False);
            Assert.That(vm.Yme009SessionRecords, Is.Empty);
            Assert.That(vm.Yme009SessionRecordHeader, Is.Empty);
            Assert.That(vm.Yme009PassCount, Is.EqualTo(0));
            Assert.That(vm.Yme009BusinessFailCount, Is.EqualTo(0));
            Assert.That(vm.Yme009ReadExceptionCount, Is.EqualTo(0));
            Assert.That(vm.Yme009PassRateText, Is.Empty);
        }

        [Test]
        public void Should_HideSessionRecords_When_ProductIsNotYme009()
        {
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S1"))
                .ReturnsAsync(new List<TestRecord> { SessionRow(1, "SN-KM", new DateTime(2026, 11, 23, 12, 0, 0)) });
            session.Setup(s => s.GetCurrentSessionId()).Returns("S1");
            vm.SelectedProductCode = "KM001";
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-KM", DateTime.Now);

            Assert.That(vm.ShowYme009SessionRecords, Is.False);
            Assert.That(vm.ShowYme009SessionRecordEmpty, Is.False);
            Assert.That(vm.Yme009SessionRecords, Is.Empty);
            Assert.That(vm.Yme009PassCount, Is.EqualTo(0));
            Assert.That(vm.Yme009PassRateText, Is.Empty);
            storage.Verify(s => s.GetTestRecordsBySessionAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_ApplyStatisticsFromTheSameQuery_When_RecordsReload()
        {
            var pass = SessionRow(1, "SN-1", new DateTime(2026, 11, 24, 10, 0, 0));
            var business = SessionRow(2, "SN-1", new DateTime(2026, 11, 24, 10, 0, 1), "FAIL", Yme009FailReasonCodes.SnMismatch);
            var read = SessionRow(3, "SN-2", new DateTime(2026, 11, 24, 10, 0, 2), "FAIL", Yme009FailReasonCodes.ReadFail);
            read.CitJudgment = "Fail";
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            session.Setup(s => s.GetCurrentSessionId()).Returns("S1");
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S1"))
                .ReturnsAsync(new List<TestRecord> { pass, business, read });
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);
            await WaitUntilAsync(() => vm.Yme009SessionRecords.Count == 3);

            Assert.That(vm.Yme009SessionRecords[0].Id, Is.EqualTo(1));
            Assert.That(vm.Yme009SessionRecords[1].Id, Is.EqualTo(2));
            Assert.That(vm.Yme009SessionRecords[2].Id, Is.EqualTo(3));
            Assert.That(vm.Yme009PassCount, Is.EqualTo(1));
            Assert.That(vm.Yme009BusinessFailCount, Is.EqualTo(1));
            Assert.That(vm.Yme009ReadExceptionCount, Is.EqualTo(1));
            Assert.That(vm.Yme009PassRateText, Is.EqualTo("50.00%"));
        }

        [Test]
        public async Task Should_KeepStatistics_When_RecordQueryFails()
        {
            var saved = SessionRow(1, "SN-OLD", new DateTime(2026, 11, 24, 10, 0, 0));
            var calls = 0;
            var vm = CreateViewModel(new ProductRegistryAdapter(), out var session, out _, out var storage, out _);
            session.Setup(s => s.GetCurrentSessionId()).Returns("S1");
            storage.Setup(s => s.GetTestRecordsBySessionAsync("S1")).Returns(() =>
            {
                calls++;
                if (calls == 1)
                    return Task.FromResult((IReadOnlyList<TestRecord>)new List<TestRecord> { saved });
                throw new InvalidOperationException("查询失败");
            });
            vm.SelectedProductCode = Yme009Product.Code;
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now);
            await WaitUntilAsync(() => vm.Yme009PassCount == 1);

            vm.SessionSnapshot = SessionSnapshot.Active("S1", "ORD-YME", DateTime.Now.AddSeconds(1));
            await Task.Delay(50);

            Assert.That(calls, Is.GreaterThanOrEqualTo(2));
            Assert.That(vm.Yme009SessionRecords.Count, Is.EqualTo(1));
            Assert.That(vm.Yme009SessionRecords[0].StickerSn, Is.EqualTo("SN-OLD"));
            Assert.That(vm.Yme009PassCount, Is.EqualTo(1));
            Assert.That(vm.Yme009PassRateText, Is.EqualTo("100.00%"));
        }

        private static TestRecord SessionRow(int id, string stickerSn, DateTime verifyTime, string result = "PASS", string failReason = null)
        {
            return new TestRecord
            {
                Id = id,
                SessionId = 14,
                StickerSN = stickerSn,
                Result = result,
                FailReason = failReason,
                VerifyTime = verifyTime,
                SnJudgment = "Pass",
                VersionJudgment = "Pass",
                CitJudgment = "Pass",
                AgingRebootJudgment = "Pass",
                AgingResetJudgment = "Pass"
            };
        }

        private static void PrepareStart(
            MainViewModel vm,
            Mock<ISessionLifecycleService> session,
            Mock<IStorageService> storage,
            string projectId,
            string orderId,
            string sessionName)
        {
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ProjectIdInput = projectId;
            vm.OrderIdInput = orderId;
            storage.Setup(s => s.ProjectNameExistsAsync(projectId)).ReturnsAsync(false);
            session.Setup(s => s.CreateAndStartSession(orderId, orderId, projectId, Yme009Product.Code)).Returns(sessionName);
        }

        private static void SetupExistingVersion(Mock<IStorageService> storage, string projectName, string orderName, string version)
        {
            storage.Setup(s => s.GetAllProductsAsync()).ReturnsAsync(new List<Product>
            {
                new Product { Id = 7, ProductName = projectName, ProductCode = Yme009Product.Code }
            });
            storage.Setup(s => s.GetAllOrdersAsync()).ReturnsAsync(new List<Order>
            {
                new Order { Id = 8, OrderName = orderName, ProductId = 7 }
            });
            storage.Setup(s => s.GetSessionsByOrderIdAsync(8)).ReturnsAsync(new List<TestSession>
            {
                new TestSession { Id = 9, OrderId = 8, SessionName = "OLD" }
            });
            storage.Setup(s => s.GetVerificationParameterAsync(9)).ReturnsAsync(new VerificationParameter
            {
                SessionId = 9,
                ExpectedAndroidVersion = version
            });
        }

        public static MainViewModel CreateForWorkstation(
            out Mock<ISessionLifecycleService> session,
            IYme009RoundCoordinator round = null)
        {
            return CreateForWorkstation(out session, out _, round, null);
        }

        public static MainViewModel CreateForWorkstation(
            out Mock<ISessionLifecycleService> session,
            out Mock<IStorageService> storage,
            IYme009RoundCoordinator round = null,
            Mock<IAdbAccessService> adb = null)
        {
            return CreateViewModel(
                new ProductRegistryAdapter(),
                out session,
                out _,
                out storage,
                out _,
                null,
                null,
                null,
                round,
                adb?.Object);
        }

        private static MainViewModel CreateViewModel(
            IProductRegistry registry,
            out Mock<ISessionLifecycleService> session,
            out Mock<IUserDialogService> dialog,
            out Mock<IStorageService> storage,
            out Mock<IVerificationFlowServiceFactory> flowFactory,
            IParameterService parameterService = null,
            IVerificationFlowService flowService = null,
            ILoggingService loggingService = null,
            IYme009RoundCoordinator yme009RoundCoordinator = null,
            IAdbAccessService adbAccessService = null)
        {
            session = new Mock<ISessionLifecycleService>();
            dialog = new Mock<IUserDialogService>();
            storage = new Mock<IStorageService>();
            flowFactory = new Mock<IVerificationFlowServiceFactory>();
            var flow = flowService ?? Mock.Of<IVerificationFlowService>(f => f.Snapshot == VerificationSnapshot.Idle());
            var logging = loggingService ?? Mock.Of<ILoggingService>(l => l.Snapshot == LoggingSnapshot.Idle());
            var versionFlow = new Mock<IVersionVerificationFlowService>();

            session.Setup(s => s.Snapshot).Returns(SessionSnapshot.Idle());
            versionFlow.Setup(v => v.Snapshot).Returns(VerificationSnapshot.Idle());
            flowFactory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(flow);
            storage.Setup(s => s.ProjectNameExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            storage.Setup(s => s.GetAllProductsAsync()).ReturnsAsync(new List<Product>());
            storage.Setup(s => s.GetAllOrdersAsync()).ReturnsAsync(new List<Order>());
            storage.Setup(s => s.GetTestRecordsBySessionAsync(It.IsAny<string>())).ReturnsAsync(new List<TestRecord>());

            return new MainViewModel(
                session.Object,
                flowFactory.Object,
                logging,
                storage.Object,
                adbAccessService ?? Mock.Of<IAdbAccessService>(),
                Mock.Of<IExportAggregationService>(),
                CreatePassingValidator(),
                dialog.Object,
                versionFlow.Object,
                Path.GetTempPath(),
                registry,
                parameterService,
                Mock.Of<IDeviceAccessService>(),
                yme009RoundCoordinator);
        }

        private static IOrderNameValidator CreatePassingValidator()
        {
            var validator = new Mock<IOrderNameValidator>();
            string message = null;
            validator.Setup(v => v.Validate(It.IsAny<string>(), out message)).Returns(true);
            return validator.Object;
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000, int pollMs = 20)
        {
            var start = DateTime.UtcNow;
            while (!predicate())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");
                await Task.Delay(pollMs);
            }
        }
    }
}
