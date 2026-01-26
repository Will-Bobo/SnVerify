/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Batch;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.MES;
using SnVerify.Services.Storage;
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
        private Mock<IBatchManager> _batchManagerMock;
        private Mock<IVerificationFlowServiceFactory> _flowServiceFactoryMock;
        private Mock<IVerificationFlowService> _verificationFlowServiceMock;
        private Mock<ILoggingService> _loggingServiceMock;
        private Mock<IMESInterface> _mesInterfaceMock;
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;

        [SetUp]
        public void SetUp()
        {
            _batchManagerMock = new Mock<IBatchManager>();
            _flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            _verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            _loggingServiceMock = new Mock<ILoggingService>();
            _mesInterfaceMock = new Mock<IMESInterface>();
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();

            _batchManagerMock.Setup(m => m.Snapshot).Returns(BatchSnapshot.Idle());
            _verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            _flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns(_verificationFlowServiceMock.Object);
            _loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());
            _mesInterfaceMock.Setup(m => m.Snapshot).Returns(MESSnapshot.Idle());

            _viewModel = new MainViewModel(
                _batchManagerMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _mesInterfaceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                Path.GetTempPath());
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
                .Returns(VerificationSnapshot.Completed("TEST_SN", "PASS"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("PASS"));
        }

        [Test]
        public void StatusText_ShouldReturnFAIL_WhenResultIsFAIL()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("TEST_SN", "FAIL", "MISMATCH"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("FAIL"));
            Assert.That(_viewModel.ShowFailReason, Is.True);
            Assert.That(_viewModel.FailReason, Is.EqualTo("MISMATCH"));
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldTriggerVerification_WhenValidInput()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var activeBatchSnapshot = BatchSnapshot.Active("BATCH001", "Batch 001", DateTime.Now);
            _batchManagerMock.Setup(m => m.Snapshot).Returns(activeBatchSnapshot);
            _viewModel.BatchSnapshot = activeBatchSnapshot; // 更新 ViewModel 的 BatchSnapshot
            
            _verificationFlowServiceMock.SetupSequence(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(testSn))
                .Returns(VerificationSnapshot.Completed(testSn, "PASS"));

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
            _batchManagerMock.Setup(m => m.Snapshot).Returns(BatchSnapshot.Active("BATCH001", "Batch 001", DateTime.Now));
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Processing("ANOTHER_SN"));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenBatchNotActive()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            _batchManagerMock.Setup(m => m.Snapshot).Returns(BatchSnapshot.Idle());

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
            var activeBatchSnapshot = BatchSnapshot.Active("BATCH001", "Batch 001", DateTime.Now);
            _batchManagerMock.Setup(m => m.Snapshot).Returns(activeBatchSnapshot);
            _viewModel.BatchSnapshot = activeBatchSnapshot; // 更新 ViewModel 的 BatchSnapshot
            _viewModel.ScanInputText = testSn;
            
            _verificationFlowServiceMock.SetupSequence(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(testSn))
                .Returns(VerificationSnapshot.Completed(testSn, "PASS"));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            Assert.That(_viewModel.ScanInputText, Is.Empty);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreEmptyInput()
        {
            // Arrange
            _batchManagerMock.Setup(m => m.Snapshot).Returns(BatchSnapshot.Active("BATCH001", "Batch 001", DateTime.Now));

            // Act
            await _viewModel.HandleScanInputAsync("");
            await _viewModel.HandleScanInputAsync("   ");

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
