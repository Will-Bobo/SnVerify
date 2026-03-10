using System;
using NUnit.Framework;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Services.Rules;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Tests.Services.Storage.Export
{
    [TestFixture]
    public class DefaultExportValueResolverTests
    {
        private DefaultExportValueResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _resolver = new DefaultExportValueResolver();
        }

        [Test]
        public void Resolve_NullRecord_Returns_EmptyString()
        {
            Assert.That(_resolver.Resolve(ExportFieldId.Id, null), Is.EqualTo(""));
            Assert.That(_resolver.Resolve(ExportFieldId.StickerSn, null), Is.EqualTo(""));
        }

        [Test]
        public void Resolve_Id_Returns_RecordId()
        {
            var record = new TestRecord { Id = 42 };
            Assert.That(_resolver.Resolve(ExportFieldId.Id, record), Is.EqualTo("42"));
        }

        [Test]
        public void Resolve_StickerSn_DeviceSn_WifiMac_ChipId()
        {
            var record = new TestRecord
            {
                StickerSN = "S1",
                DeviceSN = "D1",
                WifiMac = "AA:BB",
                ChipId = "F50XXX"
            };
            Assert.That(_resolver.Resolve(ExportFieldId.StickerSn, record), Is.EqualTo("S1"));
            Assert.That(_resolver.Resolve(ExportFieldId.DeviceSn, record), Is.EqualTo("D1"));
            Assert.That(_resolver.Resolve(ExportFieldId.WifiMac, record), Is.EqualTo("AA:BB"));
            Assert.That(_resolver.Resolve(ExportFieldId.ChipId, record), Is.EqualTo("F50XXX"));
        }

        [Test]
        public void Resolve_Version_And_Board_Fields()
        {
            var record = new TestRecord
            {
                ExpectedVersion = "11",
                ActualVersion = "12",
                ExpectedBoardVersion = "B1",
                BoardVersion = "B2",
                ExpectedChargeBoardVersion = "C1",
                ChargeBoardVersion = "C2"
            };
            Assert.That(_resolver.Resolve(ExportFieldId.ExpectedVersion, record), Is.EqualTo("11"));
            Assert.That(_resolver.Resolve(ExportFieldId.ActualVersion, record), Is.EqualTo("12"));
            Assert.That(_resolver.Resolve(ExportFieldId.ExpectedBoardVersion, record), Is.EqualTo("B1"));
            Assert.That(_resolver.Resolve(ExportFieldId.ActualBoardVersion, record), Is.EqualTo("B2"));
            Assert.That(_resolver.Resolve(ExportFieldId.ExpectedChargeBoardVersion, record), Is.EqualTo("C1"));
            Assert.That(_resolver.Resolve(ExportFieldId.ActualChargeBoardVersion, record), Is.EqualTo("C2"));
        }

        [Test]
        public void Resolve_Result_ErrorDetail_Uses_FailReasonTextResolver()
        {
            var code = RuleFailReasonCodes.SnNotMatch;
            var record = new TestRecord { Result = "PASS", FailReason = code };
            var expectedText = FailReasonTextResolver.Resolve(code);
            Assert.That(_resolver.Resolve(ExportFieldId.Result, record), Is.EqualTo("PASS"));
            Assert.That(_resolver.Resolve(ExportFieldId.ErrorDetail, record), Is.EqualTo(expectedText));
        }

        [Test]
        public void Resolve_VerificationTime_Formatted()
        {
            var dt = new DateTime(2026, 3, 4, 12, 30, 45);
            var record = new TestRecord { VerifyTime = dt };
            Assert.That(_resolver.Resolve(ExportFieldId.VerificationTime, record), Is.EqualTo("2026年3月4日 12:30:45"));
        }

        [Test]
        public void Resolve_VerificationTime_Default_Returns_Empty()
        {
            var record = new TestRecord { VerifyTime = default };
            Assert.That(_resolver.Resolve(ExportFieldId.VerificationTime, record), Is.EqualTo(""));
        }

        [Test]
        public void Resolve_Null_String_Fields_Return_Empty()
        {
            var record = new TestRecord();
            Assert.That(_resolver.Resolve(ExportFieldId.StickerSn, record), Is.EqualTo(""));
            Assert.That(_resolver.Resolve(ExportFieldId.DeviceSn, record), Is.EqualTo(""));
            Assert.That(_resolver.Resolve(ExportFieldId.ErrorDetail, record), Is.EqualTo(""));
        }
    }
}
