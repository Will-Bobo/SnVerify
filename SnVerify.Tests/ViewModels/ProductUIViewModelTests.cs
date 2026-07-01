/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step2：Product UI(ViewModel) 单元测试。
/// </remarks>

using System;
using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Domain.State;
using SnVerify.Domain.Validation;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
using SnVerify.ViewModels;

namespace SnVerify.Tests.ViewModels
{
    /// <summary>
    /// 产品选择相关 ViewModel 行为测试（不触发校验流程、不调用 Coordinator）。
    /// </summary>
    [TestFixture]
    public class ProductUIViewModelTests
    {
        private Mock<ISessionLifecycleService> _sessionLifecycleServiceMock;
        private Mock<IVerificationFlowServiceFactory> _flowServiceFactoryMock;
        private Mock<IVerificationFlowService> _verificationFlowServiceMock;
        private Mock<IVersionVerificationFlowService> _versionVerificationFlowServiceMock;
        private Mock<ILoggingService> _loggingServiceMock;
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IAdbAccessService> _adbAccessServiceMock;
        private Mock<IExportAggregationService> _exportAggregationServiceMock;
        private Mock<IOrderNameValidator> _orderNameValidatorMock;
        private Mock<IUserDialogService> _dialogServiceMock;
        private Mock<IProductRegistry> _productRegistryMock;

        private MainViewModel CreateViewModel()
        {
            _sessionLifecycleServiceMock = new Mock<ISessionLifecycleService>();
            _flowServiceFactoryMock = new Mock<IVerificationFlowServiceFactory>();
            _verificationFlowServiceMock = new Mock<IVerificationFlowService>();
            _versionVerificationFlowServiceMock = new Mock<IVersionVerificationFlowService>();
            _loggingServiceMock = new Mock<ILoggingService>();
            _storageServiceMock = new Mock<IStorageService>();
            _adbAccessServiceMock = new Mock<IAdbAccessService>();
            _exportAggregationServiceMock = new Mock<IExportAggregationService>();
            _orderNameValidatorMock = new Mock<IOrderNameValidator>();
            _dialogServiceMock = new Mock<IUserDialogService>();
            _productRegistryMock = new Mock<IProductRegistry>();

            _sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            _verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            _versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            _flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(_verificationFlowServiceMock.Object);
            _loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());

            _productRegistryMock
                .Setup(r => r.GetProductCodes())
                .Returns(new List<string> { "SOLTAG25", "KM001" });

            _productRegistryMock
                .Setup(r => r.Get("SOLTAG25"))
                .Returns(new ProductProfile { ProductCode = "SOLTAG25", Mode = VerificationMode.Legacy, AdbConfig = null });

            _productRegistryMock
                .Setup(r => r.Get("KM001"))
                .Returns(new ProductProfile { ProductCode = "KM001", Mode = VerificationMode.Phase3, AdbConfig = null });

            return new MainViewModel(
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
        }

        [Test]
        public void Constructor_ShouldLoadAvailableProducts_FromRegistry()
        {
            var vm = CreateViewModel();

            Assert.That(vm.AvailableProducts, Is.Not.Null);
            Assert.That(vm.AvailableProducts, Does.Contain("SOLTAG25"));
            Assert.That(vm.AvailableProducts, Does.Contain("KM001"));
        }

        [Test]
        public void SelectedProductCode_Changed_ShouldUpdateCurrentProductDisplay()
        {
            var vm = CreateViewModel();

            vm.SelectedProductCode = "KM001";

            Assert.That(vm.CurrentProductDisplay, Is.EqualTo("KM001 [Phase3模式]"));
        }

        [Test]
        public void WhenSessionActive_ProductCode_ShouldNotBeModifiable()
        {
            var vm = CreateViewModel();

            vm.SelectedProductCode = "SOLTAG25";
            var before = vm.SelectedProductCode;
            var beforeDisplay = vm.CurrentProductDisplay;

            // Session 启动后应禁止修改 ProductCode
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "O1", DateTime.Now);
            Assert.That(vm.IsProductCodeComboBoxEnabled, Is.False);

            vm.SelectedProductCode = "KM001";

            Assert.That(vm.SelectedProductCode, Is.EqualTo(before));
            Assert.That(vm.CurrentProductDisplay, Is.EqualTo(beforeDisplay));
        }
    }
}

