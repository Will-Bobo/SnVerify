/// <author>AI Assistant</author>
/// <remarks>
/// ProductRegistry 单元测试（Stage3 Step1）。
/// 验证产品代码到 Profile 的映射与属性配置。
/// </remarks>

using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Product;
using SnVerify.Infrastructure.Product;

namespace SnVerify.Tests.Infrastructure
{
    /// <summary>
    /// ProductRegistry 行为测试。
    /// </summary>
    [TestFixture]
    public class ProductRegistryTests
    {
        [Test]
        public void Get_ShouldReturnProfile_ForKnownProductCodes()
        {
            var soltag25 = ProductRegistry.Get("SOLTAG25");
            var km001 = ProductRegistry.Get("KM001");

            Assert.That(soltag25, Is.Not.Null, "SOLTAG25 Profile 应存在");
            Assert.That(km001, Is.Not.Null, "KM001 Profile 应存在");
        }

        [Test]
        public void Soltag25_Profile_ShouldMatchLegacySpec()
        {
            var profile = ProductRegistry.Get("SOLTAG25");
            Assert.That(profile, Is.Not.Null);

            Assert.That(profile.ProductCode, Is.EqualTo("SOLTAG25"));
            Assert.That(profile.Mode, Is.EqualTo(VerificationMode.Legacy));
            Assert.That(profile.EnableChipIdCheck, Is.False);
            Assert.That(profile.EnableWifiMacCheck, Is.False);
            Assert.That(profile.EnableBoardVersionCheck, Is.False);
            Assert.That(profile.EnableChargeBoardVersionCheck, Is.False);

            Assert.That(profile.EnableAndroidVersionCheck, Is.True, "SOLTAG25 应启用 Android 版本合一检验");
            Assert.That(profile.AdbConfig, Is.Not.Null, "SOLTAG25 需要显式 ADB 配置用于自检");
            Assert.That(profile.AdbConfig.AggregateCommand, Is.Null);
            Assert.That(profile.AdbConfig.BootstrapCommandSpecs, Is.Not.Null);
            Assert.That(profile.AdbConfig.BootstrapCommandSpecs.Count, Is.EqualTo(1));
            Assert.That(profile.AdbConfig.BootstrapCommandSpecs[0].Command, Is.EqualTo("shell ylzero"));
            Assert.That(profile.AdbConfig.BootstrapCommandSpecs[0].TimeoutBehavior, Is.EqualTo(BootstrapTimeoutBehavior.Fail));
            Assert.That(profile.AdbConfig.BootstrapCommandSpecs[0].AcceptableExitCodes, Is.EqualTo(new[] { 127, 255 }));

            Assert.That(profile.AdbConfig.Commands, Is.Not.Null);
            Assert.That(profile.AdbConfig.Commands.Count, Is.EqualTo(2));
            Assert.That(profile.AdbConfig.Commands[0].Field, Is.EqualTo(DeviceInfoField.DeviceSn));
            Assert.That(profile.AdbConfig.Commands[0].Command, Is.EqualTo("shell getprop sys.skyroam.osi.sn"));
            Assert.That(profile.AdbConfig.Commands[0].ParserKey, Is.EqualTo(ParserKeys.Field.Trim));
            Assert.That(profile.AdbConfig.Commands[1].Field, Is.EqualTo(DeviceInfoField.AndroidVersion));
            Assert.That(profile.AdbConfig.Commands[1].Command, Is.EqualTo("shell getprop ro.build.display.id"));
            Assert.That(profile.AdbConfig.Commands[1].ParserKey, Is.EqualTo(ParserKeys.Field.Trim));
        }

        [Test]
        public void Km001_Profile_ShouldMatchPhase3Spec()
        {
            var profile = ProductRegistry.Get("KM001");
            Assert.That(profile, Is.Not.Null);

            Assert.That(profile.ProductCode, Is.EqualTo("KM001"));
            Assert.That(profile.Mode, Is.EqualTo(VerificationMode.Phase3));
            Assert.That(profile.EnableChipIdCheck, Is.True);
            Assert.That(profile.EnableWifiMacCheck, Is.True);
            Assert.That(profile.EnableBoardVersionCheck, Is.True);
            Assert.That(profile.EnableChargeBoardVersionCheck, Is.True);

            Assert.That(profile.AdbConfig, Is.Not.Null);
            Assert.That(profile.AdbConfig.BootstrapCommandSpecs, Is.Null, "KM001 已切换为单聚合命令，不再执行 bootstrap");
            Assert.That(profile.AdbConfig.AggregateCommand, Is.Not.Null, "KM001 应配置聚合命令");
            Assert.That(profile.AdbConfig.AggregateCommand.Command, Is.EqualTo("shell dumpsys window getmcuversion"));
            Assert.That(profile.AdbConfig.AggregateCommand.ParserKey, Is.EqualTo(ParserKeys.Aggregate.Km001McuVersion));
            Assert.That(profile.AdbConfig.Commands, Is.Null, "聚合命令与字段命令不可混配");

            Assert.That(profile.FieldLabels, Is.Not.Null, "KM001 需配置字段标签映射");
            Assert.That(profile.FieldLabels.ContainsKey(DeviceInfoField.BoardVersion), Is.True);
            Assert.That(profile.FieldLabels[DeviceInfoField.BoardVersion], Is.EqualTo("芯片版本号"));
            Assert.That(profile.FieldLabels[DeviceInfoField.AndroidVersion], Is.EqualTo("Android版本号"));
            Assert.That(profile.FieldLabels[DeviceInfoField.ChargeBoardVersion], Is.EqualTo("充电板版本号"));
            Assert.That(profile.FieldLabels[DeviceInfoField.ChipId], Is.EqualTo("芯片ID"));
            Assert.That(profile.FieldLabels[DeviceInfoField.WifiMac], Is.EqualTo("MAC地址"));
        }

        [Test]
        public void Km008_Profile_ShouldUseKm008Parser_AndDisableChipBoardChecks()
        {
            var profile = ProductRegistry.Get("KM008");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Mode, Is.EqualTo(VerificationMode.Phase3));
            Assert.That(profile.EnableChipIdCheck, Is.False);
            Assert.That(profile.EnableBoardVersionCheck, Is.False);
            Assert.That(profile.EnableWifiMacCheck, Is.True);
            Assert.That(profile.AdbConfig.AggregateCommand.ParserKey, Is.EqualTo(ParserKeys.Aggregate.Km008AndroidVersion));
            Assert.That(profile.AdbConfig.AggregateCommand.Command, Is.EqualTo("shell dumpsys window getmcuversion"));
        }

        [Test]
        public void GetProductCodes_ShouldContainAllRegisteredCodes()
        {
            var codes = ProductRegistry.GetProductCodes();

            Assert.That(codes, Does.Contain("SOLTAG25"));
            Assert.That(codes, Does.Contain("KM001"));
            Assert.That(codes, Does.Contain("KM008"));
        }
    }
}

