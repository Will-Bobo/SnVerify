using NUnit.Framework;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009TextComparisonTests
    {
        [Test]
        public void Should_TreatOuterWhitespaceAsEqual_When_Comparing()
        {
            Assert.That(Yme009TextComparison.Equals(" SW-1 ", "SW-1"), Is.True);
            Assert.That(Yme009TextComparison.Equals("SW-1\t", "SW-1"), Is.True);
            Assert.That(Yme009TextComparison.Normalize("  SW-1  "), Is.EqualTo("SW-1"));
        }

        [Test]
        public void Should_RemainCaseSensitive_When_Comparing()
        {
            Assert.That(Yme009TextComparison.Equals("sw-1", "SW-1"), Is.False);
            Assert.That(Yme009TextComparison.Equals("Sn1", "SN1"), Is.False);
        }

        [Test]
        public void Should_KeepInternalDifferences_When_Comparing()
        {
            Assert.That(Yme009TextComparison.Equals("SW 1", "SW1"), Is.False);
            Assert.That(Yme009TextComparison.Equals("SW-1", "SW-1 "), Is.True);
            Assert.That(Yme009TextComparison.Equals("A-B", "A_B"), Is.False);
        }

        [Test]
        public void Should_TreatNullAndWhitespaceAsEmpty_When_Normalizing()
        {
            Assert.That(Yme009TextComparison.Normalize(null), Is.EqualTo(string.Empty));
            Assert.That(Yme009TextComparison.Equals(null, "   "), Is.True);
            Assert.That(Yme009TextComparison.Equals(null, "SW-1"), Is.False);
            Assert.That(Yme009TextComparison.Equals(string.Empty, "SW-1"), Is.False);
        }
    }
}
