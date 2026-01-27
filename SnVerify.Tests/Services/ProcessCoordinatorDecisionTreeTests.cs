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
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ProcessCoordinator 决策树校验逻辑单元测试（基于 SN_Sticker_Device_Relation_Rules.md）
    /// </summary>
    [TestFixture]
    public class ProcessCoordinatorDecisionTreeTests
    {
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;
        private Mock<ILoggingService> _loggingServiceMock;
        private IProcessCoordinator _processCoordinator;
        private const string TestBatchId = "BATCH001";
        private VerificationSnapshot _lastSnapshot;

        [SetUp]
        public void SetUp()
        {
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();
            _loggingServiceMock = new Mock<ILoggingService>();

            _processCoordinator = new ProcessCoordinator(
                TestBatchId,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _loggingServiceMock.Object);

            _processCoordinator.SnapshotChanged += (sender, snapshot) =>
            {
                _lastSnapshot = snapshot;
            };
        }

        #region 规则 1：绑定一致，且无历史 PASS 绑定 → PASS

        [Test]
        public async Task StartVerificationAsync_Rule1_ShouldPass_WhenBindingMatchesAndNoHistory()
        {
            // Arrange - 规则 1：StickerSN == DeviceSN，且都不在历史 PASS 中
            const string stickerSN = "SN001";
            const string deviceSN = "SN001";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(deviceSN))
                .ReturnsAsync(false);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(stickerSN, deviceSN))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(deviceSN));

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(_lastSnapshot.FailReason, Is.Null);
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(deviceSN), "快照应包含设备SN");

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "PASS" &&
                    r.SN == stickerSN &&
                    r.DeviceSN == deviceSN &&
                    r.FailReason == null)),
                Times.Once);
        }

        #endregion

        #region 规则 2：绑定一致，但存在历史 PASS 绑定 → FAIL（已出站）

        [Test]
        public async Task StartVerificationAsync_Rule2_ShouldFail_WhenBindingMatchesButExistsInHistory()
        {
            // Arrange - 规则 2：StickerSN == DeviceSN，但绑定关系在历史 PASS 中
            const string stickerSN = "SN002";
            const string deviceSN = "SN002";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(true); // StickerSN 在历史 PASS 中
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(stickerSN, deviceSN))
                .ReturnsAsync(true); // 绑定关系在历史 PASS 中

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(deviceSN));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN已存在"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(deviceSN), "快照应包含设备SN");

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" &&
                    r.SN == stickerSN &&
                    r.DeviceSN == deviceSN &&
                    r.FailReason == "设备SN已存在")),
                Times.Once);
        }

        #endregion

        #region 规则 3：绑定不一致，StickerSN 已存在于历史 PASS 绑定中 → FAIL（贴纸重复）

        [Test]
        public async Task StartVerificationAsync_Rule3_ShouldFail_WhenStickerSnExistsInHistory()
        {
            // Arrange - 规则 3：StickerSN != DeviceSN，StickerSN 在历史 PASS 中
            const string stickerSN = "SN003";
            const string deviceSN = "SN999";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(true); // StickerSN 在历史 PASS 中

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(deviceSN));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN 与 条形码SN [不匹配]，并且 条形码SN 已存在"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(deviceSN), "快照应包含设备SN");

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" &&
                    r.SN == stickerSN &&
                    r.DeviceSN == deviceSN &&
                    r.FailReason == "设备SN 与 条形码SN [不匹配]，并且 条形码SN 已存在")),
                Times.Once);
        }

        #endregion

        #region 规则 4：绑定不一致，DeviceSN 已存在于历史 PASS 绑定中 → FAIL（设备已出站）

        [Test]
        public async Task StartVerificationAsync_Rule4_ShouldFail_WhenDeviceSnExistsInHistory()
        {
            // Arrange - 规则 4：StickerSN != DeviceSN，DeviceSN 在历史 PASS 中
            const string stickerSN = "SN004";
            const string deviceSN = "SN888";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(false); // StickerSN 不在历史 PASS 中
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(deviceSN))
                .ReturnsAsync(true); // DeviceSN 在历史 PASS 中

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(deviceSN));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN 与 条形码SN [不匹配]，并且 设备SN 已存在"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(deviceSN), "快照应包含设备SN");

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" &&
                    r.SN == stickerSN &&
                    r.DeviceSN == deviceSN &&
                    r.FailReason == "设备SN 与 条形码SN [不匹配]，并且 设备SN 已存在")),
                Times.Once);
        }

        #endregion

        #region 规则 5：绑定不一致，且双方均无历史 PASS 绑定 → FAIL（包装不一致）

        [Test]
        public async Task StartVerificationAsync_Rule5_ShouldFail_WhenBindingMismatchAndNoHistory()
        {
            // Arrange - 规则 5：StickerSN != DeviceSN，且都不在历史 PASS 中
            const string stickerSN = "SN005";
            const string deviceSN = "SN777";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(false); // StickerSN 不在历史 PASS 中
            _storageServiceMock
                .Setup(x => x.IsDeviceSnInPassHistoryAsync(deviceSN))
                .ReturnsAsync(false); // DeviceSN 不在历史 PASS 中

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(deviceSN));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN 与 条形码SN [不匹配]"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(deviceSN), "快照应包含设备SN");

            _storageServiceMock.Verify(
                x => x.SaveVerifyResultAsync(It.Is<SnVerifyResult>(r =>
                    r.Result == "FAIL" &&
                    r.SN == stickerSN &&
                    r.DeviceSN == deviceSN &&
                    r.FailReason == "设备SN 与 条形码SN [不匹配]")),
                Times.Once);
        }

        #endregion

        #region 异常场景测试

        [Test]
        public async Task StartVerificationAsync_ShouldFail_WhenAdbTimeout()
        {
            // Arrange
            const string stickerSN = "SN006";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Failure("Timeout", true));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("TIMEOUT"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("ADB读取设备超时"));
        }

        [Test]
        public async Task StartVerificationAsync_ShouldFail_WhenAdbSnEmpty()
        {
            // Arrange
            const string stickerSN = "SN007";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(false);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(""));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("ADB读取设备SN为空"));
            Assert.That(_lastSnapshot.DeviceSN, Is.Null, "ADB读取为空时设备SN应为null");
        }

        #endregion

        #region 决策树顺序测试（确保规则按顺序判断）

        [Test]
        public async Task StartVerificationAsync_ShouldCheckRule2BeforeRule3_WhenBothConditionsMet()
        {
            // Arrange - StickerSN == DeviceSN 且都在历史 PASS 中（规则 2 优先）
            const string stickerSN = "SN008";
            const string deviceSN = "SN008";

            _storageServiceMock
                .Setup(x => x.IsStickerSnInPassHistoryAsync(stickerSN))
                .ReturnsAsync(true);
            _storageServiceMock
                .Setup(x => x.IsBindingInPassHistoryAsync(stickerSN, deviceSN))
                .ReturnsAsync(true);

            _adbAccessServiceMock
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(deviceSN));

            _storageServiceMock
                .Setup(x => x.GetFailResultBySnAsync(TestBatchId, stickerSN))
                .ReturnsAsync((SnVerifyResult)null);

            _storageServiceMock
                .Setup(x => x.SaveVerifyResultAsync(It.IsAny<SnVerifyResult>()))
                .Returns(Task.CompletedTask);

            // Act
            await _processCoordinator.StartVerificationAsync(stickerSN);

            // Assert - 应该命中规则 2（设备SN已存在），而不是规则 3
            Assert.That(_lastSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_lastSnapshot.FailReason, Is.EqualTo("设备SN已存在"));
            Assert.That(_lastSnapshot.DeviceSN, Is.EqualTo(deviceSN), "快照应包含设备SN");

            // 验证未调用 IsDeviceSnInPassHistoryAsync（因为规则 2 已命中，不会继续判断）
            _storageServiceMock.Verify(
                x => x.IsDeviceSnInPassHistoryAsync(It.IsAny<string>()),
                Times.Never);
        }

        #endregion
    }
}
