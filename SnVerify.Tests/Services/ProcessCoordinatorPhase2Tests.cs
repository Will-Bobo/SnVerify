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
        private const string TestSessionId = "BATCH001"; // Phase 2.5 以 SessionId 为入口
        private const int TestSessionIdInt = 1;
        private const string TestSnScan = "ABC123";
        private const string TestSnAdb = "ABC123";
        private VerificationSnapshot _lastSnapshot;

        [SetUp]
        public void SetUp()
        {
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();
            _storageServiceMock.Setup(x => x.GetInternalSessionIdBySessionNameAsync(TestSessionId)).ReturnsAsync(TestSessionIdInt);
            _storageServiceMock.Setup(x => x.GetTestRecordBySessionAndStickerSnAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((TestRecord)null);
            _storageServiceMock.Setup(x => x.SaveTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);
            _storageServiceMock.Setup(x => x.UpdateTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);

            _processCoordinator = new ProcessCoordinator(
                TestSessionId,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object);

            _processCoordinator.SnapshotChanged += (sender, snapshot) =>
            {
                _lastSnapshot = snapshot;
            };
        }

        [Test]
        public void Snapshot_ShouldContainSessionId()
        {
            // Assert - Phase 2.5 以 SessionId 为入口
            Assert.That(_processCoordinator.Snapshot.SessionId, Is.EqualTo(TestSessionId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeSessionIdInSnapshot()
        {
            // Arrange
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            _storageServiceMock.Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert - Phase 2.5 SessionId
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.SessionId, Is.EqualTo(TestSessionId));
            Assert.That(_processCoordinator.Snapshot.SessionId, Is.EqualTo(TestSessionId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeSessionIdInProcessingState()
        {
            // Arrange
            var tcs = new TaskCompletionSource<AdbSnReadResult>();
            _adbAccessServiceMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).Returns(tcs.Task);
            _storageServiceMock.Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);

            // Act
            var task = _processCoordinator.StartVerificationAsync(TestSnScan);
            await Task.Delay(50); // 等待 Processing 状态

            // Assert - Phase 2.5 SessionId
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.IsProcessing, Is.True);
            Assert.That(_lastSnapshot.SessionId, Is.EqualTo(TestSessionId));
            Assert.That(_lastSnapshot.CurrentSn, Is.EqualTo(TestSnScan));

            tcs.SetResult(AdbSnReadResult.Success(TestSnAdb));
            await task;
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeSessionIdInCompletedState()
        {
            // Arrange
            _adbAccessServiceMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            _storageServiceMock.Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert - Phase 2.5 SessionId
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.IsProcessing, Is.False);
            Assert.That(_lastSnapshot.SessionId, Is.EqualTo(TestSessionId));
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(TestSnAdb), "快照应包含设备SN");
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeSessionIdInErrorState()
        {
            // Arrange - 规则2：绑定一致，但存在历史 PASS 绑定 → FAIL
            _adbAccessServiceMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            _storageServiceMock.Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan)).ReturnsAsync(true);

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert - Phase 2.5 SessionId
            Assert.That(_lastSnapshot, Is.Not.Null);
            Assert.That(_lastSnapshot.SessionId, Is.EqualTo(TestSessionId));
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN已存在"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(TestSnScan), "快照应包含设备SN");
        }

        [Test]
        public void Reset_ShouldPreserveSessionId()
        {
            // Arrange
            _adbAccessServiceMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            _storageServiceMock.Setup(x => x.IsStickerSnInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(TestSnAdb)).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsBindingInPassHistoryAsync(TestSnScan)).ReturnsAsync(false);

            // Act
            var task = _processCoordinator.StartVerificationAsync(TestSnScan);
            task.Wait();
            _processCoordinator.Reset();

            // Assert - Phase 2.5 SessionId
            var snapshot = _processCoordinator.Snapshot;
            Assert.That(snapshot.SessionId, Is.EqualTo(TestSessionId));
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.CurrentSn, Is.Null);
        }

        [Test]
        public async Task StartVerificationAsync_ShouldMaintainSessionIdAcrossMultipleCalls()
        {
            // Arrange
            _adbAccessServiceMock.Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdbSnReadResult.Success(TestSnAdb));
            _storageServiceMock.Setup(x => x.IsStickerSnInPassHistoryAsync(It.IsAny<string>())).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsDeviceSnInPassHistoryAsync(It.IsAny<string>())).ReturnsAsync(false);
            _storageServiceMock.Setup(x => x.IsBindingInPassHistoryAsync(It.IsAny<string>())).ReturnsAsync(false);

            // Act
            await _processCoordinator.StartVerificationAsync("SN001");
            await _processCoordinator.StartVerificationAsync("SN002");

            // Assert - Phase 2.5 SessionId
            Assert.That(_processCoordinator.Snapshot.SessionId, Is.EqualTo(TestSessionId));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldIncludeSessionIdInTimeoutState()
        {
            // Arrange
            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Failure("Timeout", true));

            // Act
            await _processCoordinator.StartVerificationAsync(TestSnScan);

            // Assert - Phase 2.5 SessionId
            Assert.That(_lastSnapshot.SessionId, Is.EqualTo(TestSessionId));
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("TIMEOUT"));
            Assert.That(_lastSnapshot.DeviceSN, Is.Null, "ADB超时时设备SN应为null");
        }
    }
}
