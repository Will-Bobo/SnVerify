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
    /// ProcessCoordinator 单元测试
    /// </summary>
    [TestFixture]
    public class ProcessCoordinatorTests
    {
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;
        private IProcessCoordinator _processCoordinator;
        private const string TestBatchId = "BATCH001";
        private const string TestSnScan = "ABC123";
        private const string TestSnAdb = "ABC123";
        private VerificationSnapshot _lastSnapshot;
        private int _snapshotChangedCount;

        [SetUp]
        public void SetUp()
        {
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();
            _lastSnapshot = null;
            _snapshotChangedCount = 0;

            // 设置批次存在
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(true);

            _processCoordinator = new ProcessCoordinator(
                TestBatchId,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object);

            _processCoordinator.SnapshotChanged += (sender, snapshot) =>
            {
                _lastSnapshot = snapshot;
                _snapshotChangedCount++;
            };
        }

        [Test]
        public async Task StartVerificationAsync_ShouldCompleteSuccessfully_WhenSnMatches()
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
                .Setup(x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.BatchId == TestBatchId &&
                    r.SN == TestSnScan &&
                    r.Result == "PASS")))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(_lastSnapshot.CurrentSn, Is.EqualTo(TestSnScan));
            Assert.That(_snapshotChangedCount, Is.GreaterThan(0));

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "PASS" && r.FailReason == null)),
                Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldFail_WhenSnMismatch()
        {
            // Arrange
            var snAdb = "XYZ789";
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(snAdb));

            // 新决策树逻辑：绑定不一致，且双方均无历史 PASS 绑定 → FAIL（规则5：Sticker_Device_Mismatch）
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(snAdb))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, TestSnScan))
                .ReturnsAsync((SnVerifyResult)null); // 不存在 FAIL 记录，创建新记录

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("Sticker_Device_Mismatch"));

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" && r.FailReason == "Sticker_Device_Mismatch")),
                Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldFail_WhenDuplicateSn()
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
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("AlreadyPassed"));

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" && r.FailReason == "AlreadyPassed")),
                Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleTimeout()
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
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("TIMEOUT"));

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "TIMEOUT")),
                Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleAdbFailure()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Failure("ADB command failed"));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, TestSnScan))
                .ReturnsAsync((SnVerifyResult)null); // 不存在 FAIL 记录，创建新记录

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.Not.Null);

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL")),
                Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldLockDuringProcessing()
        {
            // Arrange
            var tcs = new TaskCompletionSource<AdbSnReadResult>();
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, It.IsAny<string>()))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            // Act - 启动第一个流程（会阻塞）
            var task1 = _processCoordinator.StartVerificationAsync(TestSnScan);

            // 等待一小段时间确保流程已开始
            await Task.Delay(50);

            // 尝试启动第二个流程（应该被忽略）
            var task2 = _processCoordinator.StartVerificationAsync("XYZ789");

            // 完成第一个流程
            tcs.SetResult(AdbSnReadResult.Success(TestSnAdb));

            await task1;
            await task2;

            // Assert - 第二个流程应该被忽略（因为第一个还在处理）
            // 验证只保存了一次结果
            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()),
                Times.Once);
        }

        [Test]
        public void Reset_ShouldClearState()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

            // Act - 先启动一个流程
            var task = _processCoordinator.StartVerificationAsync(TestSnScan);
            task.Wait();

            // 重置
            _processCoordinator.Reset();

            // Assert
            var snapshot = _processCoordinator.Snapshot;
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.CurrentSn, Is.Null);
            Assert.That(snapshot.LastResult, Is.Null);
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldUpdateSnapshotToProcessing()
        {
            // Arrange
            var tcs = new TaskCompletionSource<AdbSnReadResult>();
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            // Act
            var task = _processCoordinator.StartVerificationAsync(TestSnScan);

            // 等待一小段时间
            await Task.Delay(50);

            // Assert - 应该有一个 Processing 状态的快照
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.IsProcessing, Is.True);
            Assert.That(_lastSnapshot.CurrentSn, Is.EqualTo(TestSnScan));

            // 完成流程
            tcs.SetResult(AdbSnReadResult.Success(TestSnAdb));
            await task;
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleCaseSensitiveComparison()
        {
            // Arrange
            var snScan = "abc123";
            var snAdb = "ABC123";
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(snAdb));

            // 新决策树逻辑：绑定不一致（区分大小写），且双方均无历史 PASS 绑定 → FAIL（规则5：Sticker_Device_Mismatch）
            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(snScan))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(snAdb))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, snScan))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(snScan);

            // Assert - 应该不匹配（区分大小写）
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("Sticker_Device_Mismatch"));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldHandleEmptyAdbSn()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(""));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, TestSnScan))
                .ReturnsAsync((SnVerifyResult)null); // 不存在 FAIL 记录，创建新记录

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.Not.Null);
        }
    }
}
