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
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));

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
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(snAdb));

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
            Assert.That(_lastSnapshot.FailReason, Is.Not.Null.And.Contains("MISMATCH"));

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" && r.FailReason.Contains("MISMATCH"))),
                Times.Once);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldFail_WhenDuplicateSn()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, TestSnScan))
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
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("DUPLICATE_SN"));

            // 验证未调用 ADB
            _adbAccessServiceMock.Verify(
                x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()),
                Times.Never);

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" && r.FailReason == "DUPLICATE_SN")),
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
            _storageServiceMock
                .Setup(x => x.IsSnDuplicateInPassAsync(TestBatchId, snScan))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(snAdb));

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
            Assert.That(_lastSnapshot.FailReason, Is.Not.Null.And.Contains("MISMATCH"));
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
