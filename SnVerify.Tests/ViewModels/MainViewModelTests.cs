/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Domain.Validation;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Logging;
using SnVerify.Services.Mes.Gate;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
using SnVerify.ViewModels;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Parameter;
using SnVerify.Properties;

namespace SnVerify.Tests.ViewModels
{
    /// <summary>
    /// MainViewModel 单元测试（UI 可运行闭环阶段）
    /// </summary>
    [TestFixture]
    public class MainViewModelTests
    {
        private MainViewModel _viewModel;
        private Mock<ISessionLifecycleService> _sessionLifecycleServiceMock;
        private Mock<IVerificationFlowServiceFactory> _flowServiceFactoryMock;
        private Mock<IVerificationFlowService> _verificationFlowServiceMock;
        private Mock<IVersionVerificationFlowService> _versionVerificationFlowServiceMock;
        private Mock<ILoggingService> _loggingServiceMock;
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;
        private Mock<IDeviceAccessService> _deviceAccessServiceMock;
        private Mock<IExportAggregationService> _exportAggregationServiceMock;
        private Mock<IOrderNameValidator> _orderNameValidatorMock;
        private Mock<IUserDialogService> _dialogServiceMock;
        private Mock<IProductRegistry> _productRegistryMock;
        private string _backupLastProjectId;
        private string _backupLastOrderId;
        private string _backupLastExportFolder;
        private string _backupLastProductCode;
        private string _backupLastExpectedAndroidVersion;
        private string _backupLastExpectedBoardVersion;
        private string _backupLastExpectedChargeBoardVersion;

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 1500, int pollMs = 20)
        {
            var start = DateTime.UtcNow;
            while (!predicate())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");
                await Task.Delay(pollMs);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _backupLastProjectId = Settings.Default.LastProjectId;
            _backupLastOrderId = Settings.Default.LastOrderId;
            _backupLastExportFolder = Settings.Default.LastExportFolder;
            _backupLastProductCode = Settings.Default.LastProductCode;
            _backupLastExpectedAndroidVersion = Settings.Default.LastExpectedAndroidVersion;
            _backupLastExpectedBoardVersion = Settings.Default.LastExpectedBoardVersion;
            _backupLastExpectedChargeBoardVersion = Settings.Default.LastExpectedChargeBoardVersion;
            Settings.Default.LastProjectId = "";
            Settings.Default.LastOrderId = "";
            Settings.Default.LastExportFolder = "";
            Settings.Default.LastProductCode = "";
            Settings.Default.LastExpectedAndroidVersion = "";
            Settings.Default.LastExpectedBoardVersion = "";
            Settings.Default.LastExpectedChargeBoardVersion = "";
            Settings.Default.Save();

            _sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            _flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            _verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            _versionVerificationFlowServiceMock = new Mock<IVersionVerificationFlowService>();
            _loggingServiceMock = new Mock<ILoggingService>();
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();
            _deviceAccessServiceMock = new Mock<IDeviceAccessService>();
            _exportAggregationServiceMock = new Mock<IExportAggregationService>();
            _orderNameValidatorMock = new Mock<IOrderNameValidator>();
            _dialogServiceMock = new Mock<IUserDialogService>();
            _productRegistryMock = new Mock<IProductRegistry>();

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            _verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            _versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            _flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>())).Returns(_verificationFlowServiceMock.Object);
            _loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());
            _storageServiceMock.Setup(s => s.ProjectNameExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            _viewModel = new MainViewModel(
                _sessionLifecycleServiceMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _exportAggregationServiceMock.Object,
                _orderNameValidatorMock.Object,
                _dialogServiceMock.Object,
                _versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                _productRegistryMock.Object,
                parameterService: null,
                deviceAccessService: _deviceAccessServiceMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            Settings.Default.LastProjectId = _backupLastProjectId ?? "";
            Settings.Default.LastOrderId = _backupLastOrderId ?? "";
            Settings.Default.LastExportFolder = _backupLastExportFolder ?? "";
            Settings.Default.LastProductCode = _backupLastProductCode ?? "";
            Settings.Default.LastExpectedAndroidVersion = _backupLastExpectedAndroidVersion ?? "";
            Settings.Default.LastExpectedBoardVersion = _backupLastExpectedBoardVersion ?? "";
            Settings.Default.LastExpectedChargeBoardVersion = _backupLastExpectedChargeBoardVersion ?? "";
            Settings.Default.Save();
        }

        [Test]
        public void Constructor_WhenLogDirectoryIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new MainViewModel(
                    _sessionLifecycleServiceMock.Object,
                    _flowServiceFactoryMock.Object,
                    _loggingServiceMock.Object,
                    _storageServiceMock.Object,
                    _adbAccessServiceMock.Object,
                    _exportAggregationServiceMock.Object,
                    _orderNameValidatorMock.Object,
                    _dialogServiceMock.Object,
                    _versionVerificationFlowServiceMock.Object,
                    null));
        }

        [Test]
        public async Task StartBatchAsync_ForPhase3Product_ShouldPersistVerificationParameter()
        {
            // Arrange
            var sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            var flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            var verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            var versionVerificationFlowServiceMock = new Mock<IVersionVerificationFlowService>();
            var loggingServiceMock = new Mock<ILoggingService>();
            var storageServiceMock = new Mock<IStorageService>();
            var adbAccessServiceMock = new Mock<IAdbAccessService>();
            var exportAggregationServiceMock = new Mock<IExportAggregationService>();
            var orderNameValidatorMock = new Mock<IOrderNameValidator>();
            var dialogServiceMock = new Mock<IUserDialogService>();
            var productRegistryMock = new Mock<IProductRegistry>();
            var parameterServiceMock = new Mock<IParameterService>();
            const string createdSessionId = "ORDER001_20260312_120000";
            const int internalSessionId = 1001;

            sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>()))
                                  .Returns(verificationFlowServiceMock.Object);
            loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());

            productRegistryMock.Setup(r => r.GetProductCodes())
                .Returns(new[] { "KM001" });
            productRegistryMock.Setup(r => r.Get("KM001"))
                .Returns(new ProductProfile
                {
                    ProductCode = "KM001",
                    Mode = VerificationMode.Phase3,
                    AdbConfig = null
                });

            orderNameValidatorMock
                .Setup(v => v.Validate(It.IsAny<string>(), out It.Ref<string>.IsAny))
                .Returns(true);
            sessionLifecycleServiceMock
                .Setup(s => s.CreateAndStartSession("ORDER001", "ORDER001", "PROJECT_KM001", "KM001"))
                .Returns(createdSessionId);
            storageServiceMock
                .Setup(s => s.GetInternalSessionIdBySessionNameAsync(createdSessionId))
                .ReturnsAsync(internalSessionId);

            var tcs = new TaskCompletionSource<bool>();
            parameterServiceMock
                .Setup(p => p.SaveParameterAsync(It.IsAny<VerificationParameter>()))
                .Callback(() => tcs.TrySetResult(true))
                .Returns(Task.CompletedTask);

            var viewModel = new MainViewModel(
                sessionLifecycleServiceMock.Object,
                flowServiceFactoryMock.Object,
                loggingServiceMock.Object,
                storageServiceMock.Object,
                adbAccessServiceMock.Object,
                exportAggregationServiceMock.Object,
                orderNameValidatorMock.Object,
                dialogServiceMock.Object,
                versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                productRegistryMock.Object,
                parameterServiceMock.Object);

            viewModel.SelectedProductCode = "KM001";
            viewModel.ProjectIdInput = "PROJECT_KM001";
            viewModel.OrderIdInput = "ORDER001";
            viewModel.ExpectedAndroidVersion = "A1";
            viewModel.ExpectedBoardVersion = "B1";
            viewModel.ExpectedChargeBoardVersion = "C1";

            // Act
            viewModel.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => tcs.Task.IsCompleted);

            // Assert
            parameterServiceMock.Verify(p => p.SaveParameterAsync(
                    It.Is<VerificationParameter>(vp =>
                        vp.SessionId == internalSessionId &&
                        vp.ExpectedAndroidVersion == "A1" &&
                        vp.ExpectedBoardVersion == "B1" &&
                        vp.ExpectedChargeBoardVersion == "C1")),
                Times.Once);
        }

        [Test]
        public void Constructor_ShouldRestoreLastProductCodeAndExpectedVersions_ForPhase3Product()
        {
            Settings.Default.LastProductCode = "KM001";
            Settings.Default.LastExpectedAndroidVersion = "1.0.5";
            Settings.Default.LastExpectedBoardVersion = "1.0.3";
            Settings.Default.LastExpectedChargeBoardVersion = "2.0.1";
            Settings.Default.Save();

            var sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            var flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            var verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            var versionVerificationFlowServiceMock = new Mock<IVersionVerificationFlowService>();
            var loggingServiceMock = new Mock<ILoggingService>();
            var storageServiceMock = new Mock<IStorageService>();
            var adbAccessServiceMock = new Mock<IAdbAccessService>();
            var exportAggregationServiceMock = new Mock<IExportAggregationService>();
            var orderNameValidatorMock = new Mock<IOrderNameValidator>();
            var dialogServiceMock = new Mock<IUserDialogService>();
            var productRegistryMock = new Mock<IProductRegistry>();
            sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(verificationFlowServiceMock.Object);
            loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());
            productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "SOLTAG25", "KM001" });
            productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            productRegistryMock.Setup(r => r.Get("SOLTAG25")).Returns(new ProductProfile
            {
                ProductCode = "SOLTAG25",
                Mode = VerificationMode.Legacy
            });

            var viewModel = new MainViewModel(
                sessionLifecycleServiceMock.Object,
                flowServiceFactoryMock.Object,
                loggingServiceMock.Object,
                storageServiceMock.Object,
                adbAccessServiceMock.Object,
                exportAggregationServiceMock.Object,
                orderNameValidatorMock.Object,
                dialogServiceMock.Object,
                versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                productRegistryMock.Object);

            Assert.That(viewModel.SelectedProductCode, Is.EqualTo("KM001"));
            Assert.That(viewModel.ExpectedAndroidVersion, Is.EqualTo("1.0.5"));
            Assert.That(viewModel.ExpectedBoardVersion, Is.EqualTo("1.0.3"));
            Assert.That(viewModel.ExpectedChargeBoardVersion, Is.EqualTo("2.0.1"));
        }

        [Test]
        public void Constructor_ShouldRestoreLastProjectIdAndLastOrderId_AndNotBeClearedByProductInitialization()
        {
            Settings.Default.LastProjectId = "PROJECT_LAST";
            Settings.Default.LastOrderId = "ORDER_LAST";
            Settings.Default.LastProductCode = "KM001";
            Settings.Default.Save();

            var sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            var flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            var verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            var versionVerificationFlowServiceMock = new Mock<IVersionVerificationFlowService>();
            var loggingServiceMock = new Mock<ILoggingService>();
            var storageServiceMock = new Mock<IStorageService>();
            var adbAccessServiceMock = new Mock<IAdbAccessService>();
            var exportAggregationServiceMock = new Mock<IExportAggregationService>();
            var orderNameValidatorMock = new Mock<IOrderNameValidator>();
            var dialogServiceMock = new Mock<IUserDialogService>();
            var productRegistryMock = new Mock<IProductRegistry>();

            sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(verificationFlowServiceMock.Object);
            loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());
            storageServiceMock.Setup(s => s.ProjectNameExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "SOLTAG25", "KM001" });
            productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile { ProductCode = "KM001", Mode = VerificationMode.Phase3 });
            productRegistryMock.Setup(r => r.Get("SOLTAG25")).Returns(new ProductProfile { ProductCode = "SOLTAG25", Mode = VerificationMode.Legacy });

            var viewModel = new MainViewModel(
                sessionLifecycleServiceMock.Object,
                flowServiceFactoryMock.Object,
                loggingServiceMock.Object,
                storageServiceMock.Object,
                adbAccessServiceMock.Object,
                exportAggregationServiceMock.Object,
                orderNameValidatorMock.Object,
                dialogServiceMock.Object,
                versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                productRegistryMock.Object);

            Assert.That(viewModel.ProjectIdInput, Is.EqualTo("PROJECT_LAST"));
            Assert.That(viewModel.OrderIdInput, Is.EqualTo("ORDER_LAST"));
        }

        [Test]
        public void Constructor_ShouldRestoreLastProjectIdAndLastOrderId_WhenProductOrderIsReversed()
        {
            Settings.Default.LastProjectId = "PROJECT_LAST";
            Settings.Default.LastOrderId = "ORDER_LAST";
            Settings.Default.LastProductCode = "SOLTAG25";
            Settings.Default.Save();

            var sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            var flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            var verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            var versionVerificationFlowServiceMock = new Mock<IVersionVerificationFlowService>();
            var loggingServiceMock = new Mock<ILoggingService>();
            var storageServiceMock = new Mock<IStorageService>();
            var adbAccessServiceMock = new Mock<IAdbAccessService>();
            var exportAggregationServiceMock = new Mock<IExportAggregationService>();
            var orderNameValidatorMock = new Mock<IOrderNameValidator>();
            var dialogServiceMock = new Mock<IUserDialogService>();
            var productRegistryMock = new Mock<IProductRegistry>();

            sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(verificationFlowServiceMock.Object);
            loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());
            storageServiceMock.Setup(s => s.ProjectNameExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            // 产品顺序与上一个测试相反
            productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "KM001", "SOLTAG25" });
            productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile { ProductCode = "KM001", Mode = VerificationMode.Phase3 });
            productRegistryMock.Setup(r => r.Get("SOLTAG25")).Returns(new ProductProfile { ProductCode = "SOLTAG25", Mode = VerificationMode.Legacy });

            var viewModel = new MainViewModel(
                sessionLifecycleServiceMock.Object,
                flowServiceFactoryMock.Object,
                loggingServiceMock.Object,
                storageServiceMock.Object,
                adbAccessServiceMock.Object,
                exportAggregationServiceMock.Object,
                orderNameValidatorMock.Object,
                dialogServiceMock.Object,
                versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                productRegistryMock.Object);

            Assert.That(viewModel.ProjectIdInput, Is.EqualTo("PROJECT_LAST"));
            Assert.That(viewModel.OrderIdInput, Is.EqualTo("ORDER_LAST"));
        }

        [Test]
        public async Task StartBatchAsync_ForPhase3Product_WhenProjectIdEmpty_ShouldWarn_AndNotAutoFillProjectIdInput()
        {
            // Arrange
            var orderId = "ORDER001";
            string validationMessage = null;
            _productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "KM001" });
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile { ProductCode = "KM001", Mode = VerificationMode.Phase3 });
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);

            _viewModel.SelectedProductCode = "KM001";
            _viewModel.ProjectIdInput = "";
            _viewModel.OrderIdInput = orderId;
            _viewModel.ExpectedAndroidVersion = "A1"; // 让 Phase3 目标版本校验通过，确保命中“项目名不能为空”分支

            // Act
            _viewModel.StartBatchCommand.Execute(null);
            await Task.Delay(250);

            // Assert
            _dialogServiceMock.Verify(d => d.ShowWarning(It.Is<string>(s => s != null && s.Contains("项目名")), It.IsAny<string>()), Times.Once);
            Assert.That(_viewModel.ProjectIdInput, Is.EqualTo("")); // 不应被自动填充为 KM001
            _sessionLifecycleServiceMock.Verify(s => s.CreateAndStartSession(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task StartBatchAsync_ForPhase3Product_ShouldSaveExpectedVersionsToSettings()
        {
            // Arrange
            var sessionId = "ORDER001_20260310_100000";
            var projectId = "KM001";
            var orderId = "ORDER001";
            string validationMessage = null;
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);
            _sessionLifecycleServiceMock
                .Setup(s => s.CreateAndStartSession(orderId, orderId, projectId, projectId))
                .Returns(sessionId);
            _sessionLifecycleServiceMock
                .SetupSequence(s => s.Snapshot)
                .Returns(SessionSnapshot.Idle())
                .Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "KM001" });
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            var parameterServiceMock = new Mock<IParameterService>();
            parameterServiceMock.Setup(p => p.SaveParameterAsync(It.IsAny<VerificationParameter>())).Returns(Task.CompletedTask);
            _storageServiceMock
                .Setup(s => s.GetInternalSessionIdBySessionNameAsync(sessionId))
                .ReturnsAsync(101);
            _viewModel = new MainViewModel(
                _sessionLifecycleServiceMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _exportAggregationServiceMock.Object,
                _orderNameValidatorMock.Object,
                _dialogServiceMock.Object,
                _versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                _productRegistryMock.Object,
                parameterServiceMock.Object);
            _viewModel.SelectedProductCode = "KM001";
            _viewModel.ProjectIdInput = projectId;
            _viewModel.OrderIdInput = orderId;
            _viewModel.ExpectedAndroidVersion = "A1";
            _viewModel.ExpectedBoardVersion = "B1";
            _viewModel.ExpectedChargeBoardVersion = "C1";

            // Act
            _viewModel.StartBatchCommand.Execute(null);
            await Task.Delay(150);

            // Assert
            Assert.That(Settings.Default.LastProductCode, Is.EqualTo("KM001"));
            Assert.That(Settings.Default.LastExpectedAndroidVersion, Is.EqualTo("A1"));
            Assert.That(Settings.Default.LastExpectedBoardVersion, Is.EqualTo("B1"));
            Assert.That(Settings.Default.LastExpectedChargeBoardVersion, Is.EqualTo("C1"));
        }

        [Test]
        public void ProductFieldLabels_WhenKm001Configured_ShouldUseConfiguredLabels()
        {
            // Arrange
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3,
                FieldLabels = new Dictionary<DeviceInfoField, string>
                {
                    { DeviceInfoField.DeviceSn, "设备SN" },
                    { DeviceInfoField.AndroidVersion, "Android版本号" },
                    { DeviceInfoField.BoardVersion, "芯片版本号" },
                    { DeviceInfoField.ChargeBoardVersion, "充电板版本号" },
                    { DeviceInfoField.ChipId, "芯片ID" },
                    { DeviceInfoField.WifiMac, "MAC地址" }
                }
            });

            // Act
            _viewModel.SelectedProductCode = "KM001";

            // Assert
            Assert.That(_viewModel.DeviceSnLabel, Is.EqualTo("设备SN"));
            Assert.That(_viewModel.AndroidVersionLabel, Is.EqualTo("Android版本号"));
            Assert.That(_viewModel.BoardVersionLabel, Is.EqualTo("芯片版本号"));
            Assert.That(_viewModel.ChargeBoardVersionLabel, Is.EqualTo("充电板版本号"));
            Assert.That(_viewModel.ChipIdLabel, Is.EqualTo("芯片ID"));
            Assert.That(_viewModel.WifiMacLabel, Is.EqualTo("MAC地址"));
        }

        [Test]
        public void ProductFieldLabels_WhenNotConfigured_ShouldFallbackToDefaultLabels()
        {
            // Arrange
            _productRegistryMock.Setup(r => r.Get("SOLTAG25")).Returns(new ProductProfile
            {
                ProductCode = "SOLTAG25",
                Mode = VerificationMode.Legacy,
                FieldLabels = null
            });

            // Act
            _viewModel.SelectedProductCode = "SOLTAG25";

            // Assert
            Assert.That(_viewModel.DeviceSnLabel, Is.EqualTo("设备SN"));
            Assert.That(_viewModel.AndroidVersionLabel, Is.EqualTo("Android版本号"));
            Assert.That(_viewModel.BoardVersionLabel, Is.EqualTo("Board版本"));
            Assert.That(_viewModel.ChargeBoardVersionLabel, Is.EqualTo("充电板版本号"));
            Assert.That(_viewModel.ChipIdLabel, Is.EqualTo("芯片ID"));
            Assert.That(_viewModel.WifiMacLabel, Is.EqualTo("MAC地址"));
        }

        [Test]
        public async Task StartBatchAsync_ForLegacyProduct_ShouldNotOverwriteExpectedVersionSettings()
        {
            Settings.Default.LastExpectedAndroidVersion = "KEEP_A";
            Settings.Default.LastExpectedBoardVersion = "KEEP_B";
            Settings.Default.LastExpectedChargeBoardVersion = "KEEP_C";
            Settings.Default.Save();

            var sessionId = "ORDER_L_20260310_100000";
            var projectId = "SOLTAG25";
            var orderId = "ORDER_L";
            string validationMessage = null;
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);
            _sessionLifecycleServiceMock
                .Setup(s => s.CreateAndStartSession(orderId, orderId, projectId, It.IsAny<string>()))
                .Returns(sessionId);
            _sessionLifecycleServiceMock
                .SetupSequence(s => s.Snapshot)
                .Returns(SessionSnapshot.Idle())
                .Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "SOLTAG25" });
            _productRegistryMock.Setup(r => r.Get("SOLTAG25")).Returns(new ProductProfile
            {
                ProductCode = "SOLTAG25",
                Mode = VerificationMode.Legacy
            });

            _viewModel = new MainViewModel(
                _sessionLifecycleServiceMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _exportAggregationServiceMock.Object,
                _orderNameValidatorMock.Object,
                _dialogServiceMock.Object,
                _versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                _productRegistryMock.Object);

            _viewModel.SelectedProductCode = "SOLTAG25";
            _viewModel.ProjectIdInput = projectId;
            _viewModel.OrderIdInput = orderId;

            _viewModel.StartBatchCommand.Execute(null);
            await Task.Delay(150);

            Assert.That(Settings.Default.LastExpectedAndroidVersion, Is.EqualTo("KEEP_A"));
            Assert.That(Settings.Default.LastExpectedBoardVersion, Is.EqualTo("KEEP_B"));
            Assert.That(Settings.Default.LastExpectedChargeBoardVersion, Is.EqualTo("KEEP_C"));
        }

        [Test]
        public async Task EndBatchCommand_WhenNoTestRecordGenerated_ShouldStillEndSession_AndLogIgnoredMessage()
        {
            // Arrange：无记录时仍会结束 Session/日志，仅状态栏短暂提示并写「结束测试被忽略」日志
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(activeSessionSnapshot);
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = activeSessionSnapshot;

            _storageServiceMock
                .Setup(s => s.GetTestRecordsBySessionAsync(sessionId))
                .ReturnsAsync(Array.Empty<TestRecord>());

            // Act
            _viewModel.EndBatchCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.StatusBarMessage == "", timeoutMs: 2000);

            // Assert：无记录也执行结束流程，并记录「结束测试被忽略」日志
            _sessionLifecycleServiceMock.Verify(m => m.EndSession(), Times.Once);
            _loggingServiceMock.Verify(m => m.EndBatch(), Times.Once);
            _loggingServiceMock.Verify(m => m.LogInfo(It.Is<string>(s => s != null && s.Contains("结束测试被忽略"))), Times.Once);
        }

        [Test]
        public void StartBatchCommand_ShouldBeDisabled_WhenIsProcessing()
        {
            // Arrange
            _viewModel.VerificationSnapshot = VerificationSnapshot.Processing("SN");

            // Act
            var canExecute = _viewModel.StartBatchCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void EndBatchCommand_ShouldBeDisabled_WhenIsProcessing()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.VerificationSnapshot = VerificationSnapshot.Processing("SN");

            // Act
            var canExecute = _viewModel.EndBatchCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void StatusText_ShouldReturnWaiting_Initially()
        {
            Assert.That(_viewModel.StatusText, Is.EqualTo("等待检验"));
        }

        [Test]
        public void StatusText_ShouldReturnProcessing_WhenIsProcessing()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Processing("TEST_SN"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("正在检验..."));
        }

        [Test]
        public void StatusText_ShouldReturnPASS_WhenResultIsPASS()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("TEST_SN", "PASS", null, null, "TEST_SN"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("PASS"));
            Assert.That(_viewModel.DeviceSN, Is.EqualTo("TEST_SN"));
        }

        [Test]
        public void StatusText_ShouldReturnFAIL_WhenResultIsFAIL()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("TEST_SN", "FAIL", "MISMATCH", null, "DEVICE_SN"));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.StatusText, Is.EqualTo("FAIL"));
            Assert.That(_viewModel.ShowFailReason, Is.True);
            Assert.That(_viewModel.FailReason, Is.EqualTo("MISMATCH"));
            Assert.That(_viewModel.DeviceSN, Is.EqualTo("DEVICE_SN"));
        }

        [Test]
        public void DeviceSN_ShouldReturnEmpty_WhenSnapshotIsNull()
        {
            // Arrange
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle());

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.DeviceSN, Is.EqualTo(""));
        }

        [Test]
        public void DeviceSN_ShouldReturnValue_WhenCompletedWithDeviceSN()
        {
            // Arrange
            const string deviceSN = "DEVICE123";
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Completed("STICKER123", "PASS", null, null, deviceSN));

            // Act
            _viewModel.VerificationSnapshot = _verificationFlowServiceMock.Object.Snapshot;

            // Assert
            Assert.That(_viewModel.DeviceSN, Is.EqualTo(deviceSN));
            Assert.That(_viewModel.CurrentSn, Is.EqualTo("STICKER123"));
        }

        [Test]
        public void CurrentDeviceInfo_WhenSnapshotHasDeviceInfo_ReturnsFullDeviceInfo()
        {
            var deviceInfo = new DeviceInfo
            {
                DeviceSn = "SN1",
                ChipId = "C1",
                WifiMac = "W1",
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "Ch1"
            };
            var snapshot = VerificationSnapshot.Completed("sticker", "PASS", null, null, "SN1", deviceInfo);

            _viewModel.VerificationSnapshot = snapshot;

            Assert.That(_viewModel.CurrentDeviceInfo, Is.Not.Null);
            Assert.That(_viewModel.CurrentDeviceInfo.DeviceSn, Is.EqualTo("SN1"));
            Assert.That(_viewModel.CurrentDeviceInfo.ChipId, Is.EqualTo("C1"));
            Assert.That(_viewModel.CurrentDeviceInfo.WifiMac, Is.EqualTo("W1"));
            Assert.That(_viewModel.CurrentDeviceInfo.AndroidVersion, Is.EqualTo("A1"));
            Assert.That(_viewModel.CurrentDeviceInfo.BoardVersion, Is.EqualTo("B1"));
            Assert.That(_viewModel.CurrentDeviceInfo.ChargeBoardVersion, Is.EqualTo("Ch1"));
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldTriggerVerification_WhenValidInput()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(activeSessionSnapshot);
            _viewModel.SessionSnapshot = activeSessionSnapshot;
            
            _verificationFlowServiceMock.SetupSequence(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(testSn))
                .Returns(VerificationSnapshot.Completed(testSn, "PASS", null, null, testSn));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(testSn), Times.Once);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenIsProcessing()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _verificationFlowServiceMock.Setup(m => m.Snapshot)
                .Returns(VerificationSnapshot.Processing("ANOTHER_SN"));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenSessionNotActive()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldClearScanInputText_AfterCompletion()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(activeSessionSnapshot);
            _viewModel.SessionSnapshot = activeSessionSnapshot;
            _viewModel.ScanInputText = testSn;
            
            _verificationFlowServiceMock.SetupSequence(m => m.Snapshot)
                .Returns(VerificationSnapshot.Idle())
                .Returns(VerificationSnapshot.Processing(testSn))
                .Returns(VerificationSnapshot.Completed(testSn, "PASS", null, null, testSn));

            // Act
            await _viewModel.HandleScanInputAsync(testSn);

            // Assert
            Assert.That(_viewModel.ScanInputText, Is.Empty);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreEmptyInput()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));

            // Act
            await _viewModel.HandleScanInputAsync("");
            await _viewModel.HandleScanInputAsync("   ");

            // Assert
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task HandleScanInputAsync_ShouldIgnoreInput_WhenIsSelfChecking()
        {
            // Arrange
            var testSn = "TEST_SN_001";
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var tcs = new TaskCompletionSource<DeviceInfo>();
            var readStartedTcs = new TaskCompletionSource<bool>();
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny))
                .Returns(false);
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            _viewModel.SelectedProductCode = "KM001";
            _deviceAccessServiceMock
                .Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .Callback(() => readStartedTcs.TrySetResult(true))
                .Returns(tcs.Task);
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _viewModel.SessionSnapshot = _sessionLifecycleServiceMock.Object.Snapshot;

            // Act: start self-check and wait until VM enters self-checking state
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => readStartedTcs.Task.IsCompleted, timeoutMs: 2000);
            await WaitUntilAsync(() => _viewModel.IsSelfChecking);

            await _viewModel.HandleScanInputAsync(testSn);

            // Assert: should not trigger verification while self-checking
            _verificationFlowServiceMock.Verify(m => m.StartVerificationAsync(It.IsAny<string>()), Times.Never);

            // Cleanup: end self-check
            tcs.SetResult(new DeviceInfo { DeviceSn = "DEVICE_SN" });
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking);
        }

        [Test]
        public async Task Commands_ShouldBeDisabled_WhenIsSelfChecking()
        {
            // Arrange
            var tcs = new TaskCompletionSource<DeviceInfo>();
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny))
                .Returns(false);
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            _viewModel.SelectedProductCode = "KM001";
            _deviceAccessServiceMock.Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>())).Returns(tcs.Task);

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.IsSelfChecking);

            // Assert
            Assert.That(_viewModel.StartVerifyCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.StartBatchCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.EndBatchCommand.CanExecute(null), Is.False);
            // 新逻辑：导出仅在“开始测试→结束测试”期间禁用；自检期间（未开始测试）仍可导出
            Assert.That(_viewModel.ExportCommand.CanExecute(null), Is.True);

            // Cleanup
            tcs.SetResult(new DeviceInfo { DeviceSn = "DEVICE_SN" });
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking);
        }

        [Test]
        public async Task SelfCheckAsync_ForKm001_ShouldUseDeviceAccessService()
        {
            // Arrange
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny)).Returns(false);
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            _viewModel.SelectedProductCode = "KM001";
            _deviceAccessServiceMock
                .Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = "SN_KM001", AndroidVersion = "V_KM001" });

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking, timeoutMs: 2000);

            // Assert
            _deviceAccessServiceMock.Verify(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Once);
            _adbAccessServiceMock.Verify(m => m.ReadDeviceSnAsync(default), Times.Never);
            _loggingServiceMock.Verify(
                m => m.LogInfo(It.Is<string>(s => s != null && s.Contains("SN_KM001") && s.Contains("V_KM001"))),
                Times.AtLeastOnce);
        }

        [Test]
        public async Task SelfCheckAsync_ForSoltag25_ShouldUseDeviceAccessService_AndLogSn()
        {
            // Arrange
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny)).Returns(false);
            _productRegistryMock.Setup(r => r.Get("SOLTAG25")).Returns(new ProductProfile
            {
                ProductCode = "SOLTAG25",
                Mode = VerificationMode.Legacy
            });
            _viewModel.SelectedProductCode = "SOLTAG25";
            _deviceAccessServiceMock
                .Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = "SN_SOL", AndroidVersion = "VER_SOL" });

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking, timeoutMs: 2000);

            // Assert
            _deviceAccessServiceMock.Verify(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()), Times.Once);
            _loggingServiceMock.Verify(
                m => m.LogInfo(It.Is<string>(s => s != null && s.Contains("SN_SOL") && s.Contains("VER_SOL"))),
                Times.AtLeastOnce);
        }

        [Test]
        public async Task SelfCheckAsync_WhenAggregateProtocolInvalid_ShouldLogProtocolError()
        {
            // Arrange
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny)).Returns(false);
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            _viewModel.SelectedProductCode = "KM001";
            _deviceAccessServiceMock
                .Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ThrowsAsync(new AggregateProtocolException("协议字段数量错误"));

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking, timeoutMs: 2000);

            // Assert
            _loggingServiceMock.Verify(
                m => m.LogWarning(It.Is<string>(s => s != null && s.Contains("协议错误"))),
                Times.Once);
        }

        [Test]
        public async Task SelfCheckAsync_WhenMultipleDevices_ShouldLogWarning()
        {
            // Arrange
            var devices = new List<string> { "A", "B" };
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out devices)).Returns(true);
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            _viewModel.SelectedProductCode = "KM001";
            _deviceAccessServiceMock
                .Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>()))
                .ReturnsAsync(new DeviceInfo { DeviceSn = "SN_OK" });

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            await WaitUntilAsync(() => !_viewModel.IsSelfChecking, timeoutMs: 2000);

            // Assert
            _loggingServiceMock.As<SnVerify.Services.Logging.IFileLogger>().Verify(
                m => m.LogWarning(It.Is<string>(s => s != null && s.Contains("多台 ADB 设备"))),
                Times.Once);
        }

        [Test]
        public async Task StatusBarMessage_ShouldUpdate_WhenMesPostReportFailedEventRaised()
        {
            // Arrange
            var args = new MesEventArgs(MesEventType.ReportFailed, "MES 上报失败（不影响当前测试结果）", "S1", "O1");

            // Act
            _verificationFlowServiceMock.Raise(m => m.MesEventOccurred += null, args);

            // Assert
            await WaitUntilAsync(() => _viewModel.StatusBarMessage == "MES 上报失败（不影响当前测试结果）");
        }

        [Test]
        public void StartBatchCommand_ShouldBeDisabled_WhenVersionMatchAndTargetVersionEmpty()
        {
            // Arrange: VersionMatch 且目标版本为空时，开始测试按钮不可执行
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "";

            // Act
            var canExecute = _viewModel.StartBatchCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void StartBatchCommand_ShouldBeEnabled_WhenVersionMatchAndTargetVersionFilled()
        {
            // Arrange
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "1.0.0";

            // Act
            var canExecute = _viewModel.StartBatchCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.True);
        }

        [Test]
        public async Task StartBatchAsync_WhenVersionMatchAndTargetVersionEmpty_ShouldShowWarningAndNotCreateSession()
        {
            // Arrange: 防御性校验 - 即使命令被绕过，执行时也应弹窗并阻止
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "";
            _viewModel.ProjectIdInput = "PROJECT001";
            _viewModel.OrderIdInput = "ORDER001";

            // Act
            _viewModel.StartBatchCommand.Execute(null);
            await Task.Delay(200);

            // Assert: 应弹窗提示，不调用 CreateAndStartSession
            _dialogServiceMock.Verify(d => d.ShowWarning(It.Is<string>(s => s != null && (s.Contains("目标版本") || s.Contains("版本号"))), It.IsAny<string>()), Times.Once);
            _sessionLifecycleServiceMock.Verify(s => s.CreateAndStartSession(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task StartBatchCommand_ShouldCreateSession_WhenProjectIdAndOrderIdProvided()
        {
            // Arrange
            var projectId = "PROJECT001";
            var orderId = "ORDER001";
            var sessionId = "ORDER001_20250126_143000";
            var startTime = DateTime.Now;

            string validationMessage = null;
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);
            _sessionLifecycleServiceMock
                .Setup(s => s.CreateAndStartSession(orderId, orderId, projectId, It.IsAny<string>()))
                .Returns(sessionId);
            _sessionLifecycleServiceMock
                .SetupSequence(s => s.Snapshot)
                .Returns(SessionSnapshot.Idle())
                .Returns(SessionSnapshot.Active(sessionId, orderId, startTime));
            _flowServiceFactoryMock
                .Setup(f => f.Create(sessionId, orderId))
                .Returns(_verificationFlowServiceMock.Object);

            // Act
            _viewModel.ProjectIdInput = projectId;
            _viewModel.OrderIdInput = orderId;
            _viewModel.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.SessionSnapshot.IsActive, timeoutMs: 2000);

            // Assert
            _sessionLifecycleServiceMock.Verify(s => s.CreateAndStartSession(orderId, orderId, projectId, It.IsAny<string>()), Times.Once);
            _flowServiceFactoryMock.Verify(f => f.Create(sessionId, orderId), Times.Once);
            _loggingServiceMock.Verify(l => l.StartSession(sessionId), Times.Once);
        }

        /// <summary>
        /// Phase3 UI Guard：切换 ProductCode 时自动清空项目名与订单名。
        /// </summary>
        [Test]
        public void SelectedProductCode_WhenChanged_ClearsProjectIdAndOrderId()
        {
            _productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "A100", "B200" });
            _productRegistryMock.Setup(r => r.Get("A100")).Returns(new ProductProfile { ProductCode = "A100", Mode = VerificationMode.Legacy });
            _productRegistryMock.Setup(r => r.Get("B200")).Returns(new ProductProfile { ProductCode = "B200", Mode = VerificationMode.Legacy });
            _viewModel = new MainViewModel(
                _sessionLifecycleServiceMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _exportAggregationServiceMock.Object,
                _orderNameValidatorMock.Object,
                _dialogServiceMock.Object,
                _versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                _productRegistryMock.Object);

            _viewModel.SelectedProductCode = "A100";
            _viewModel.ProjectIdInput = "Proj1";
            _viewModel.OrderIdInput = "Ord1";

            _viewModel.SelectedProductCode = "B200";

            Assert.That(_viewModel.ProjectIdInput, Is.EqualTo(""));
            Assert.That(_viewModel.OrderIdInput, Is.EqualTo(""));
        }

        /// <summary>
        /// Phase3 UI Guard：仅大小写不同视为未变化，不清空项目名/订单名。
        /// </summary>
        [Test]
        public void SelectedProductCode_WhenChangedOnlyCase_DoesNotClearProjectIdAndOrderId()
        {
            _productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "KM001" });
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile { ProductCode = "KM001", Mode = VerificationMode.Phase3 });
            _viewModel = new MainViewModel(
                _sessionLifecycleServiceMock.Object,
                _flowServiceFactoryMock.Object,
                _loggingServiceMock.Object,
                _storageServiceMock.Object,
                _adbAccessServiceMock.Object,
                _exportAggregationServiceMock.Object,
                _orderNameValidatorMock.Object,
                _dialogServiceMock.Object,
                _versionVerificationFlowServiceMock.Object,
                Path.GetTempPath(),
                _productRegistryMock.Object);

            _viewModel.SelectedProductCode = "KM001";
            _viewModel.ProjectIdInput = "Proj1";
            _viewModel.OrderIdInput = "Ord1";

            _viewModel.SelectedProductCode = "km001";

            Assert.That(_viewModel.ProjectIdInput, Is.EqualTo("Proj1"));
            Assert.That(_viewModel.OrderIdInput, Is.EqualTo("Ord1"));
        }

        /// <summary>
        /// Phase3 UI Guard：项目名已存在且用户选择继续时，正常创建 Session。
        /// </summary>
        [Test]
        public async Task StartBatch_WhenProjectNameExists_AndUserConfirms_ContinuesAndCreatesSession()
        {
            var projectId = "EXISTING_PROJECT";
            var orderId = "ORDER001";
            var sessionId = "ORDER001_20250126_143000";
            string validationMessage = null;
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);
            _storageServiceMock.Setup(s => s.ProjectNameExistsAsync(projectId)).ReturnsAsync(true);
            _dialogServiceMock.Setup(d => d.Confirm(It.Is<string>(m => m != null && m.Contains("EXISTING_PROJECT")), It.IsAny<string>())).Returns(true);
            _sessionLifecycleServiceMock
                .Setup(s => s.CreateAndStartSession(orderId, orderId, projectId, It.IsAny<string>()))
                .Returns(sessionId);
            _sessionLifecycleServiceMock
                .SetupSequence(s => s.Snapshot)
                .Returns(SessionSnapshot.Idle())
                .Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _flowServiceFactoryMock.Setup(f => f.Create(sessionId, orderId)).Returns(_verificationFlowServiceMock.Object);

            _viewModel.ProjectIdInput = projectId;
            _viewModel.OrderIdInput = orderId;
            _viewModel.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.SessionSnapshot.IsActive, timeoutMs: 2000);

            _dialogServiceMock.Verify(d => d.Confirm(It.Is<string>(m => m != null && m.Contains("EXISTING_PROJECT")), It.IsAny<string>()), Times.Once);
            _sessionLifecycleServiceMock.Verify(s => s.CreateAndStartSession(orderId, orderId, projectId, It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Phase3 UI Guard：项目名已存在且用户取消时，不创建 Session。
        /// </summary>
        [Test]
        public async Task StartBatch_WhenProjectNameExists_AndUserCancels_DoesNotCreateSession()
        {
            var projectId = "EXISTING_PROJECT";
            var orderId = "ORDER001";
            string validationMessage = null;
            _orderNameValidatorMock.Setup(v => v.Validate(orderId, out validationMessage)).Returns(true);
            _storageServiceMock.Setup(s => s.ProjectNameExistsAsync(projectId)).ReturnsAsync(true);
            _dialogServiceMock.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

            _viewModel.ProjectIdInput = projectId;
            _viewModel.OrderIdInput = orderId;
            _viewModel.StartBatchCommand.Execute(null);
            await Task.Delay(300);

            _dialogServiceMock.Verify(d => d.Confirm(It.Is<string>(m => m != null && m.Contains("EXISTING_PROJECT")), It.IsAny<string>()), Times.Once);
            _sessionLifecycleServiceMock.Verify(s => s.CreateAndStartSession(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Session 激活时尝试切换 ProductCode 应被忽略：ProductCode 保持不变，项目名/订单名不被清空。
        /// </summary>
        [Test]
        public void SelectedProductCode_WhenSessionActive_ShouldIgnoreChanges_AndNotClearProjectOrder()
        {
            // Arrange
            _viewModel.SelectedProductCode = "SOLTAG25";
            _viewModel.ProjectIdInput = "PROJECT1";
            _viewModel.OrderIdInput = "ORDER1";

            var sessionId = "ORDER1_20260310_100000";
            var orderId = "ORDER1";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);

            // Act
            _viewModel.SelectedProductCode = "KM001";

            // Assert
            Assert.That(_viewModel.SelectedProductCode, Is.EqualTo("SOLTAG25"));
            Assert.That(_viewModel.ProjectIdInput, Is.EqualTo("PROJECT1"));
            Assert.That(_viewModel.OrderIdInput, Is.EqualTo("ORDER1"));
        }

        [Test]
        public void IsVersionInputEnabled_ShouldBeFalse_WhenSessionBecomesActive()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Idle();

            // Act
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);

            // Assert
            Assert.That(_viewModel.IsVersionInputEnabled, Is.False);
        }

        [Test]
        public void Commands_ShouldBeDisabled_WhenSessionIsActive()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var activeSessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);

            // Act
            _viewModel.SessionSnapshot = activeSessionSnapshot;

            // Assert
            Assert.That(_viewModel.StartBatchCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.EndBatchCommand.CanExecute(null), Is.True);
            Assert.That(_viewModel.ExportCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task ExportAsync_WhenTargetZipNotExists_ShouldExportWithoutOverwritePrompt()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderX", ProductId = 1 };

                // 默认构造的 MainViewModel 使用的是空 ProductProfile（非 Phase3），因此应走 Legacy 路径并弹出内容类型选择页
                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                _dialogServiceMock.Setup(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()))
                    .Returns(SnVerify.Domain.Export.ExportRecordFilter.All);

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                var tcs = new TaskCompletionSource<bool>();
                _exportAggregationServiceMock
                    .Setup(s => s.ExportByOrderIdAsync("OrderX", exportRoot, It.IsAny<SnVerify.Domain.Export.ExportRecordFilter>()))
                    .Returns(() =>
                    {
                        tcs.TrySetResult(true);
                        return Task.CompletedTask;
                    });

                // Act
                _viewModel.ExportCommand.Execute(null);
                await WaitUntilAsync(() => tcs.Task.IsCompleted);

                // Assert
                _dialogServiceMock.Verify(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()), Times.Once);
                _dialogServiceMock.Verify(d => d.ConfirmOverwrite(It.IsAny<string>()), Times.Never);
                _exportAggregationServiceMock.Verify(s => s.ExportByOrderIdAsync("OrderX", exportRoot, It.IsAny<SnVerify.Domain.Export.ExportRecordFilter>()), Times.Once);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }

        [Test]
        public async Task ExportAsync_WhenTargetZipExists_AndUserCancelsOverwrite_ShouldNotExportAndLogInfo()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);
            var zipPath = Path.Combine(exportRoot, "OrderY.zip");
            File.WriteAllText(zipPath, "ORIGINAL");

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderY", ProductId = 1 };

                // 默认构造的 MainViewModel 使用的是空 ProductProfile（非 Phase3），因此应走 Legacy 路径并弹出内容类型选择页
                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                _dialogServiceMock.Setup(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()))
                    .Returns(SnVerify.Domain.Export.ExportRecordFilter.All);

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                _dialogServiceMock.Setup(d => d.ConfirmOverwrite(It.IsAny<string>()))
                    .Returns(false);

                // Act
                _viewModel.ExportCommand.Execute(null);
                // 等待一小段时间让异步导出逻辑完成
                await Task.Delay(100);

                // Assert
                _dialogServiceMock.Verify(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()), Times.Once);
                _exportAggregationServiceMock.Verify(s => s.ExportByOrderIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SnVerify.Domain.Export.ExportRecordFilter>()), Times.Never);
                _loggingServiceMock.Verify(l => l.LogInfo(It.Is<string>(m => m.Contains("导出已取消"))), Times.AtLeastOnce);
                Assert.That(File.Exists(zipPath), Is.True, "当用户取消覆盖时，原 ZIP 文件应保持不变");
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }

        [Test]
        public void CurrentVerificationType_ShouldDefaultToSnMatch()
        {
            Assert.That(_viewModel.CurrentVerificationType, Is.EqualTo(VerificationType.SnMatch));
        }

        [Test]
        public void CurrentVerificationType_WhenSet_ShouldRaisePropertyChanged()
        {
            var raised = false;
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentVerificationType))
                    raised = true;
            };
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            Assert.That(raised, Is.True);
            Assert.That(_viewModel.CurrentVerificationType, Is.EqualTo(VerificationType.VersionMatch));
        }

        [Test]
        public void IsVerificationTypeComboBoxEnabled_ShouldBeTrue_WhenSessionNotActive()
        {
            _viewModel.SessionSnapshot = SessionSnapshot.Idle();
            Assert.That(_viewModel.IsVerificationTypeComboBoxEnabled, Is.True);
        }

        [Test]
        public void IsVerificationTypeComboBoxEnabled_ShouldBeFalse_WhenSessionActive()
        {
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            Assert.That(_viewModel.IsVerificationTypeComboBoxEnabled, Is.False);
        }

        [Test]
        public void IsScanInputVisible_ShouldBeTrue_WhenCurrentVerificationTypeIsSnMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            Assert.That(_viewModel.IsScanInputVisible, Is.True);
        }

        [Test]
        public void IsScanInputVisible_ShouldBeFalse_WhenCurrentVerificationTypeIsVersionMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            Assert.That(_viewModel.IsScanInputVisible, Is.False);
        }

        [Test]
        public void IsVersionInputVisible_ShouldBeTrue_WhenCurrentVerificationTypeIsVersionMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            Assert.That(_viewModel.IsVersionInputVisible, Is.True);
        }

        [Test]
        public void IsVersionInputVisible_ShouldBeFalse_WhenCurrentVerificationTypeIsSnMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            Assert.That(_viewModel.IsVersionInputVisible, Is.False);
        }

        [Test]
        public void IsSnInfoVisible_ShouldBeTrue_WhenSnMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            Assert.That(_viewModel.IsSnInfoVisible, Is.True);
        }

        [Test]
        public void IsSnInfoVisible_ShouldBeFalse_WhenVersionMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            Assert.That(_viewModel.IsSnInfoVisible, Is.False);
        }

        [Test]
        public void IsVersionInfoVisible_ShouldBeFalse_WhenSnMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            Assert.That(_viewModel.IsVersionInfoVisible, Is.False);
        }

        [Test]
        public void IsVersionInfoVisible_ShouldBeTrue_WhenVersionMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            Assert.That(_viewModel.IsVersionInfoVisible, Is.True);
        }

        [Test]
        public void ActualDeviceVersionDisplay_ShouldReturnPlaceholder_WhenVersionMatchAndNoRecord()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            // _lastVersionRecord is null by default
            Assert.That(_viewModel.ActualDeviceVersionDisplay, Is.EqualTo("--"));
        }

        [Test]
        public async Task ActualDeviceVersionDisplay_ShouldReturnActualVersion_WhenVersionMatchAndHasRecord()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            // 通过反射或内部更新设置 _lastVersionRecord - MainViewModel 没有 public setter
            // 需要调用 StartVersionVerifyAsync 或通过 VerificationSnapshot 更新触发
            // 我们通过执行版本检验来设置
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var session = new TestSession
            {
                Id = 1,
                SessionName = sessionId,
                OrderId = 1,
                ExpectedVersion = "1.0.0",
                StartTime = DateTime.Now
            };
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _storageServiceMock.Setup(s => s.GetSessionBySessionNameAsync(sessionId)).ReturnsAsync(session);
            var record = new TestRecord
            {
                SessionId = 1,
                Result = "PASS",
                ActualVersion = "1.0.0",
                ExpectedVersion = "1.0.0",
                VerifyTime = DateTime.Now
            };
            _versionVerificationFlowServiceMock
                .Setup(m => m.ExecuteVersionCheckAsync(It.IsAny<TestSession>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);

            _viewModel.TargetVersionInput = "1.0.0";
            _viewModel.StartVersionVerifyCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.ActualDeviceVersionDisplay == "1.0.0", timeoutMs: 2000);

            Assert.That(_viewModel.ActualDeviceVersionDisplay, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void ExpectedVersionDisplay_ShouldReturnTargetVersionInput_WhenVersionMatch()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "2.0.1";
            Assert.That(_viewModel.ExpectedVersionDisplay, Is.EqualTo("2.0.1"));
        }

        [Test]
        public void ExpectedVersionDisplay_ShouldReturnPlaceholder_WhenVersionMatchAndEmpty()
        {
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "";
            Assert.That(_viewModel.ExpectedVersionDisplay, Is.EqualTo("--"));
        }

        [Test]
        public void DeviceSN_ShouldNotShowVersionWhenSwitchedToSnMatch_AfterVersionTest()
        {
            // 版本检验后切换到 SnMatch，设备SN 区域应显示空而非版本号
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var session = new TestSession { Id = 1, SessionName = sessionId, OrderId = 1, ExpectedVersion = "1.0.0", StartTime = DateTime.Now };

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _storageServiceMock.Setup(s => s.GetSessionBySessionNameAsync(sessionId)).ReturnsAsync(session);

            var record = new TestRecord { SessionId = 1, Result = "PASS", ActualVersion = "1.0.0", ExpectedVersion = "1.0.0", VerifyTime = DateTime.Now };
            _versionVerificationFlowServiceMock.Setup(m => m.ExecuteVersionCheckAsync(It.IsAny<TestSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Completed("--", "PASS", null, sessionId, "1.0.0"));

            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "1.0.0";
            _viewModel.StartVersionVerifyCommand.Execute(null);
            System.Threading.Thread.Sleep(500); // 等待异步完成

            Assert.That(_viewModel.ActualDeviceVersionDisplay, Is.EqualTo("1.0.0"), "VersionMatch 时设备版本应显示版本号");

            // 模拟结束测试并切换到 SnMatch
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            _viewModel.SessionSnapshot = SessionSnapshot.Idle();
            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            _verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());

            // 触发快照更新（模拟定时器）
            for (int i = 0; i < 3; i++)
            {
                System.Threading.Thread.Sleep(600); // 等待 UpdateSnapshotsInternal
            }

            // SnMatch 模式下 DeviceSN 应来自 SN 流程，不应显示版本号
            Assert.That(_viewModel.DeviceSN, Is.EqualTo(""), "切换到 SnMatch 后设备SN 应为空");
        }

        [Test]
        public void SwitchingVerificationType_ShouldUpdateVisibilityPropertiesSynchronously()
        {
            // 切换 VerificationType 时，UI 可见性属性同步更新
            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            Assert.That(_viewModel.IsSnInfoVisible, Is.True);
            Assert.That(_viewModel.IsVersionInfoVisible, Is.False);

            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            Assert.That(_viewModel.IsSnInfoVisible, Is.False);
            Assert.That(_viewModel.IsVersionInfoVisible, Is.True);

            _viewModel.CurrentVerificationType = VerificationType.SnMatch;
            Assert.That(_viewModel.IsSnInfoVisible, Is.True);
            Assert.That(_viewModel.IsVersionInfoVisible, Is.False);
        }

        [Test]
        public void StartVersionVerifyCommand_ShouldBeDisabled_WhenVersionMatchAndTargetVersionEmpty()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "";

            // Act
            var canExecute = _viewModel.StartVersionVerifyCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void StartVersionVerifyCommand_ShouldBeEnabled_WhenVersionMatchAndTargetVersionFilled()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = "1.0.0";

            // Act
            var canExecute = _viewModel.StartVersionVerifyCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.True);
        }

        [Test]
        public void StartVersionVerifyCommand_ShouldBeDisabled_WhenSessionNotActive()
        {
            // Arrange: Session 未激活
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            _viewModel.SessionSnapshot = SessionSnapshot.Idle();

            // Act
            var canExecute = _viewModel.StartVersionVerifyCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void StartVersionVerifyCommand_ShouldBeDisabled_WhenIsProcessing()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.VerificationSnapshot = VerificationSnapshot.Processing("SN");

            // Act
            var canExecute = _viewModel.StartVersionVerifyCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);
        }

        [Test]
        public void StartVersionVerifyCommand_ShouldBeDisabled_WhenIsSelfChecking()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            var tcs = new TaskCompletionSource<DeviceInfo>();
            _adbAccessServiceMock.Setup(m => m.CheckMultipleDevices(out It.Ref<List<string>>.IsAny)).Returns(false);
            _productRegistryMock.Setup(r => r.Get("KM001")).Returns(new ProductProfile
            {
                ProductCode = "KM001",
                Mode = VerificationMode.Phase3
            });
            _viewModel.SelectedProductCode = "KM001";
            _deviceAccessServiceMock.Setup(m => m.ReadDeviceInfoAsync(It.IsAny<ProductProfile>())).Returns(tcs.Task);

            // Act
            _viewModel.SelfCheckCommand.Execute(null);
            var canExecute = _viewModel.StartVersionVerifyCommand.CanExecute(null);

            // Assert
            Assert.That(canExecute, Is.False);

            // Cleanup
            tcs.SetResult(new DeviceInfo { DeviceSn = "DEVICE_SN" });
        }

        [Test]
        public async Task StartVersionVerifyAsync_WhenSessionNotActive_ShouldLogAndSetBatchError()
        {
            // Arrange
            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns((string)null);
            _viewModel.SessionSnapshot = SessionSnapshot.Idle();

            // Act
            _viewModel.StartVersionVerifyCommand.Execute(null);
            await Task.Delay(150);

            // Assert
            _loggingServiceMock.Verify(m => m.LogWarning(It.Is<string>(s => s != null && s.Contains("Session 未激活"))), Times.Once);
            _versionVerificationFlowServiceMock.Verify(m => m.ExecuteVersionCheckAsync(It.IsAny<TestSession>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task StartVersionVerifyAsync_WhenExecuteReturnsPASS_ShouldUpdateSnapshotAndLastVersionRecord()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var expectedVersion = "1.0.0";
            var actualVersion = "1.0.0";
            var session = new TestSession
            {
                Id = 1,
                SessionName = sessionId,
                OrderId = 1,
                ExpectedVersion = expectedVersion,
                StartTime = DateTime.Now
            };

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = expectedVersion;
            _storageServiceMock.Setup(s => s.GetSessionBySessionNameAsync(sessionId)).ReturnsAsync(session);

            var passRecord = new TestRecord
            {
                SessionId = 1,
                Result = "PASS",
                ActualVersion = actualVersion,
                ExpectedVersion = expectedVersion,
                VerifyTime = DateTime.Now
            };
            _versionVerificationFlowServiceMock
                .Setup(m => m.ExecuteVersionCheckAsync(It.IsAny<TestSession>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(passRecord);
            _versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Completed("--", "PASS", null, sessionId, actualVersion));

            // Act
            _viewModel.StartVersionVerifyCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.VerificationSnapshot?.LastResult == "PASS", timeoutMs: 2000);

            // Assert
            Assert.That(_viewModel.VerificationSnapshot.LastResult, Is.EqualTo("PASS"));
            Assert.That(_viewModel.VerificationSnapshot.DeviceSN, Is.EqualTo(actualVersion));
            Assert.That(_viewModel.LastVersionRecord, Is.Not.Null);
            Assert.That(_viewModel.LastVersionRecord.Result, Is.EqualTo("PASS"));
            Assert.That(_viewModel.LastVersionRecord.ActualVersion, Is.EqualTo(actualVersion));
        }

        [Test]
        public async Task StartVersionVerifyAsync_WhenExecuteReturnsFAIL_ShouldUpdateSnapshotWithFailReason()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var expectedVersion = "1.0.0";
            var actualVersion = "1.0.1";
            var failReason = "Version mismatch: expected 1.0.0, actual 1.0.1";
            var session = new TestSession
            {
                Id = 1,
                SessionName = sessionId,
                OrderId = 1,
                ExpectedVersion = expectedVersion,
                StartTime = DateTime.Now
            };

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.CurrentVerificationType = VerificationType.VersionMatch;
            _viewModel.TargetVersionInput = expectedVersion;
            _storageServiceMock.Setup(s => s.GetSessionBySessionNameAsync(sessionId)).ReturnsAsync(session);

            var failRecord = new TestRecord
            {
                SessionId = 1,
                Result = "FAIL",
                FailReason = failReason,
                ActualVersion = actualVersion,
                ExpectedVersion = expectedVersion,
                VerifyTime = DateTime.Now
            };
            _versionVerificationFlowServiceMock
                .Setup(m => m.ExecuteVersionCheckAsync(It.IsAny<TestSession>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(failRecord);
            _versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Completed("--", "FAIL", failReason, sessionId, actualVersion));

            // Act
            _viewModel.StartVersionVerifyCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.VerificationSnapshot?.LastResult == "FAIL", timeoutMs: 2000);

            // Assert
            Assert.That(_viewModel.VerificationSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_viewModel.VerificationSnapshot.FailReason, Is.EqualTo(failReason));
            Assert.That(_viewModel.LastVersionRecord.FailReason, Is.EqualTo(failReason));
        }

        [Test]
        public async Task StartVersionVerifyAsync_WhenNoExpectedVersion_ShouldUseTargetVersionInput()
        {
            // Arrange: Session 无 ExpectedVersion，使用 TargetVersionInput
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var targetVersion = "2.0.0";
            var session = new TestSession
            {
                Id = 1,
                SessionName = sessionId,
                OrderId = 1,
                ExpectedVersion = null,
                StartTime = DateTime.Now
            };

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _viewModel.TargetVersionInput = targetVersion;
            _storageServiceMock.Setup(s => s.GetSessionBySessionNameAsync(sessionId)).ReturnsAsync(session);

            var passRecord = new TestRecord
            {
                SessionId = 1,
                Result = "PASS",
                ActualVersion = targetVersion,
                ExpectedVersion = targetVersion,
                VerifyTime = DateTime.Now
            };
            _versionVerificationFlowServiceMock
                .Setup(m => m.ExecuteVersionCheckAsync(It.Is<TestSession>(s => s.ExpectedVersion == targetVersion), It.IsAny<CancellationToken>()))
                .ReturnsAsync(passRecord);
            _versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Completed("--", "PASS", null, sessionId, targetVersion));

            // Act
            _viewModel.StartVersionVerifyCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.LastVersionRecord != null, timeoutMs: 2000);

            // Assert
            _versionVerificationFlowServiceMock.Verify(m => m.ExecuteVersionCheckAsync(It.Is<TestSession>(s => s.ExpectedVersion == targetVersion), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task StartVersionVerifyAsync_WhenExceptionThrown_ShouldSetUiStateFailAndLogError()
        {
            // Arrange
            var sessionId = "ORDER001_20250126_143000";
            var orderId = "ORDER001";
            var session = new TestSession
            {
                Id = 1,
                SessionName = sessionId,
                OrderId = 1,
                ExpectedVersion = "1.0.0",
                StartTime = DateTime.Now
            };

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Active(sessionId, orderId, DateTime.Now));
            _sessionLifecycleServiceMock.Setup(m => m.GetCurrentSessionId()).Returns(sessionId);
            _viewModel.SessionSnapshot = SessionSnapshot.Active(sessionId, orderId, DateTime.Now);
            _storageServiceMock.Setup(s => s.GetSessionBySessionNameAsync(sessionId)).ReturnsAsync(session);

            _versionVerificationFlowServiceMock
                .Setup(m => m.ExecuteVersionCheckAsync(It.IsAny<TestSession>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("ADB 连接失败"));

            // Act
            _viewModel.StartVersionVerifyCommand.Execute(null);
            await WaitUntilAsync(() => _viewModel.VerificationSnapshot?.LastResult == "FAIL", timeoutMs: 2000);

            // Assert
            Assert.That(_viewModel.VerificationSnapshot.LastResult, Is.EqualTo("FAIL"));
            Assert.That(_viewModel.UiState, Is.EqualTo(VerificationUiState.Fail));
            _loggingServiceMock.Verify(m => m.LogError(It.Is<string>(s => s != null && s.Contains("ADB")), It.IsAny<Exception>()), Times.Once);
        }

        [Test]
        public async Task ExportAsync_WhenTargetZipExists_AndUserConfirmsOverwrite_ShouldDeleteAndExport()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);
            var zipPath = Path.Combine(exportRoot, "OrderZ.zip");
            File.WriteAllText(zipPath, "ORIGINAL");

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderZ", ProductId = 1 };

                // 默认构造的 MainViewModel 使用的是空 ProductProfile（非 Phase3），因此应走 Legacy 路径并弹出内容类型选择页
                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                _dialogServiceMock.Setup(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()))
                    .Returns(SnVerify.Domain.Export.ExportRecordFilter.All);

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                _dialogServiceMock.Setup(d => d.ConfirmOverwrite(It.IsAny<string>()))
                    .Returns(true);

                var tcs = new TaskCompletionSource<bool>();
                _exportAggregationServiceMock
                    .Setup(s => s.ExportByOrderIdAsync("OrderZ", exportRoot, It.IsAny<SnVerify.Domain.Export.ExportRecordFilter>()))
                    .Callback(() =>
                    {
                        // 在调用导出服务之前，ZIP 应已被删除
                        Assert.That(File.Exists(zipPath), Is.False);
                    })
                    .Returns(() =>
                    {
                        tcs.TrySetResult(true);
                        return Task.CompletedTask;
                    });

                // Act
                _viewModel.ExportCommand.Execute(null);
                await WaitUntilAsync(() => tcs.Task.IsCompleted);

                // Assert
                _dialogServiceMock.Verify(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()), Times.Once);
                _dialogServiceMock.Verify(d => d.ConfirmOverwrite(It.IsAny<string>()), Times.Once);
                _exportAggregationServiceMock.Verify(s => s.ExportByOrderIdAsync("OrderZ", exportRoot, It.IsAny<SnVerify.Domain.Export.ExportRecordFilter>()), Times.Once);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }

        [Test]
        public async Task ExportAsync_ForPhase3Product_ShouldSkipRecordFilterDialog_AndUseAllFilter()
        {
            // Arrange
            var exportRoot = Path.Combine(Path.GetTempPath(), "SnVerify_Export_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);

            try
            {
                var order = new Order { Id = 1, OrderName = "OrderKM", ProductId = 1 };

                // 配置当前产品为 Phase3（例如 KM001）
                var phase3Profile = new ProductProfile
                {
                    ProductCode = "KM001",
                    ProductDisplayName = "KM001",
                    Mode = VerificationMode.Phase3
                };
                _productRegistryMock.Setup(r => r.GetProductCodes()).Returns(new[] { "KM001" });
                _productRegistryMock.Setup(r => r.Get("KM001")).Returns(phase3Profile);

                // 重新构造 ViewModel，让其加载 Phase3 产品配置
                _viewModel = new MainViewModel(
                    _sessionLifecycleServiceMock.Object,
                    _flowServiceFactoryMock.Object,
                    _loggingServiceMock.Object,
                    _storageServiceMock.Object,
                    _adbAccessServiceMock.Object,
                    _exportAggregationServiceMock.Object,
                    _orderNameValidatorMock.Object,
                    _dialogServiceMock.Object,
                    _versionVerificationFlowServiceMock.Object,
                    Path.GetTempPath(),
                    _productRegistryMock.Object,
                    parameterService: null,
                    deviceAccessService: _deviceAccessServiceMock.Object);

                _viewModel.SelectedProductCode = "KM001";

                _dialogServiceMock.Setup(d => d.ChooseExportDimension())
                    .Returns(ExportDimension.ByOrder);

                // Phase3 场景下应跳过 ChooseExportRecordFilter，这里如果被调用会抛异常以暴露错误
                _dialogServiceMock
                    .Setup(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()))
                    .Throws(new Exception("ChooseExportRecordFilter should not be called for Phase3 product"));

                _storageServiceMock.Setup(s => s.GetAllOrdersAsync())
                    .ReturnsAsync(new[] { order });

                _dialogServiceMock.Setup(d => d.ChooseOrder(It.IsAny<IReadOnlyList<Order>>()))
                    .Returns(order);

                _dialogServiceMock.Setup(d => d.ChooseFolder(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(exportRoot);

                var tcs = new TaskCompletionSource<bool>();
                _exportAggregationServiceMock
                    .Setup(s => s.ExportByOrderIdAsync(
                        "OrderKM",
                        exportRoot,
                        It.Is<SnVerify.Domain.Export.ExportRecordFilter>(f => f == SnVerify.Domain.Export.ExportRecordFilter.All)))
                    .Returns(() =>
                    {
                        tcs.TrySetResult(true);
                        return Task.CompletedTask;
                    });

                // Act
                _viewModel.ExportCommand.Execute(null);
                await WaitUntilAsync(() => tcs.Task.IsCompleted);

                // Assert
                _dialogServiceMock.Verify(d => d.ChooseExportDimension(), Times.Once);
                _dialogServiceMock.Verify(d => d.ChooseExportRecordFilter(It.IsAny<IReadOnlyList<VerificationType>>()), Times.Never);
                _exportAggregationServiceMock.Verify(
                    s => s.ExportByOrderIdAsync(
                        "OrderKM",
                        exportRoot,
                        It.Is<SnVerify.Domain.Export.ExportRecordFilter>(f => f == SnVerify.Domain.Export.ExportRecordFilter.All)),
                    Times.Once);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }
    }
}
