using NUnit.Framework;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009ExpectedIdentityComparisonTests
    {
        private const string Sn = "SN-001";
        private const string Mac = "48:C8:62:16:BF:1F";
        private const string Imei1 = "111111111111111";
        private const string Imei2 = "222222222222222";

        [Test]
        public void Should_Pass_When_AllFourFieldsMatch()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(Expected(), Actual(Sn, Mac, Imei1, Imei2));

            Assert.That(result.Passed, Is.True);
            Assert.That(result.ActualValueMissing, Is.False);
            Assert.That(result.Differences, Is.Empty);
        }

        [Test]
        public void Should_Pass_When_SnAndImeiDifferOnlyByOuterSpace()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(
                Expected(),
                Actual("  " + Sn + " ", Mac, " " + Imei1, Imei2 + "\t"));

            Assert.That(result.Passed, Is.True);
            Assert.That(result.Differences, Is.Empty);
        }

        [Test]
        public void Should_Fail_When_SnDiffers()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(Expected(), Actual("SN-002", Mac, Imei1, Imei2));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences, Has.Count.EqualTo(1));
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.Sn));
            Assert.That(result.Differences[0].ActualValueMissing, Is.False);
        }

        [Test]
        public void Should_Fail_When_Imei1Differs()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(Expected(), Actual(Sn, Mac, "111111111111112", Imei2));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.Imei1));
            Assert.That(result.Differences[0].Expected, Is.EqualTo(Imei1));
            Assert.That(result.Differences[0].Actual, Is.EqualTo("111111111111112"));
        }

        [Test]
        public void Should_Fail_When_Imei2Differs()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(Expected(), Actual(Sn, Mac, Imei1, "222222222222223"));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.Imei2));
        }

        [Test]
        public void Should_Fail_When_ImeiCaseDiffers()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(
                new Yme009ExpectedIdentity(Sn, Mac, "Ab1", Imei2),
                Actual(Sn, Mac, "ab1", Imei2));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.Imei1));
        }

        [Test]
        public void Should_Pass_When_WifiMacDiffersOnlyByCaseAndColon()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(
                Expected(),
                Actual(Sn, "  48c86216bf1f  ", Imei1, Imei2));

            Assert.That(result.Passed, Is.True);
            Assert.That(result.Differences, Is.Empty);
        }

        [Test]
        public void Should_Fail_When_WifiMacUsesHyphen()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(
                Expected(),
                Actual(Sn, "48-C8-62-16-BF-1F", Imei1, Imei2));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.WifiMac));
            Assert.That(result.Differences[0].Actual, Does.Contain("-"));
        }

        [Test]
        public void Should_Fail_When_WifiMacUsesDot()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(
                Expected(),
                Actual(Sn, "48.C8.62.16.BF.1F", Imei1, Imei2));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.WifiMac));
            Assert.That(result.Differences[0].Actual, Does.Contain("."));
        }

        [Test]
        public void Should_Fail_When_ActualValueIsMissingOrIneffective()
        {
            AssertMissing(Actual(null, Mac, Imei1, Imei2), Yme009ExpectedFields.Sn);
            AssertMissing(Actual("   ", Mac, Imei1, Imei2), Yme009ExpectedFields.Sn);
            AssertMissing(Actual(Sn, null, Imei1, Imei2), Yme009ExpectedFields.WifiMac);
            AssertMissing(Actual(Sn, "::::", Imei1, Imei2), Yme009ExpectedFields.WifiMac);
            AssertMissing(Actual(Sn, Mac, string.Empty, Imei2), Yme009ExpectedFields.Imei1);
            AssertMissing(Actual(Sn, Mac, Imei1, "\t"), Yme009ExpectedFields.Imei2);
        }

        [Test]
        public void Should_RemainCaseSensitive_When_ComparingSn()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(Expected(), Actual("sn-001", Mac, Imei1, Imei2));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.Sn));
            Assert.That(result.Differences[0].Actual, Is.EqualTo("sn-001"));
        }

        [Test]
        public void Should_ListEveryMismatchedField_When_SeveralFieldsDiffer()
        {
            var result = Yme009ExpectedIdentityComparison.Compare(
                Expected(),
                Actual("OTHER", "00-11", "1", null));

            Assert.That(result.Passed, Is.False);
            Assert.That(result.ActualValueMissing, Is.True);
            Assert.That(result.Differences, Has.Count.EqualTo(4));
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(Yme009ExpectedFields.Sn));
            Assert.That(result.Differences[1].FieldName, Is.EqualTo(Yme009ExpectedFields.WifiMac));
            Assert.That(result.Differences[2].FieldName, Is.EqualTo(Yme009ExpectedFields.Imei1));
            Assert.That(result.Differences[3].FieldName, Is.EqualTo(Yme009ExpectedFields.Imei2));
            Assert.That(result.Differences[3].ActualValueMissing, Is.True);
        }

        private static void AssertMissing(Yme009ExpectedIdentity actual, string fieldName)
        {
            var result = Yme009ExpectedIdentityComparison.Compare(Expected(), actual);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.ActualValueMissing, Is.True);
            Assert.That(result.Differences[0].FieldName, Is.EqualTo(fieldName));
            Assert.That(result.Differences[0].ActualValueMissing, Is.True);
        }

        private static Yme009ExpectedIdentity Expected()
        {
            return new Yme009ExpectedIdentity(" " + Sn + " ", " " + Mac + " ", Imei1, Imei2);
        }

        private static Yme009ExpectedIdentity Actual(string sn, string wifiMac, string imei1, string imei2)
        {
            return new Yme009ExpectedIdentity(sn, wifiMac, imei1, imei2);
        }
    }
}
