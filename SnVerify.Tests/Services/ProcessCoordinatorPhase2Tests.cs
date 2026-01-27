/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ProcessCoordinator Phase2 单元测试
    /// </summary>
    [TestFixture]
    public class ProcessCoordinatorPhase2Tests
    {
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;
        private IProcessCoordinator _processCoordinator;
        private const string TestBatchId = "BATCH001";
        private const string TestSnScan = "ABC123";
        private const string TestSnAdb = "ABC123";
        private VerificationSnapshot _lastSnapshot;

        [SetUp]
        public void SetUp()
        {
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();

            _processCoordinator = new ProcessCoordinator(
                TestBatchId,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object);

            _processCoordinator.SnapshotChanged += (sender, snapshot) =>
            {
                _lastSnapshot = snapshot;
            };
        }

        [Test]
        public void Snapshot_ShouldContainBatchId()
        {
            // Assert
            Assert.That(_processCoordinator.Snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeBatchIdInSnapshot()
        {
            // Arrange
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

            // 新决策树逻辑：检查历史绑定，都不存在才能 PASS
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan, TestSnAdb))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_processCoordinator.Snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeBatchIdInProcessingState()
        {
            // Arrange
            var tcs = new TaskCompletionSource<AdbSnReadResult>();
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            // 新决策树逻辑：检查历史绑定，都不存在才能 PASS
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan, TestSnAdb))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            var task = _processCoordinator.StartVerificationAsync(TestSnScan);
            await Task.Delay(50); // 等待 Processing 状态

            // Assert
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.IsProcessing, Is.True);
            Assert.That(_lastSnapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_lastSnapshot.CurrentSn, Is.EqualTo(TestSnScan));

            // 完成流程
            tcs.SetResult(AdbSnReadResult.Success(TestSnAdb));
            await task;
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeBatchIdInCompletedState()
        {
            // Arrange
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

            // 新决策树逻辑：检查历史绑定，都不存在才能 PASS
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan, TestSnAdb))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(TestSnAdb), "快照应包含设备SN");
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeBatchIdInErrorState()
        {
            // Arrange
            // 新决策树逻辑：绑定一致，但存在历史 PASS 绑定 → FAIL（规则2：AlreadyPassed）
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb))
                .ReturnsAsync(false);
            // 绑定关系已存在 → AlreadyPassed
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan, TestSnAdb))
                .ReturnsAsync(true);

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, TestSnScan))
                .ReturnsAsync((SnVerifyResult)null); // 不存在 FAIL 记录，创建新记录

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN已存在"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(TestSnScan), "快照应包含设备SN");
        }

        [Test]
        public void Reset_ShouldPreserveBatchId()
        {
            // Arrange
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

            // 新决策树逻辑：检查历史绑定，都不存在才能 PASS
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan, TestSnAdb))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            var task = _processCoordinator.StartVerificationAsync(TestSnScan);
            task.Wait();
            _processCoordinator.Reset();

            // Assert
            var snapshot = _processCoordinator.Snapshot;
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.CurrentSn, Is.Null);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldMaintainBatchIdAcrossMultipleCalls()
        {
            // Arrange
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

            // 新决策树逻辑：检查历史绑定，都不存在才能 PASS
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync("SN001");
            await _processCoordinator.StartVerificationAsync("SN002");

            // Assert
            Assert.That(_processCoordinator.Snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeBatchIdInTimeoutState()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Failure("Timeout", true));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, TestSnScan))
                .ReturnsAsync((SnVerifyResult)null); // 不存在 FAIL 记录，创建新记录

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("TIMEOUT"));
            Assert.That(_lastSnapshot.DeviceSN, Is.Null, "ADB超时时设备SN应为null");
        }
    }
}
