/// <summary>
/// ProcessCoordinator MES Gate 挂载单元测试（Phase 2.5 冻结契约）。
/// 契约：MES_Plugin_Gate_Design_Freeze.md；TDD 覆盖 Pre-Gate / Post-Report / MesMode。
/// </summary>

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Mes.Gate;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class ProcessCoordinatorMesGateTests
    {
        private Mock<IStorageService> _storageMock;
        private Mock<IAdbAccessService> _adbMock;
        private Mock<ILoggingService> _loggingMock;
        private Mock<IMesPreCheck> _mesPreCheckMock;
        private Mock<IMesResultReporter> _mesReporterMock;
        private const string TestSessionId = "SESS_MES_GATE";
        private const int TestSessionIdInt = 1;
        private const string TestSn = "SN_MES001";
        private const string TestSnAdb = "SN_MES001";
        private VerificationSnapshot? _lastSnapshot;
        private MesEventArgs? _lastMesEvent;

        [SetUp]
        public void SetUp()
        {
            _storageMock = new Mock<IStorageService>();
            _adbMock = new Mock<IAdbAccessService>();
            _loggingMock = new Mock<ILoggingService>();
            _mesPreCheckMock = new Mock<IMesPreCheck>();
            _mesReporterMock = new Mock<IMesResultReporter>();

            _storageMock.Setup(x => x.GetInternalSessionIdBySessionNameAsync(TestSessionId)).ReturnsAsync(TestSessionIdInt);
            _storageMock.Setup(x => x.GetTestRecordBySessionAndStickerSnAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((TestRecord?)null);
            _storageMock.Setup(x => x.SaveTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);
            _storageMock.Setup(x => x.UpdateTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);
            _storageMock.Setup(x => x.IsStickerSnInPassHistoryAsync(TestSn)).ReturnsAsync(false);
            _storageMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb)).ReturnsAsync(false);
            _storageMock.Setup(x => x.IsBindingInPassHistoryAsync(TestSn)).ReturnsAsync(false);

            _lastSnapshot = null;
            _lastMesEvent = null;
        }

        private ProcessCoordinator CreateCoordinator(MesMode mesMode, IMesPreCheck? preCheck = null, IMesResultReporter? reporter = null)
        {
            var coordinator = new ProcessCoordinator(
                TestSessionId,
                _storageMock.Object,
                _adbMock.Object,
                _loggingMock.Object,
                preCheck,
                reporter,
                mesMode,
                orderId: "ORD001");
            coordinator.SnapshotChanged += (_, s) => _lastSnapshot = s;
            coordinator.MesEventOccurred += (_, e) => _lastMesEvent = e;
            return coordinator;
        }

        // ---------- Pre-Gate: MesMode Disabled ----------
        [Test]
        public async Task PreGate_MesModeDisabled_ShouldNotCallMesPreCheck()
        {
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Disabled, _mesPreCheckMock.Object, null);

            await coordinator.StartVerificationAsync(TestSn);

            _mesPreCheckMock.Verify(x => x.CheckAsync(It.IsAny<MesContext>()), Times.Never);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Pre-Gate: MesMode Enabled, Allow ----------
        [Test]
        public async Task PreGate_MesModeEnabled_Allow_ShouldContinueToVerification()
        {
            _mesPreCheckMock
                .Setup(x => x.CheckAsync(It.IsAny<MesContext>()))
                .ReturnsAsync(new MesPreCheckResult { Decision = MesPreCheckDecision.Allow });
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Enabled, _mesPreCheckMock.Object, null);

            await coordinator.StartVerificationAsync(TestSn);

            _mesPreCheckMock.Verify(x => x.CheckAsync(It.Is<MesContext>(c => c.StickerSN == TestSn && c.SessionId == TestSessionId)), Times.Once);
            Assert.That(_lastMesEvent, Is.Null);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Pre-Gate: MesMode Enabled, Reject — Phase 2.5 不阻断 ----------
        [Test]
        public async Task PreGate_MesModeEnabled_Reject_ShouldNotBlock_ShouldRaiseEventAndContinue()
        {
            _mesPreCheckMock
                .Setup(x => x.CheckAsync(It.IsAny<MesContext>()))
                .ReturnsAsync(new MesPreCheckResult { Decision = MesPreCheckDecision.Reject, Reason = "MES拒绝" });
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Enabled, _mesPreCheckMock.Object, null);

            await coordinator.StartVerificationAsync(TestSn);

            _mesPreCheckMock.Verify(x => x.CheckAsync(It.IsAny<MesContext>()), Times.Once);
            Assert.That(_lastMesEvent, Is.Not.Null);
            Assert.That(_lastMesEvent.EventType, Is.EqualTo(MesEventType.PreGateFailed));
            Assert.That(_lastMesEvent.Message, Does.Contain("MES拒绝").Or.Contain("Reject"));
            // Phase 2.5：核心检验链路仍执行，结果由 Verify 决定
            _adbMock.Verify(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Pre-Gate: MesMode Enabled, DegradedAllow ----------
        [Test]
        public async Task PreGate_MesModeEnabled_DegradedAllow_ShouldNotBlock_ShouldRaiseEventAndContinue()
        {
            _mesPreCheckMock
                .Setup(x => x.CheckAsync(It.IsAny<MesContext>()))
                .ReturnsAsync(new MesPreCheckResult { Decision = MesPreCheckDecision.DegradedAllow, Reason = "MES降级" });
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Enabled, _mesPreCheckMock.Object, null);

            await coordinator.StartVerificationAsync(TestSn);

            Assert.That(_lastMesEvent, Is.Not.Null);
            Assert.That(_lastMesEvent.EventType, Is.EqualTo(MesEventType.PreGateFailed));
            _adbMock.Verify(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Post-Report: MesMode Disabled ----------
        [Test]
        public async Task PostReport_MesModeDisabled_ShouldNotCallMesResultReporter()
        {
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Disabled, null, _mesReporterMock.Object);

            await coordinator.StartVerificationAsync(TestSn);

            await Task.Delay(150);
            _mesReporterMock.Verify(x => x.ReportTestResultAsync(It.IsAny<TestResultContext>()), Times.Never);
        }

        // ---------- Post-Report: MesMode Enabled, 成功 ----------
        [Test]
        public async Task PostReport_MesModeEnabled_ShouldCallReporterAsync()
        {
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Enabled, null, _mesReporterMock.Object);

            await coordinator.StartVerificationAsync(TestSn);
            await Task.Delay(200);

            _mesReporterMock.Verify(
                x => x.ReportTestResultAsync(It.Is<TestResultContext>(c =>
                    c.SessionId == TestSessionId && c.StickerSN == TestSn && c.Result == "PASS" && c.DeviceSN == TestSnAdb)),
                Times.Once);
        }

        // ---------- Post-Report: MesMode Enabled, 异常时触发 ReportFailed 事件 ----------
        [Test]
        public async Task PostReport_MesModeEnabled_WhenReporterThrows_ShouldRaiseReportFailedEvent()
        {
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            _mesReporterMock
                .Setup(x => x.ReportTestResultAsync(It.IsAny<TestResultContext>()))
                .ThrowsAsync(new InvalidOperationException("MES 上报异常"));
            var coordinator = CreateCoordinator(MesMode.Enabled, null, _mesReporterMock.Object);

            await coordinator.StartVerificationAsync(TestSn);
            await Task.Delay(200);

            Assert.That(_lastMesEvent, Is.Not.Null);
            Assert.That(_lastMesEvent.EventType, Is.EqualTo(MesEventType.ReportFailed));
            Assert.That(_lastMesEvent.Message, Does.Contain("不影响当前测试结果"));
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"), "Post-Report 失败不得影响本站 PASS/FAIL");
        }

        // ---------- 核心检验链路：所有 MesMode 下行为一致 ----------
        [Test]
        public async Task CoreVerificationFlow_ShouldBeUnchanged_RegardlessOfMesMode()
        {
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinatorDisabled = CreateCoordinator(MesMode.Disabled, null, null);
            await coordinatorDisabled.StartVerificationAsync(TestSn);
            var resultDisabled = _lastSnapshot?.LastResult;

            _lastSnapshot = null;
            var coordinatorEnabled = CreateCoordinator(MesMode.Enabled, null, null);
            await coordinatorEnabled.StartVerificationAsync(TestSn);
            var resultEnabled = _lastSnapshot?.LastResult;

            Assert.That(resultDisabled, Is.EqualTo("PASS"));
            Assert.That(resultEnabled, Is.EqualTo("PASS"));
        }

        // ---------- Stub 插件可替换：NoOp 不抛，行为与 Disabled 一致（仅无 Post 调用） ----------
        [Test]
        public async Task StubPlugin_NoOpPreCheckAllow_ShouldBehaveLikeAllow()
        {
            var noOpPreCheck = new NoOpMesPreCheckAllow();
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Enabled, noOpPreCheck, null);

            await coordinator.StartVerificationAsync(TestSn);

            Assert.That(_lastMesEvent, Is.Null);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Phase 3 预留：MesMode Strict，Reject 时阻断 ----------
        [Test]
        public async Task PreGate_MesModeStrict_Reject_ShouldBlockAndFail()
        {
            _mesPreCheckMock
                .Setup(x => x.CheckAsync(It.IsAny<MesContext>()))
                .ReturnsAsync(new MesPreCheckResult { Decision = MesPreCheckDecision.Reject, Reason = "前站未过" });
            var coordinator = CreateCoordinator(MesMode.Strict, _mesPreCheckMock.Object, null);

            await coordinator.StartVerificationAsync(TestSn);

            _mesPreCheckMock.Verify(x => x.CheckAsync(It.IsAny<MesContext>()), Times.Once);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot?.FailReason, Does.Contain("前站未过").Or.Contain("MES拒绝"));
            _adbMock.Verify(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        // ---------- Phase 3 预留：MesMode Strict，Allow 时继续校验 ----------
        [Test]
        public async Task PreGate_MesModeStrict_Allow_ShouldContinueToVerification()
        {
            _mesPreCheckMock
                .Setup(x => x.CheckAsync(It.IsAny<MesContext>()))
                .ReturnsAsync(new MesPreCheckResult { Decision = MesPreCheckDecision.Allow });
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Strict, _mesPreCheckMock.Object, null);

            await coordinator.StartVerificationAsync(TestSn);

            _mesPreCheckMock.Verify(x => x.CheckAsync(It.IsAny<MesContext>()), Times.Once);
            _adbMock.Verify(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Phase 3：JekeMesPlugin 挂载到 Coordinator，Disabled 不调 PreCheck ----------
        [Test]
        public async Task JekeMesPlugin_WhenMesModeDisabled_ShouldNotCallPreCheck()
        {
            var plugin = new SnVerify.Services.Mes.JekeMesPlugin();
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Disabled, plugin, plugin);

            await coordinator.StartVerificationAsync(TestSn);

            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        // ---------- Phase 3：JekeMesPlugin 挂载到 Coordinator，Enabled 调 PreCheck 与 Post-Report ----------
        [Test]
        public async Task JekeMesPlugin_WhenMesModeEnabled_ShouldCallPreCheckAndPostReport()
        {
            var plugin = new SnVerify.Services.Mes.JekeMesPlugin();
            _adbMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            var coordinator = CreateCoordinator(MesMode.Enabled, plugin, plugin);

            await coordinator.StartVerificationAsync(TestSn);
            await Task.Delay(150);

            Assert.That(_lastSnapshot?.LastResult, Is.EqualTo("PASS"));
        }

        private sealed class NoOpMesPreCheckAllow : IMesPreCheck
        {
            public Task<MesPreCheckResult> CheckAsync(MesContext context) =>
                Task.FromResult(new MesPreCheckResult { Decision = MesPreCheckDecision.Allow });
        }
    }
}
