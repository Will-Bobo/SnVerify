/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.State;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// VerificationFlowService Phase2 单元测试
    /// </summary>
    [TestFixture]
    public class VerificationFlowServicePhase2Tests
    {
        private Mock<IProcessCoordinator> _processCoordinatorMock;
        private Mock<IFileLogger> _loggerMock;
        private IVerificationFlowService _verificationFlowService;
        private const string TestBatchId = "BATCH001";
        private const string TestSn = "ABC123";

        [SetUp]
        public void SetUp()
        {
            _processCoordinatorMock = new Mock<IProcessCoordinator>();
            _loggerMock = new Mock<IFileLogger>();

            // 设置初始 Snapshot
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle(TestBatchId));

            _verificationFlowService = new VerificationFlowService(
                _processCoordinatorMock.Object,
                _loggerMock.Object);
        }

        [Test]
        public void Snapshot_ShouldReturnProcessCoordinatorSnapshot()
        {
            // Arrange
            var expectedSnapshot = VerificationSnapshot.Processing(TestSn, TestBatchId);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(expectedSnapshot);

            // Act
            var snapshot = _verificationFlowService.Snapshot;

            // Assert
            Assert.That(snapshot, Is.EqualTo(expectedSnapshot));
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public void Snapshot_ShouldIncludeBatchId()
        {
            // Assert
            Assert.That(_verificationFlowService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldDelegateToProcessCoordinator()
        {
            // Arrange
            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(Task.CompletedTask);

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            _processCoordinatorMock.Verify(x => x.StartVerificationAsync(TestSn), Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldThrowException_WhenSnIsEmpty()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _verificationFlowService.StartVerificationAsync(null));
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _verificationFlowService.StartVerificationAsync(""));
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _verificationFlowService.StartVerificationAsync("   "));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldPropagateProcessCoordinatorExceptions()
        {
            // Arrange
            var exception = new InvalidOperationException("ProcessCoordinator error");
            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .ThrowsAsync(exception);

            // Act & Assert
            var thrownException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _verificationFlowService.StartVerificationAsync(TestSn));
            Assert.That(thrownException, Is.EqualTo(exception));
        }

        [Test]
        public void Reset_ShouldDelegateToProcessCoordinator()
        {
            // Act
            _verificationFlowService.Reset();

            // Assert
            _processCoordinatorMock.Verify(x => x.Reset(), Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldMaintainBatchIdInSnapshot()
        {
            // Arrange
            var processingSnapshot = VerificationSnapshot.Processing(TestSn, TestBatchId);
            var completedSnapshot = VerificationSnapshot.Completed(TestSn, "PASS", null, TestBatchId);

            _processCoordinatorMock
                .SetupSequence(x => x.Snapshot)
                .Returns(processingSnapshot)
                .Returns(completedSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(Task.CompletedTask)
                .Callback(() =>
                {
                    // 模拟 ProcessCoordinator 更新 Snapshot
                    _processCoordinatorMock
                        .Setup(x => x.Snapshot)
                        .Returns(completedSnapshot);
                });

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(snapshot.LastResult, Is.EqualTo("PASS"));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldReflectErrorStateInSnapshot()
        {
            // Arrange
            var errorSnapshot = VerificationSnapshot.Completed(TestSn, "FAIL", "DUPLICATE_SN", TestBatchId);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(errorSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(Task.CompletedTask);

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("DUPLICATE_SN"));
        }

        [Test]
        public void Snapshot_ShouldReflectProcessingState()
        {
            // Arrange
            var processingSnapshot = VerificationSnapshot.Processing(TestSn, TestBatchId);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(processingSnapshot);

            // Act
            var snapshot = _verificationFlowService.Snapshot;

            // Assert
            Assert.That(snapshot.IsProcessing, Is.True);
            Assert.That(snapshot.CurrentSn, Is.EqualTo(TestSn));
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public void Snapshot_ShouldReflectIdleState()
        {
            // Arrange
            var idleSnapshot = VerificationSnapshot.Idle(TestBatchId);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(idleSnapshot);

            // Act
            var snapshot = _verificationFlowService.Snapshot;

            // Assert
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.CurrentSn, Is.Null);
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleTimeoutState()
        {
            // Arrange
            var timeoutSnapshot = VerificationSnapshot.Completed(TestSn, "TIMEOUT", "ADB_TIMEOUT", TestBatchId);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(timeoutSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(Task.CompletedTask);

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(snapshot.LastResult, Is.EqualTo("TIMEOUT"));
            Assert.That(snapshot.FailReason, Is.EqualTo("ADB_TIMEOUT"));
        }
    }
}
