/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.State;
using SnVerify.Services.Coordination;
using SnVerify.Services.Input;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ScanInputService Phase2 单元测试
    /// </summary>
    [TestFixture]
    public class ScanInputServicePhase2Tests
    {
        private Mock<IProcessCoordinator> _processCoordinatorMock;
        private IScanInputService _scanInputService;
        private const string TestSn = "ABC123";
        private const string TestBatchId = "BATCH001";
        private ScanSnapshot _lastSnapshot;
        private int _snapshotChangedCount;

        [SetUp]
        public void SetUp()
        {
            _processCoordinatorMock = new Mock<IProcessCoordinator>();
            _lastSnapshot = null;
            _snapshotChangedCount = 0;

            // 设置 ProcessCoordinator 初始状态
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            _scanInputService = new ScanInputService(_processCoordinatorMock.Object, TestBatchId);

            // 订阅快照变化（如果接口支持）
            // 注意：这里假设 ScanInputService 有快照变化事件或属性
        }

        [Test]
        public void Snapshot_ShouldReturnIdleState_Initially()
        {
            // Act
            var snapshot = _scanInputService.Snapshot;

            // Assert
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.LastScanSN, Is.Null);
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task OnScanInput_ShouldTriggerProcessCoordinator_WhenNotProcessing()
        {
            // Arrange
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(Task.CompletedTask);

            // Act
            await _scanInputService.OnScanInputAsync(TestSn);

            // Assert
            _processCoordinatorMock.Verify(
                x => x.StartVerificationAsync(TestSn),
                Times.Once);

            var snapshot = _scanInputService.Snapshot;
            Assert.That(snapshot.LastScanSN, Is.EqualTo(TestSn));
        }

        [Test]
        public async Task OnScanInput_ShouldNotTrigger_WhenAlreadyProcessing()
        {
            // Arrange
            _processCoordinatorMock
                .SetupSequence(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(TestSn));

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(async () =>
                {
                    // 模拟处理中状态
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        VerificationSnapshot.Processing(TestSn));
                    await Task.Delay(10);
                });

            // Act - 第一次调用
            var task1 = _scanInputService.OnScanInputAsync(TestSn);

            // 等待一小段时间确保流程已开始
            await Task.Delay(50);

            // 第二次调用（应该被忽略）
            await _scanInputService.OnScanInputAsync("XYZ789");

            await task1;

            // Assert - 应该只触发一次
            _processCoordinatorMock.Verify(
                x => x.StartVerificationAsync(It.IsAny<string>()),
                Times.Once);
        }

        [Test]
        public async Task OnScanInput_ShouldHandleEmptySn()
        {
            // Act
            await _scanInputService.OnScanInputAsync("");

            // Assert
            var snapshot = _scanInputService.Snapshot;
            Assert.That(snapshot.ErrorMessage, Is.Not.Null);
            Assert.That(snapshot.ErrorMessage, Does.Contain("empty"));

            // 不应该触发 ProcessCoordinator
            _processCoordinatorMock.Verify(
                x => x.StartVerificationAsync(It.IsAny<string>()),
                Times.Never);
        }

        [Test]
        public async Task OnScanInput_ShouldHandleWhitespaceOnly()
        {
            // Act
            await _scanInputService.OnScanInputAsync("   ");

            // Assert
            var snapshot = _scanInputService.Snapshot;
            Assert.That(snapshot.ErrorMessage, Is.Not.Null);

            _processCoordinatorMock.Verify(
                x => x.StartVerificationAsync(It.IsAny<string>()),
                Times.Never);
        }

        [Test]
        public void Reset_ShouldClearState()
        {
            // Arrange
            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            _processCoordinatorMock
                .Setup(x => x.Reset())
                .Callback(() =>
                {
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        VerificationSnapshot.Idle());
                });

            // Act
            _scanInputService.Reset();

            // Assert
            var snapshot = _scanInputService.Snapshot;
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.LastScanSN, Is.Null);
            Assert.That(snapshot.ErrorMessage, Is.Null);

            _processCoordinatorMock.Verify(x => x.Reset(), Times.Once);
        }

        [Test]
        public async Task OnScanInput_ShouldUpdateSnapshot_WhenProcessCoordinatorUpdates()
        {
            // Arrange
            var processingSnapshot = VerificationSnapshot.Processing(TestSn);
            var completedSnapshot = VerificationSnapshot.Completed(TestSn, "PASS");

            _processCoordinatorMock
                .SetupSequence(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(processingSnapshot)
                .Returns(completedSnapshot);

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(TestSn))
                .Returns(async () =>
                {
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        processingSnapshot);
                    await Task.Delay(10);
                    _processCoordinatorMock.Raise(
                        x => x.SnapshotChanged += null,
                        this,
                        completedSnapshot);
                });

            // Act
            await _scanInputService.OnScanInputAsync(TestSn);

            // Assert
            var snapshot = _scanInputService.Snapshot;
            Assert.That(snapshot.LastScanSN, Is.EqualTo(TestSn));
        }

        [Test]
        public async Task OnScanInput_ShouldTrimAndPreserveCase()
        {
            // Arrange
            var inputSn = "  abc123  ";
            var expectedSn = "abc123"; // 只去除空格，保留大小写

            _processCoordinatorMock
                .Setup(x => x.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            _processCoordinatorMock
                .Setup(x => x.StartVerificationAsync(expectedSn))
                .Returns(Task.CompletedTask);

            // Act
            await _scanInputService.OnScanInputAsync(inputSn);

            // Assert
            _processCoordinatorMock.Verify(
                x => x.StartVerificationAsync(expectedSn),
                Times.Once);
        }
    }
}
