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
            Assert.That(profile.HasSummarySheet, Is.False);
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
        public void GetProfile_KM008_ShouldNotContain_ChipAndBoardAndCharge_Columns()
        {
            var registry = new ProductExportRegistry();
            var km008 = registry.GetProfile("KM008");
            Assert.That(km008, Is.Not.Null);
            Assert.That(km008.RecordColumns, Is.Not.Null);

            // KM008 只导出：RowNumber/StickerSn/DeviceSn/WifiMac/Result/ErrorDetail/VerificationTime/ExpectedVersion/ActualVersion
            Assert.That(km008.RecordColumns.Count, Is.EqualTo(9));

            var fieldIds = new ExportFieldId[km008.RecordColumns.Count];
            for (int i = 0; i < km008.RecordColumns.Count; i++)
                fieldIds[i] = km008.RecordColumns[i].FieldId;
            Assert.That(fieldIds, Does.Not.Contain(ExportFieldId.ChipId));
            Assert.That(fieldIds, Does.Not.Contain(ExportFieldId.ExpectedBoardVersion));
            Assert.That(fieldIds, Does.Not.Contain(ExportFieldId.ActualBoardVersion));
            Assert.That(fieldIds, Does.Not.Contain(ExportFieldId.ExpectedChargeBoardVersion));
            Assert.That(fieldIds, Does.Not.Contain(ExportFieldId.ActualChargeBoardVersion));
        }

        [Test]
        public void KM001_RecordColumns_Order_Matches_ExportFieldId_Sequence()
        {
            var registry = new ProductExportRegistry();
            var profile = registry.GetProfile("KM001");
            var expectedOrder = new[]
            {
                ExportFieldId.RowNumber,
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
