/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.State;
using SnVerify.Services.Coordination;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// VerificationFlowService 单元测试
    /// </summary>
    [TestFixture]
    public class VerificationFlowServiceTests
    {
        private Mock<IProcessCoordinator> _processCoordinatorMock;
        private IVerificationFlowService _verificationFlowService;
        private const string TestSn = "ABC123";

        [SetUp]
        public void SetUp()
        {
            _processCoordinatorMock = new Mock<IProcessCoordinator>();

            // 设置初始快照
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            _verificationFlowService = new VerificationFlowService(_processCoordinatorMock.Object);
        }

        [Test]
        public void Snapshot_ShouldReturnCoordinatorSnapshot()
        {
            // Arrange
            var expectedSnapshot = VerificationSnapshot.Processing(TestSn);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(expectedSnapshot);

            // Act
            var snapshot = _verificationFlowService.Snapshot;

            // Assert
            Assert.That(snapshot, Is.EqualTo(expectedSnapshot));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldDelegateToCoordinator()
        {
            // Arrange
            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(Task.CompletedTask);

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            _processCoordinatorMock.Verify(
                x => x.StartVerificationAsync(TestSn),
                Times.Once);
        }

        [Test]
        public void Reset_ShouldDelegateToCoordinator()
        {
            // Act
            _verificationFlowService.Reset();

            // Assert
            _processCoordinatorMock.Verify(x => x.Reset(), Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldUpdateSnapshot_WhenCoordinatorUpdates()
        {
            // Arrange
            var processingSnapshot = VerificationSnapshot.Processing(TestSn);
            var completedSnapshot = VerificationSnapshot.Completed(TestSn, "PASS", null, null, TestSn);

            // 设置初始快照为 Idle
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(async () =>
                {
                    // 在回调中动态更新 Snapshot 返回值为 processingSnapshot
                    _processCoordinatorMock
                        .Setup(x => x.Snapshot)
                        .Returns(processingSnapshot);
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        processingSnapshot);
                    await Task.Delay(10);
                    
                    // 在回调中动态更新 Snapshot 返回值为 completedSnapshot
                    _processCoordinatorMock
                        .Setup(x => x.Snapshot)
                        .Returns(completedSnapshot);
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        completedSnapshot);
                });

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var finalSnapshot = _verificationFlowService.Snapshot;
            Assert.That(finalSnapshot.IsProcessing, Is.False);
            Assert.That(finalSnapshot.LastResult, Is.EqualTo("PASS"));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleFailure()
        {
            // Arrange
            var completedSnapshot = VerificationSnapshot.Completed(TestSn, "FAIL", "MISMATCH", null, "DEVICE_SN");
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(completedSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(async () =>
                {
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        completedSnapshot);
                    await Task.CompletedTask;
                });

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("MISMATCH"));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleTimeout()
        {
            // Arrange
            var timeoutSnapshot = VerificationSnapshot.Completed(TestSn, "TIMEOUT", "ADB_TIMEOUT", null, null);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(timeoutSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(async () =>
                {
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        timeoutSnapshot);
                    await Task.CompletedTask;
                });

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("TIMEOUT"));
            Assert.That(snapshot.FailReason, Is.EqualTo("ADB_TIMEOUT"));
        }

        [Test]
        public void Reset_ShouldUpdateSnapshotToIdle()
        {
            // Arrange
            var processingSnapshot = VerificationSnapshot.Processing(TestSn);
            var idleSnapshot = VerificationSnapshot.Idle();

            // 设置初始快照为 processing
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(processingSnapshot);

            _processCoordinatorMock
                .Setup(x => x.Reset())
                .Callback(() =>
                {
                    // 在回调中动态更新 Snapshot 返回值
                    _processCoordinatorMock
                        .Setup(x => x.Snapshot)
                        .Returns(idleSnapshot);
                    
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        idleSnapshot);
                });

            // Act
            _verificationFlowService.Reset();

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.CurrentSn, Is.Null);
            Assert.That(snapshot.LastResult, Is.Null);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleDuplicateSn()
        {
            // Arrange
            var duplicateSnapshot = VerificationSnapshot.Completed(TestSn, "FAIL", "DUPLICATE_SN", null, null);
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(duplicateSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(async () =>
                {
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        duplicateSnapshot);
                    await Task.CompletedTask;
                });

            // Act
            await _verificationFlowService.StartVerificationAsync(TestSn);

            // Assert
            var snapshot = _verificationFlowService.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("DUPLICATE_SN"));
        }

        [Test]
        public void Snapshot_ShouldBeReadOnly()
        {
            // Arrange
            var snapshot1 = VerificationSnapshot.Processing(TestSn);
            var snapshot2 = VerificationSnapshot.Completed(TestSn, "PASS", null, null, TestSn);

            _processCoordinatorMock
                .SetupSequence(x => x.Snapshot)
                .Returns(snapshot1)
                .Returns(snapshot2);

            // Act
            var firstSnapshot = _verificationFlowService.Snapshot;
            var secondSnapshot = _verificationFlowService.Snapshot;

            // Assert - 快照应该是不可变的
            Assert.That(firstSnapshot.IsProcessing, Is.True);
            Assert.That(secondSnapshot.IsProcessing, Is.False);
            Assert.That(firstSnapshot, Is.Not.EqualTo(secondSnapshot));
        }
    }
}
