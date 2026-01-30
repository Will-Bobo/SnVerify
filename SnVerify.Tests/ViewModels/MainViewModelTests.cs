/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Domain.Validation;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Mes.Gate;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
using SnVerify.ViewModels;

namespace SnVerify.Tests.ViewModels
{
    /// <summary>
    /// MainViewModel 单元测试（UI 可运行闭环阶段）
    /// </summary>
    [TestFixture]
    public class MainViewModelTests
    {
        private MainViewModel _viewModel;
        private Mock<ISessionLifecycleService> _sessionLifecycleServiceMock;
        private Mock<IVerificationFlowServiceFactory> _flowServiceFactoryMock;
        private Mock<IVerificationFlowService> _verificationFlowServiceMock;
        private Mock<ILoggingService> _loggingServiceMock;
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;
        private Mock<IExportAggregationService> _exportAggregationServiceMock;
        private Mock<IOrderNameValidator> _orderNameValidatorMock;
        private Mock<IUserDialogService> _dialogServiceMock;

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 1500, int pollMs = 20)
        {
            var start = DateTime.UtcNow;
            while (!predicate())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");
                await Task.Delay(pollMs);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            _flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            _verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            _loggingServiceMock = new Mock<ILoggingService>();
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();
            _exportAggregationServiceMock = new Mock<IExportAggregationService>();
            _orderNameValidatorMock = new Mock<IOrderNameValidator>();
            _dialogServiceMock = new Mock<IUserDialogService>();

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            _verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            _flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>())).Returns(_verificationFlowServiceMock.Object);
            _loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());

            _viewModel = new MainViewModel(
                _sessionLifecycleServiceMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _exportAggregationServiceMock.Object,
                _orderNameValidatorMock.Object,
                _dialogServiceMock.Object,
                Path.GetTempPath());
        }

        [Test]
        public void Constructor_WhenLogDirectoryIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new MainViewModel(
                    _sessionLifecycleServiceMock.Object,
                    _flowServiceFactoryMock.Object,
                    _loggingServiceMock.Object,
                    _storageServiceMock.Object,
                    _adbAccessServiceMock.Object,
                    _exportAggregationServiceMock.Object,
                    _orderNameValidatorMock.Object,
                    _dialogServiceMock.Object,
                    null));
        }

        [Test]
        public async Task EndBatchCommand_WhenNoTestRecordGenerated_ShouldStillEndSession_AndLogIgnoredMessage()
        {
            // Arrange：无记录时仍会结束 Session/日志，仅状态栏短暂提示并写「结束测试被忽略」日志
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(activeSessionSnapshot);
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = activeSessionSnapshot;

            _storageServiceMock
                .Setup(s => s.GetTestRecordsBySessionAsync(sessionId))
                .ReturnsAsync(Array.Empty<TestRecord>());

            // Act
            _viewModel.EndBatchCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.StatusBarMessage == "", timeoutMs: 2000);

            // Assert：无记录也执行结束流程，并记录「结束测试被忽略」日志
            _sessionLifecycleServiceMock.Verify(m => m.EndSession(), Times.Once);
            _loggingServiceMock.Verify(m => m.EndBatch(), Times.Once);
            _loggingServiceMock.Verify(m => m.LogInfo(It.Is<string>(s => s != null && s.Contains("结束测试被忽略"))), Times.Once);
        }

        [Test]
        public void StartBatchCommand_ShouldBeDisabled_WhenIsProcessing()
        {
            // Arrange
            _viewModel.VerificationSnapshot = VerificationSnapshot.Processing("SN");

            // Act
            var canExecute = _viewModel.StartBatchCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void EndBatchCommand_ShouldBeDisabled_WhenIsProcessing()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.VerificationSnapshot = VerificationSnapshot.Processing("SN");

            // Act
            var canExecute = _viewModel.EndBatchCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void StatusText_ShouldReturnWaiting_Initially()
        {
            Assert.That(_viewModel.StatusText, Is.EqualTo("等待检验"));
        }

        [Test]
        public void StatusText_ShouldReturnProcessing_WhenIsProcessing()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Processing("TEST_SN"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("正在检验..."));
        }

        [Test]
        public void StatusText_ShouldReturnPASS_WhenResultIsPASS()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("TEST_SN", "PASS", null, null, "TEST_SN"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("PASS"));
            Assert.That(_viewModel.DeviceSN, Is.EqualTo("TEST_SN"));
        }

        [Test]
        public void StatusText_ShouldReturnFAIL_WhenResultIsFAIL()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("TEST_SN", "FAIL", "MISMATCH", null, "DEVICE_SN"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("FAIL"));
            Assert.That(_viewModel.ShowFailReason, Is.True);
            Assert.That(_viewModel.FailReason, Is.EqualTo("MISMATCH"));
            Assert.That(_viewModel.DeviceSN, Is.EqualTo("DEVICE_SN"));
        }

        [Test]
        public void DeviceSN_ShouldReturnEmpty_WhenSnapshotIsNull()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.DeviceSN, Is.EqualTo(""));
        }

        [Test]
        public void DeviceSN_ShouldReturnValue_WhenCompletedWithDeviceSN()
        {
            // Arrange
            const string deviceSN = "DEVICE123";
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("STICKER123", "PASS", null, null, deviceSN));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.DeviceSN, Is.EqualTo(deviceSN));
            Assert.That(_viewModel.CurrentSn, Is.EqualTo("STICKER123"));
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldTriggerVerification_WhenValidInput()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(activeSessionSnapshot);
            _viewModel.SessionSnapshot = activeSessionSnapshot;
            
            _verificationFlowServiceMock.SetupSequence(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(testSn))
                .Returns(VerificationSnapshot.Completed(testSn, "PASS", null, null, testSn));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(testSn), Times.Once);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenIsProcessing()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Processing("ANOTHER_SN"));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenSessionNotActive()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldClearScanInputText_AfterCompletion()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(activeSessionSnapshot);
            _viewModel.SessionSnapshot = activeSessionSnapshot;
            _viewModel.ScanInputText = testSn;
            
            _verificationFlowServiceMock.SetupSequence(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(testSn))
                .Returns(VerificationSnapshot.Completed(testSn, "PASS", null, null, testSn));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            Assert.That(_viewModel.ScanInputText, Is.Empty);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreEmptyInput()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));

            // Act
            await _viewModel.HandleScanInputAsync("");
            await _viewModel.HandleScanInputAsync("   ");

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenIsSelfChecking()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _viewModel.SessionSnapshot = _sessionLifecycleServiceMock.Object.Snapshot;

            var tcs = new TaskCompletionSource<AdbSnReadResult>();
            _adbAccessServiceMock.Setup(m => m.ReadDeviceSnAsync(default)).Returns(tcs.Task);
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny))
                .Returns(false);

            // Act: start self-check and wait until VM enters self-checking state
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.IsSelfChecking);

            await _viewModel.HandleScanInputAsync(testSn);

            // Assert: should not trigger verification while self-checking
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);

            // Cleanup: end self-check
            tcs.SetResult(AdbSnReadResult.Success("DEVICE_SN"));
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking);
        }

        [Test]
        public async Task Commands_ShouldBeDisabled_WhenIsSelfChecking()
        {
            // Arrange
            var tcs = new TaskCompletionSource<AdbSnReadResult>();
            _adbAccessServiceMock.Setup(m => m.ReadDeviceSnAsync(default)).Returns(tcs.Task);
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny))
                .Returns(false);

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.IsSelfChecking);

            // Assert
            Assert.That(_viewModel.StartVerifyCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.StartBatchCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.EndBatchCommand.CanExecute(null), Is.False);
            // 新逻辑：导出仅在“开始测试→结束测试”期间禁用；自检期间（未开始测试）仍可导出
            Assert.That(_viewModel.ExportCommand.CanExecute(null), Is.True);

            // Cleanup
            tcs.SetResult(AdbSnReadResult.Success("DEVICE_SN"));
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking);
        }

        [Test]
        public async Task StatusBarMessage_ShouldUpdate_WhenMesPostReportFailedEventRaised()
        {
            // Arrange
            var args = new MesEventArgs(MesEventType.ReportFailed, "MES 上报失败（不影响当前测试结果）", "S1", "O1");

            // Act
            _verificationFlowServiceMock.Raise(m => m.MesEventOccurred += null, args);

            // Assert
            await WaitUntilAsync(() => _viewModel.StatusBarMessage == "MES 上报失败（不影响当前测试结果）");
        }

        [Test]
        public async Task StartBatchCommand_ShouldCreateSession_WhenProjectIdAndOrderIdProvided()
        {
            // Arrange
            var projectId = "PROJECT001";
            var orderId = "ORDER001";
            var sessionId = "ORDER001_20250126_143000";
            var startTime = DateTime.Now;

            string validationMessage = null;
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);
            _sessionLifecycleServiceMock
                .Setup(s => s.CreateAndStartSession(orderId, orderId, projectId))
                .Returns(sessionId);
            _sessionLifecycleServiceMock
                .SetupSequence(s => s.Snapshot)
                .Returns(SessionSnapshot.Idle())
                .Returns(SessionSnapshot.Active(sessionId, orderId, startTime));
            _flowServiceFactoryMock
                .Setup(f => f.Create(sessionId, orderId))
                .Returns(_verificationFlowServiceMock.Object);

            // Act
            _viewModel.ProjectIdInput = projectId;
            _viewModel.OrderIdInput = orderId;
            _viewModel.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.SessionSnapshot.IsActive, timeoutMs: 2000);

            // Assert
            _sessionLifecycleServiceMock.Verify(s => s.CreateAndStartSession(orderId, orderId, projectId), Times.Once);
            _flowServiceFactoryMock.Verify(f => f.Create(sessionId, orderId), Times.Once);
            _loggingServiceMock.Verify(l => l.StartSession(sessionId), Times.Once);
        }

        [Test]
        public void Commands_ShouldBeDisabled_WhenSessionIsActive()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);

            // Act
            _viewModel.SessionSnapshot = activeSessionSnapshot;

            // Assert
            Assert.That(_viewModel.StartBatchCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.EndBatchCommand.CanExecute(null), Is.True);
            Assert.That(_viewModel.ExportCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task ExportAsync_WhenTargetZipNotExists_ShouldExportWithoutOverwritePrompt()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderX", ProductId = 1 };

                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                var tcs = new TaskCompletionSource<bool>();
                _exportAggregationServiceMock
                    .Setup(s => s.ExportByOrderIdAsync("OrderX", exportRoot))
                    .Returns(() =>
                    {
                        tcs.TrySetResult(true);
                        return Task.CompletedTask;
                    });

                // Act
                _viewModel.ExportCommand.Execute(null);
                await WaitUntilAsync(() => tcs.Task.IsCompleted);

                // Assert
                _dialogServiceMock.Verify(d => d.ConfirmOverwrite(It.IsAny<string>()), Times.Never);
                _exportAggregationServiceMock.Verify(s => s.ExportByOrderIdAsync("OrderX", exportRoot), Times.Once);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }

        [Test]
        public async Task ExportAsync_WhenTargetZipExists_AndUserCancelsOverwrite_ShouldNotExportAndLogInfo()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);
            var zipPath = Path.Combine(exportRoot, "OrderY.zip");
            File.WriteAllText(zipPath, "ORIGINAL");

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderY", ProductId = 1 };

                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                _dialogServiceMock.Setup(d => d.ConfirmOverwrite(It.IsAny<string>()))
                    .Returns(false);

                // Act
                _viewModel.ExportCommand.Execute(null);
                // 等待一小段时间让异步导出逻辑完成
                await Task.Delay(100);

                // Assert
                _exportAggregationServiceMock.Verify(s => s.ExportByOrderIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
                _loggingServiceMock.Verify(l => l.LogInfo(It.Is<string>(m => m.Contains("导出已取消"))), Times.AtLeastOnce);
                Assert.That(File.Exists(zipPath), Is.True, "当用户取消覆盖时，原 ZIP 文件应保持不变");
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }

        [Test]
        public async Task ExportAsync_WhenTargetZipExists_AndUserConfirmsOverwrite_ShouldDeleteAndExport()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);
            var zipPath = Path.Combine(exportRoot, "OrderZ.zip");
            File.WriteAllText(zipPath, "ORIGINAL");

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderZ", ProductId = 1 };

                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                _dialogServiceMock.Setup(d => d.ConfirmOverwrite(It.IsAny<string>()))
                    .Returns(true);

                var tcs = new TaskCompletionSource<bool>();
                _exportAggregationServiceMock
                    .Setup(s => s.ExportByOrderIdAsync("OrderZ", exportRoot))
                    .Callback(() =>
                    {
                        // 在调用导出服务之前，ZIP 应已被删除
                        Assert.That(File.Exists(zipPath), Is.False);
                    })
                    .Returns(() =>
                    {
                        tcs.TrySetResult(true);
                        return Task.CompletedTask;
                    });

                // Act
                _viewModel.ExportCommand.Execute(null);
                await WaitUntilAsync(() => tcs.Task.IsCompleted);

                // Assert
                _dialogServiceMock.Verify(d => d.ConfirmOverwrite(It.IsAny<string>()), Times.Once);
                _exportAggregationServiceMock.Verify(s => s.ExportByOrderIdAsync("OrderZ", exportRoot), Times.Once);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }
    }
}
