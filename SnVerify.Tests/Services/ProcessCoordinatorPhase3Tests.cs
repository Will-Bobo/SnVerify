/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Domain.Product;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Parameter;
using SnVerify.Services.Storage;
using SnVerify.Services.Mes.Gate;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ProcessCoordinator Phase3 流程骨架单元测试：SN / ChipId / 版本 / ADB / 参数配置。
    /// </summary>
    [TestFixture]
    public class ProcessCoordinatorPhase3Tests
    {
        private const string SessionId = "ORDER001_20260305_120000";
        private const string OrderId = "ORDER001";
        private const string ProjectId = "KM001";
        private const int InternalSessionId = 10;
        private const string StickerSn = "SN001";
        private const string DeviceSn = "SN001";
        private const string ChipId = "F501234";

        private Mock<IStorageService> _storageMock;
        private Mock<IDeviceAccessService> _deviceAccessMock;
        private Mock<ILoggingService> _loggingMock;
        private Mock<IParameterService> _parameterServiceMock;
        private Mock<SnVerify.Services.Adb.IAdbAccessService> _adbMock;
        private ProcessCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _storageMock = new Mock<IStorageService>();
            _deviceAccessMock = new Mock<IDeviceAccessService>();
            _adbMock = new Mock<SnVerify.Services.Adb.IAdbAccessService>();
            _loggingMock = new Mock<ILoggingService>();
            _parameterServiceMock = new Mock<IParameterService>();

            _storageMock
                .Setup(x => x.GetInternalSessionIdBySessionNameAsync(SessionId))
                .ReturnsAsync(InternalSessionId);
            _storageMock
                .Setup(x => x.GetProductNameBySessionNameAsync(SessionId))
                .ReturnsAsync(ProjectId);
            _storageMock
                .Setup(x => x.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Returns(Task.CompletedTask);

            _coordinator = new ProcessCoordinator(
                SessionId,
                _storageMock.Object,
                _adbMock.Object,
                _loggingMock.Object,
                null,
                null,
                MesMode.Disabled,
                OrderId,
                _parameterServiceMock.Object,
                versionVerificationService: null,
                productRegistry: new SnVerify.Infrastructure.Product.ProductRegistryAdapter(),
                deviceAccessService: _deviceAccessMock.Object);
        }

        private static VerificationParameter CreateParameter(
            string android = "A1",
            string board = null,
            string charge = null)
        {
            return new VerificationParameter
            {
                SessionId = InternalSessionId,
                ExpectedAndroidVersion = android,
                ExpectedBoardVersion = board,
                ExpectedChargeBoardVersion = charge
            };
        }

        private static DeviceInfo CreateDeviceInfo(
            string sn = DeviceSn,
            string chipId = ChipId,
            string android = "A1",
            string board = null,
            string charge = null)
        {
            return new DeviceInfo
            {
                DeviceSn = sn,
                ChipId = chipId,
                AndroidVersion = android,
                BoardVersion = board,
                ChargeBoardVersion = charge
            };
        }

        [Test]
        public async Task ProcessScanAsync_SnMatch_ChipOk_VersionOk_ShouldPass()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(CreateParameter(android: "A1"));

            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(sn: StickerSn, chipId: ChipId, android: "A1"));

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            _storageMock
                .Setup(x => x.IsChipIdPassedInBatchAsync(It.IsAny<string>(), OrderId, ChipId))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.IsProcessing, Is.False);
            Assert.That(snapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(snapshot.CurrentSn, Is.EqualTo(StickerSn));
            Assert.That(snapshot.DeviceSN, Is.EqualTo(DeviceSn));

            _storageMock.Verify(
                x => x.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                    r.SessionId == InternalSessionId &&
                    r.StickerSN == StickerSn &&
                    r.DeviceSN == DeviceSn &&
                    r.ChipId == ChipId &&
                    r.Result == "PASS" &&
                    r.FailReason == null)),
                Times.Once);
        }

        [Test]
        public async Task ProcessScanAsync_SnNotMatch_ShouldFailWithSnNotMatch()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(CreateParameter(android: "A1"));

            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(sn: "OTHER_SN", chipId: ChipId, android: "A1"));

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("SN_NOT_MATCH"));

            _storageMock.Verify(
                x => x.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                    r.Result == "FAIL" &&
                    r.FailReason == "SN_NOT_MATCH")),
                Times.Once);
        }

        [Test]
        public async Task ProcessScanAsync_InvalidChipId_ShouldFailWithChipIdInvalid()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(CreateParameter(android: "A1"));

            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(sn: StickerSn, chipId: "X123", android: "A1"));

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("CHIPID_INVALID"));

            _storageMock.Verify(
                x => x.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never,
                "ChipId 格式非法时不应执行唯一性查询");
        }

        [Test]
        public async Task ProcessScanAsync_DuplicateChipIdInOrder_ShouldFailWithChipIdDuplicate()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(CreateParameter(android: "A1"));

            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(sn: StickerSn, chipId: ChipId, android: "A1"));

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            _storageMock
                .Setup(x => x.IsChipIdPassedInBatchAsync(It.IsAny<string>(), OrderId, ChipId))
                .ReturnsAsync(true);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("CHIPID_DUPLICATE"));
        }

        [Test]
        public async Task ProcessScanAsync_AndroidVersionMismatch_ShouldFailWithAndroidVersionMismatch()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(CreateParameter(android: "A1"));

            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(CreateDeviceInfo(sn: StickerSn, chipId: ChipId, android: "A2"));

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            _storageMock
                .Setup(x => x.IsChipIdPassedInBatchAsync(It.IsAny<string>(), OrderId, ChipId))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("ANDROID_VERSION_MISMATCH"));
        }

        [Test]
        public async Task ProcessScanAsync_AdbReadFail_ShouldFailWithAdbReadFail()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(CreateParameter(android: "A1"));

            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync((DeviceInfo)null);

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("ADB_READ_FAIL"));
        }

        [Test]
        public async Task ProcessScanAsync_ParameterNotConfigured_ShouldFailAndSkipAdb()
        {
            // Arrange
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync((VerificationParameter)null);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot.FailReason, Is.EqualTo("PARAMETER_NOT_CONFIGURED"));

            _deviceAccessMock.Verify(
                x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()),
                Times.Never,
                "参数未配置时不应访问 ADB");
        }

        [Test]
        public async Task ProcessScanAsync_ParameterWithAllExpectedFieldsFilled_ShouldPassWhenAllMatch()
        {
            // Arrange: Parameter 三个 Expected 都存在，设备信息全部匹配
            var parameter = CreateParameter(android: "A1", board: "B1", charge: "C1");
            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(parameter);

            var deviceInfo = CreateDeviceInfo(sn: StickerSn, chipId: ChipId, android: "A1", board: "B1", charge: "C1");
            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(deviceInfo);

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            _storageMock
                .Setup(x => x.IsChipIdPassedInBatchAsync(It.IsAny<string>(), OrderId, ChipId))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(snapshot.FailReason, Is.Null);

            _storageMock.Verify(
                x => x.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                    r.Result == "PASS" &&
                    r.StickerSN == StickerSn &&
                    r.DeviceSN == DeviceSn &&
                    r.ChipId == ChipId &&
                    r.BoardVersion == "B1" &&
                    r.ChargeBoardVersion == "C1" &&
                    r.ExpectedVersion == "A1" &&
                    r.ActualVersion == "A1")),
                Times.Once);
        }

        [Test]
        public async Task ProcessScanAsync_ParameterExistsButAllExpectedEmpty_ShouldSkipVersionChecksAndPass()
        {
            // Arrange: Parameter 存在但三个 Expected 都为空，仍应继续流程，不以 PARAMETER_NOT_CONFIGURED 失败
            var parameter = new VerificationParameter
            {
                SessionId = InternalSessionId,
                ExpectedAndroidVersion = null,
                ExpectedBoardVersion = null,
                ExpectedChargeBoardVersion = null
            };

            _parameterServiceMock
                .Setup(x => x.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(parameter);

            // 设备 SN / ChipId 合法，但版本刻意设置为“不匹配”，因为没有 Expected，不应触发版本错误
            var deviceInfo = CreateDeviceInfo(sn: StickerSn, chipId: ChipId, android: "X-ANDROID", board: "X-BOARD", charge: "X-CHARGE");
            _deviceAccessMock
                .Setup(x => x.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(deviceInfo);

            _storageMock
                .Setup(x => x.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), OrderId, StickerSn))
                .ReturnsAsync(false);

            _storageMock
                .Setup(x => x.IsChipIdPassedInBatchAsync(It.IsAny<string>(), OrderId, ChipId))
                .ReturnsAsync(false);

            // Act
            await _coordinator.ProcessScanAsync(StickerSn, ProjectId);

            // Assert: 只要 SN / ChipId / 订单内唯一校验通过，流程应 PASS 而不是 PARAMETER_NOT_CONFIGURED 或版本不一致
            var snapshot = _coordinator.Snapshot;
            Assert.That(snapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(snapshot.FailReason, Is.Null);

            _storageMock.Verify(
                x => x.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                    r.Result == "PASS" &&
                    r.StickerSN == StickerSn &&
                    r.DeviceSN == DeviceSn &&
                    r.ChipId == ChipId)),
                Times.Once);
        }
    }
}

