/// <author>AI Assistant</author>
/// <remarks>
/// Stage3：Product 驱动 UI 渲染单元测试。
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
    /// Product 驱动 UI 相关的 ViewModel 行为测试。
    /// </summary>
    [TestFixture]
    public class ProductUIRenderingTests
    {
        private MainViewModel CreateViewModel(IProductRegistry productRegistry)
        {
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

            sessionLifecycleServiceMock.Setup(m => m.Snapshot).Returns(SessionSnapshot.Idle());
            verificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            versionVerificationFlowServiceMock.Setup(m => m.Snapshot).Returns(VerificationSnapshot.Idle());
            flowServiceFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>())).Returns(verificationFlowServiceMock.Object);
            loggingServiceMock.Setup(m => m.Snapshot).Returns(LoggingSnapshot.Idle());

            return new MainViewModel(
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
                productRegistry);
        }

        private static IProductRegistry CreateRegistryStub()
        {
            var mock = new Mock<IProductRegistry>();

            mock.Setup(r => r.GetProductCodes())
                .Returns(new List<string> { "SOLTAG25", "KM001" });

            mock.Setup(r => r.Get("SOLTAG25"))
                .Returns(new ProductProfile
                {
                    ProductCode = "SOLTAG25",
                    ProductName = "SOLTAG25",
                    Mode = VerificationMode.Legacy,
                    AdbConfig = null
                });

            mock.Setup(r => r.Get("KM001"))
                .Returns(new ProductProfile
                {
                    ProductCode = "KM001",
                    ProductName = "KM001",
                    Mode = VerificationMode.Phase3,
                    AdbConfig = null
                });

            mock.Setup(r => r.GetProductProfile(It.IsAny<string>()))
                .Returns<string>(code => mock.Object.Get(code));

            return mock.Object;
        }

        [Test]
        public void SelectedProductCode_ShouldDriveLegacyAndPhase3Flags()
        {
            var registry = CreateRegistryStub();
            var vm = CreateViewModel(registry);

            vm.SelectedProductCode = "SOLTAG25";
            Assert.That(vm.IsLegacyProduct, Is.True);
            Assert.That(vm.IsPhase3Product, Is.False);

            vm.SelectedProductCode = "KM001";
            Assert.That(vm.IsLegacyProduct, Is.False);
            Assert.That(vm.IsPhase3Product, Is.True);
        }

        [Test]
        public void SessionActive_ShouldPreventChangingProductCode()
        {
            var registry = CreateRegistryStub();
            var vm = CreateViewModel(registry);

            vm.SelectedProductCode = "SOLTAG25";
            var before = vm.SelectedProductCode;

            // Session 激活后禁止修改 ProductCode
            vm.SessionSnapshot = SessionSnapshot.Active("S1", "O1", DateTime.Now);
            vm.SelectedProductCode = "KM001";

            Assert.That(vm.SelectedProductCode, Is.EqualTo(before));
        }
    }
}

