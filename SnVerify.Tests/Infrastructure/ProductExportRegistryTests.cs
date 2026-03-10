using NUnit.Framework;
using SnVerify.Domain.Export;
using SnVerify.Infrastructure.Export;

namespace SnVerify.Tests.Infrastructure
{
    [TestFixture]
    public class ProductExportRegistryTests
    {
        [Test]
        public void GetProfile_KM001_Returns_Profile_With_14_RecordColumns()
        {
            var registry = new ProductExportRegistry();
            var profile = registry.GetProfile("KM001");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.ProductCode, Is.EqualTo("KM001"));
            Assert.That(profile.RecordColumns, Is.Not.Null);
            Assert.That(profile.RecordColumns.Count, Is.EqualTo(14));
        }

        [Test]
        public void GetProfile_KM001_HasSummarySheet_True()
        {
            var registry = new ProductExportRegistry();
            var profile = registry.GetProfile("KM001");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.HasSummarySheet, Is.True);
        }

        [Test]
        public void GetProfile_km001_IgnoreCase_Returns_Same_Profile()
        {
            var registry = new ProductExportRegistry();
            var profile = registry.GetProfile("km001");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.ProductCode, Is.EqualTo("KM001"));
            Assert.That(profile.RecordColumns.Count, Is.EqualTo(14));
        }

        [Test]
        public void GetProfile_Unknown_Returns_Null()
        {
            var registry = new ProductExportRegistry();
            Assert.That(registry.GetProfile("UNKNOWN"), Is.Null);
            Assert.That(registry.GetProfile("Legacy"), Is.Null);
        }

        [Test]
        public void GetProfile_NullOrEmpty_Returns_Null()
        {
            var registry = new ProductExportRegistry();
            Assert.That(registry.GetProfile(null), Is.Null);
            Assert.That(registry.GetProfile(""), Is.Null);
            Assert.That(registry.GetProfile("   "), Is.Null);
        }

        [Test]
        public void KM001_RecordColumns_Order_Matches_ExportFieldId_Sequence()
        {
            var registry = new ProductExportRegistry();
            var profile = registry.GetProfile("KM001");
            var expectedOrder = new[]
            {
                ExportFieldId.Id,
                ExportFieldId.StickerSn,
                ExportFieldId.DeviceSn,
                ExportFieldId.WifiMac,
                ExportFieldId.ChipId,
                ExportFieldId.ExpectedBoardVersion,
                ExportFieldId.ActualBoardVersion,
                ExportFieldId.ExpectedChargeBoardVersion,
                ExportFieldId.ActualChargeBoardVersion,
                ExportFieldId.Result,
                ExportFieldId.ErrorDetail,
                ExportFieldId.VerificationTime,
                ExportFieldId.ExpectedVersion,
                ExportFieldId.ActualVersion
            };
            for (int i = 0; i < expectedOrder.Length; i++)
                Assert.That(profile.RecordColumns[i].FieldId, Is.EqualTo(expectedOrder[i]), $"Column index {i}");
        }
    }
}
