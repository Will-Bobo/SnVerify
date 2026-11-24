using NUnit.Framework;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009AttributeParserTests
    {
        [Test]
        public void Should_DefineExactlyEightAttributes_When_CatalogIsRead()
        {
            Assert.That(Yme009AttributeCatalog.All, Has.Count.EqualTo(8));
            Assert.That(Yme009AttributeCatalog.All[0].PropertyKey, Is.EqualTo("sys.skyroam.osi.sn"));
            Assert.That(Yme009AttributeCatalog.All[0].Role, Is.EqualTo(Yme009FieldRole.RequiredText));
            Assert.That(Yme009AttributeCatalog.All[1].PropertyKey, Is.EqualTo("ro.build.display.id"));
            Assert.That(Yme009AttributeCatalog.All[1].Role, Is.EqualTo(Yme009FieldRole.RequiredText));
            Assert.That(Yme009AttributeCatalog.All[2].PropertyKey, Is.EqualTo("sys.skyroam.imei1"));
            Assert.That(Yme009AttributeCatalog.All[2].Role, Is.EqualTo(Yme009FieldRole.Information));
            Assert.That(Yme009AttributeCatalog.All[3].PropertyKey, Is.EqualTo("sys.skyroam.imei2"));
            Assert.That(Yme009AttributeCatalog.All[3].Role, Is.EqualTo(Yme009FieldRole.Information));
            Assert.That(Yme009AttributeCatalog.All[4].PropertyKey, Is.EqualTo("persist.sys.wifi.mac"));
            Assert.That(Yme009AttributeCatalog.All[4].Role, Is.EqualTo(Yme009FieldRole.Information));
            Assert.That(Yme009AttributeCatalog.All[5].PropertyKey, Is.EqualTo("persist.sys.yl.cit"));
            Assert.That(Yme009AttributeCatalog.All[5].Role, Is.EqualTo(Yme009FieldRole.RequiredBinary));
            Assert.That(Yme009AttributeCatalog.All[6].PropertyKey, Is.EqualTo("persist.sys.yl.aging.reboot"));
            Assert.That(Yme009AttributeCatalog.All[6].Role, Is.EqualTo(Yme009FieldRole.RequiredBinary));
            Assert.That(Yme009AttributeCatalog.All[7].PropertyKey, Is.EqualTo("persist.sys.yl.aging.reset"));
            Assert.That(Yme009AttributeCatalog.All[7].Role, Is.EqualTo(Yme009FieldRole.RequiredBinary));
            Assert.That(Yme009AttributeCatalog.All[2].ParticipatesInJudgment, Is.False);
            Assert.That(Yme009AttributeCatalog.All[0].ParticipatesInJudgment, Is.True);
        }

        [Test]
        public void Should_ParseEachFieldIndependently_When_ValuesAreNormal()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder().Build());

            AssertField(parsed.DeviceSn, Yme009ReadStatus.Ok, "SN1", Yme009FieldRole.RequiredText);
            AssertField(parsed.SoftwareVersion, Yme009ReadStatus.Ok, "SW-1", Yme009FieldRole.RequiredText);
            AssertField(parsed.Imei1, Yme009ReadStatus.Ok, "IMEI1", Yme009FieldRole.Information);
            AssertField(parsed.Imei2, Yme009ReadStatus.Ok, "IMEI2", Yme009FieldRole.Information);
            AssertField(parsed.WifiMac, Yme009ReadStatus.Ok, "AA:BB", Yme009FieldRole.Information);
            AssertField(parsed.ProductionTest, Yme009ReadStatus.Ok, "1", Yme009FieldRole.RequiredBinary);
            AssertField(parsed.AgingReboot, Yme009ReadStatus.Ok, "1", Yme009FieldRole.RequiredBinary);
            AssertField(parsed.AgingReset, Yme009ReadStatus.Ok, "1", Yme009FieldRole.RequiredBinary);
        }

        [Test]
        public void Should_StripOnlyTrailingLineEndings_When_CommandSucceeds()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded(" SN1\r\n\n"))
                .SoftwareVersion(Yme009FieldRaw.Succeeded("SW-1\n"))
                .ProductionTest(Yme009FieldRaw.Succeeded("1\r\n"))
                .AgingReboot(Yme009FieldRaw.Succeeded("0\n\r"))
                .Build());

            Assert.That(parsed.DeviceSn.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.DeviceSn.Text, Is.EqualTo(" SN1"));
            Assert.That(parsed.SoftwareVersion.Text, Is.EqualTo("SW-1"));
            Assert.That(parsed.ProductionTest.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.ProductionTest.Text, Is.EqualTo("1"));
            Assert.That(parsed.AgingReboot.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.AgingReboot.Text, Is.EqualTo("0"));
        }

        [Test]
        public void Should_ClassifyEmptyRequiredTextAsMissing_When_CommandSucceeds()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("\r\n"))
                .SoftwareVersion(Yme009FieldRaw.Succeeded(null))
                .Build());

            Assert.That(parsed.DeviceSn.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.DeviceSn.Text, Is.EqualTo(string.Empty));
            Assert.That(parsed.SoftwareVersion.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.Imei1.Status, Is.EqualTo(Yme009ReadStatus.Ok));
        }

        [Test]
        public void Should_KeepWhitespaceOnlyTextAsPresent_When_CommandSucceeds()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("   \n"))
                .Build());

            Assert.That(parsed.DeviceSn.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.DeviceSn.Text, Is.EqualTo("   "));
        }

        [Test]
        public void Should_ClassifyEmptyBinaryAsMissing_When_CommandSucceeds()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded(string.Empty))
                .AgingReboot(Yme009FieldRaw.Succeeded("\r\n"))
                .Build());

            Assert.That(parsed.ProductionTest.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.ProductionTest.Text, Is.EqualTo(string.Empty));
            Assert.That(parsed.AgingReboot.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.AgingReboot.Text, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Should_ClassifyIllegalBinaryAsProtocolInvalid_When_CommandSucceeds()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .AgingReboot(Yme009FieldRaw.Succeeded("2"))
                .AgingReset(Yme009FieldRaw.Succeeded(" 1"))
                .Build());

            Assert.That(parsed.AgingReboot.Status, Is.EqualTo(Yme009ReadStatus.ProtocolInvalid));
            Assert.That(parsed.AgingReboot.Text, Is.EqualTo("2"));
            Assert.That(parsed.AgingReset.Status, Is.EqualTo(Yme009ReadStatus.ProtocolInvalid));
            Assert.That(parsed.AgingReset.Text, Is.EqualTo(" 1"));
            Assert.That(parsed.ProductionTest.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.DeviceSn.Status, Is.EqualTo(Yme009ReadStatus.Ok));
        }

        [Test]
        public void Should_NotUseProtocolInvalid_When_TextOrInformationValueIsUnusual()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("2"))
                .Imei1(Yme009FieldRaw.Succeeded("0"))
                .WifiMac(Yme009FieldRaw.Succeeded("not-a-mac"))
                .Build());

            Assert.That(parsed.DeviceSn.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.Imei1.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.WifiMac.Status, Is.EqualTo(Yme009ReadStatus.Ok));
        }

        [Test]
        public void Should_ClassifyInformationEmptyAsMissing_When_CommandSucceeds()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .Imei1(Yme009FieldRaw.Succeeded(string.Empty))
                .Imei2(Yme009FieldRaw.Succeeded("\n"))
                .WifiMac(Yme009FieldRaw.Succeeded("\r\n"))
                .Build());

            Assert.That(parsed.Imei1.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.Imei2.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.WifiMac.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(parsed.ProductionTest.Status, Is.EqualTo(Yme009ReadStatus.Ok));
        }

        [Test]
        public void Should_DiscardOutput_When_CommandDoesNotSucceed()
        {
            var parsed = Yme009AttributeParser.Parse(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.CommandFailed())
                .SoftwareVersion(Yme009FieldRaw.TimedOut())
                .ProductionTest(Yme009FieldRaw.Disconnected())
                .Imei1(Yme009FieldRaw.CommandFailed())
                .Build());

            Assert.That(parsed.DeviceSn.Status, Is.EqualTo(Yme009ReadStatus.CommandFailed));
            Assert.That(parsed.DeviceSn.Text, Is.EqualTo(string.Empty));
            Assert.That(parsed.SoftwareVersion.Status, Is.EqualTo(Yme009ReadStatus.TimedOut));
            Assert.That(parsed.ProductionTest.Status, Is.EqualTo(Yme009ReadStatus.Disconnected));
            Assert.That(parsed.Imei1.Status, Is.EqualTo(Yme009ReadStatus.CommandFailed));
            Assert.That(parsed.Imei2.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.Imei2.Text, Is.EqualTo("IMEI2"));
            Assert.That(parsed.AgingReboot.Status, Is.EqualTo(Yme009ReadStatus.Ok));
            Assert.That(parsed.AgingReset.Text, Is.EqualTo("1"));
        }

        [Test]
        public void Should_LeaveLeadingNewlineInPlace_When_StrippingTrailingLineEndings()
        {
            Assert.That(Yme009AttributeParser.StripTrailingLineEndings("\n1"), Is.EqualTo("\n1"));
            Assert.That(Yme009AttributeParser.StripTrailingLineEndings("1 \n"), Is.EqualTo("1 "));
        }

        private static void AssertField(
            Yme009ParsedField field,
            Yme009ReadStatus status,
            string text,
            Yme009FieldRole role)
        {
            Assert.That(field.Status, Is.EqualTo(status));
            Assert.That(field.Text, Is.EqualTo(text));
            Assert.That(field.Role, Is.EqualTo(role));
        }
    }
}
