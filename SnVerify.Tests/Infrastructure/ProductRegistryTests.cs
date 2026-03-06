/// <author>AI Assistant</author>
/// <remarks>
/// ProductRegistry 单元测试（Stage3 Step1）。
/// 验证产品代码到 Profile 的映射与属性配置。
/// </remarks>

using NUnit.Framework;
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

            Assert.That(profile.AdbCommands, Is.Not.Null);
            Assert.That(profile.AdbCommands.ReadDeviceSn, Is.EqualTo("getprop sys.skyroam.osi.sn"));
            Assert.That(profile.AdbCommands.ReadAndroidVersion, Is.EqualTo("getprop ro.build.display.id"));
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

            Assert.That(profile.AdbCommands, Is.Not.Null);
            // Phase3 允许 ADB 命令为空占位，这里只验证对象存在即可。
        }

        [Test]
        public void GetProductCodes_ShouldContainAllRegisteredCodes()
        {
            var codes = ProductRegistry.GetProductCodes();

            Assert.That(codes, Does.Contain("SOLTAG25"));
            Assert.That(codes, Does.Contain("KM001"));
        }
    }
}

