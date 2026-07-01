using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Coordination;
using SnVerify.Services.Adb;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Parameter;
using SnVerify.Services.Rules;
using SnVerify.Services.Storage;
using SnVerify.Services.Verification;
using SnVerify.Infrastructure.Product;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class ProcessCoordinatorLegacyAndroidTests
    {
        private Mock<IStorageService> _storageMock;
        private Mock<IAdbAccessService> _adbMock;
        private Mock<IDeviceAccessService> _deviceAccessMock;
        private Mock<IParameterService> _parameterMock;
        private Mock<IProductRegistry> _registryMock;
        private IVersionVerificationService _versionService;
        private ProcessCoordinator _coordinator;

        private const string SessionName = "ORD_20260701_120000";
        private const int InternalSessionId = 42;
        private const string StickerSn = "SN001";
        private const string DeviceSn = "SN001";

        private static ProductProfile SoltagProfile => new ProductProfile
        {
            ProductCode = "SOLTAG25",
            Mode = VerificationMode.Legacy,
            EnableAndroidVersionCheck = true,
            AdbConfig = new DeviceAdbConfig()
        };

        [SetUp]
        public void SetUp()
        {
            _storageMock = new Mock<IStorageService>();
            _adbMock = new Mock<IAdbAccessService>();
            _deviceAccessMock = new Mock<IDeviceAccessService>();
            _parameterMock = new Mock<IParameterService>();
            _registryMock = new Mock<IProductRegistry>();
            _versionService = new VersionVerificationService();

            _storageMock.Setup(s => s.GetInternalSessionIdBySessionNameAsync(SessionName))
                .ReturnsAsync(InternalSessionId);
            _storageMock.Setup(s => s.GetProductCodeBySessionIdAsync(InternalSessionId))
                .ReturnsAsync("SOLTAG25");
            _registryMock.Setup(r => r.GetProductProfile("SOLTAG25")).Returns(SoltagProfile);
            _parameterMock.Setup(p => p.GetParameterAsync(InternalSessionId))
                .ReturnsAsync(new VerificationParameter
                {
                    SessionId = InternalSessionId,
                    ExpectedAndroidVersion = "V1.0"
                });
            _storageMock.Setup(s => s.GetTestRecordBySessionAndStickerSnAsync(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync((TestRecord)null);
            _storageMock.Setup(s => s.SaveTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);
            _storageMock.Setup(s => s.UpdateTestRecordAsync(It.IsAny<TestRecord>())).Returns(Task.CompletedTask);
            _storageMock.Setup(s => s.IsBindingInPassHistoryAsync(It.IsAny<string>())).ReturnsAsync(false);
            _storageMock.Setup(s => s.IsStickerSnInPassHistoryAsync(It.IsAny<string>())).ReturnsAsync(false);
            _storageMock.Setup(s => s.IsDeviceSnInPassHistoryAsync(It.IsAny<string>())).ReturnsAsync(false);

            _coordinator = new ProcessCoordinator(
                SessionName,
                _storageMock.Object,
                _adbMock.Object,
                parameterService: _parameterMock.Object,
                versionVerificationService: _versionService,
                productRegistry: _registryMock.Object,
                deviceAccessService: _deviceAccessMock.Object);
        }

        [Test]
        public async Task Should_Pass_When_Sn_And_AndroidVersion_Match_For_Soltag25()
        {
            _deviceAccessMock.Setup(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = DeviceSn, AndroidVersion = "V1.0" });

            await _coordinator.StartVerificationAsync(StickerSn);

            _storageMock.Verify(s => s.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                r.Result == "PASS" &&
                r.ExpectedVersion == "V1.0" &&
                r.ActualVersion == "V1.0" &&
                r.StickerSN == StickerSn)), Times.Once);
            _adbMock.Verify(a => a.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(_coordinator.Snapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(_coordinator.Snapshot.DeviceInfo?.AndroidVersion, Is.EqualTo("V1.0"));
        }

        [Test]
        public async Task Should_Fail_When_Sn_Pass_But_AndroidVersion_Mismatch_For_Soltag25()
        {
            _deviceAccessMock.Setup(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = DeviceSn, AndroidVersion = "V9.9" });

            await _coordinator.StartVerificationAsync(StickerSn);

            _storageMock.Verify(s => s.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                r.Result == "FAIL" &&
                r.FailReason == RuleFailReasonCodes.AndroidVersionMismatch &&
                r.ExpectedVersion == "V1.0" &&
                r.ActualVersion == "V9.9")), Times.Once);
            Assert.That(_coordinator.Snapshot.LastResult, Is.EqualTo("FAIL"));
        }

        [Test]
        public async Task Should_Fail_When_Sn_Mismatch_Without_AndroidVersion_Check_For_Soltag25()
        {
            _deviceAccessMock.Setup(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = "OTHER", AndroidVersion = "V1.0" });

            await _coordinator.StartVerificationAsync(StickerSn);

            _storageMock.Verify(s => s.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                r.Result == "FAIL" &&
                r.FailReason.Contains("不匹配") &&
                r.ExpectedVersion == "V1.0" &&
                r.ActualVersion == "V1.0")), Times.Once);
            _parameterMock.Verify(p => p.GetParameterAsync(InternalSessionId), Times.Once);
        }

        [Test]
        public async Task Should_Use_ReadDeviceSnAsync_When_AndroidVersionCheck_Disabled()
        {
            _registryMock.Setup(r => r.GetProductProfile("SOLTAG25")).Returns(new ProductProfile
            {
                ProductCode = "SOLTAG25",
                Mode = VerificationMode.Legacy,
                EnableAndroidVersionCheck = false
            });
            _adbMock.Setup(a => a.ReadDeviceSnAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success(DeviceSn));

            await _coordinator.StartVerificationAsync(StickerSn);

            _adbMock.Verify(a => a.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Once);
            _deviceAccessMock.Verify(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Never);
        }

        [Test]
        public async Task Should_Use_DeviceAccess_When_Db_ProductCode_Empty_But_SessionProductCode_Is_Soltag25()
        {
            _storageMock.Setup(s => s.GetProductCodeBySessionIdAsync(InternalSessionId))
                .ReturnsAsync((string)null);

            var coordinator = new ProcessCoordinator(
                SessionName,
                _storageMock.Object,
                _adbMock.Object,
                parameterService: _parameterMock.Object,
                versionVerificationService: _versionService,
                productRegistry: _registryMock.Object,
                deviceAccessService: _deviceAccessMock.Object,
                sessionProductCode: "SOLTAG25");

            _deviceAccessMock.Setup(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = DeviceSn, AndroidVersion = "V1.0" });

            await coordinator.StartVerificationAsync(StickerSn);

            _deviceAccessMock.Verify(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Once);
            _adbMock.Verify(a => a.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(coordinator.Snapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(coordinator.Snapshot.DeviceInfo?.AndroidVersion, Is.EqualTo("V1.0"));
        }

        [Test]
        public async Task Should_Fail_When_SessionProductCode_Requires_Android_But_Parameter_Missing()
        {
            _parameterMock.Setup(p => p.GetParameterAsync(InternalSessionId))
                .ReturnsAsync((VerificationParameter)null);

            var coordinator = new ProcessCoordinator(
                SessionName,
                _storageMock.Object,
                _adbMock.Object,
                parameterService: _parameterMock.Object,
                versionVerificationService: _versionService,
                productRegistry: _registryMock.Object,
                deviceAccessService: _deviceAccessMock.Object,
                sessionProductCode: "SOLTAG25");

            _deviceAccessMock.Setup(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = DeviceSn, AndroidVersion = "V1.0" });

            await coordinator.StartVerificationAsync(StickerSn);

            _storageMock.Verify(s => s.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                r.Result == "FAIL" &&
                r.FailReason == RuleFailReasonCodes.ParameterNotConfigured)), Times.Once);
            Assert.That(coordinator.Snapshot.LastResult, Is.EqualTo("FAIL"));
        }

        [Test]
        public async Task Should_Fail_When_SessionProductCode_Requires_Android_But_Registry_Missing()
        {
            _registryMock.Setup(r => r.GetProductProfile("SOLTAG25")).Returns((ProductProfile)null);

            var coordinator = new ProcessCoordinator(
                SessionName,
                _storageMock.Object,
                _adbMock.Object,
                parameterService: _parameterMock.Object,
                versionVerificationService: _versionService,
                productRegistry: _registryMock.Object,
                deviceAccessService: _deviceAccessMock.Object,
                sessionProductCode: "SOLTAG25");

            await coordinator.StartVerificationAsync(StickerSn);

            _adbMock.Verify(a => a.ReadDeviceSnAsync(It.IsAny<CancellationToken>()), Times.Never);
            _deviceAccessMock.Verify(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Never);
            _storageMock.Verify(s => s.SaveTestRecordAsync(It.Is<TestRecord>(r =>
                r.Result == "FAIL" &&
                r.FailReason == RuleFailReasonCodes.ProductProfileNotFound)), Times.Once);
            Assert.That(coordinator.Snapshot.LastResult, Is.EqualTo("FAIL"));
        }

        [Test]
        public async Task Should_Persist_ExpectedVersion_On_Fail_Retry_Update()
        {
            _deviceAccessMock.Setup(d => d.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = "OTHER", AndroidVersion = "V1.0" });

            var existingFail = new TestRecord
            {
                Id = 99,
                SessionId = InternalSessionId,
                StickerSN = StickerSn,
                Result = "FAIL",
                ExpectedVersion = null,
                ActualVersion = null
            };
            _storageMock.Setup(s => s.GetTestRecordBySessionAndStickerSnAsync(InternalSessionId, StickerSn))
                .ReturnsAsync(existingFail);

            await _coordinator.StartVerificationAsync(StickerSn);

            _storageMock.Verify(s => s.UpdateTestRecordAsync(It.Is<TestRecord>(r =>
                r.Id == 99 &&
                r.ExpectedVersion == "V1.0" &&
                r.ActualVersion == "V1.0")), Times.Once);
        }
    }
}
