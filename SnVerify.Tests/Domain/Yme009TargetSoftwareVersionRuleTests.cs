using System.Collections.Generic;
using NUnit.Framework;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009TargetSoftwareVersionRuleTests
    {
        [Test]
        public void Should_Allow_When_NoEstablishedTargetSoftwareVersion()
        {
            var decision = Yme009TargetSoftwareVersionRule.Evaluate(" SW-1 ", new List<string>());

            Assert.That(decision.IsAllowed, Is.True);
            Assert.That(decision.Rejection, Is.EqualTo(Yme009TargetSoftwareVersionRejection.None));
            Assert.That(decision.NormalizedRequestedVersion, Is.EqualTo("SW-1"));
        }

        [Test]
        public void Should_Reject_When_TargetSoftwareVersionMissing()
        {
            var decision = Yme009TargetSoftwareVersionRule.Evaluate("  ", null);

            Assert.That(decision.IsAllowed, Is.False);
            Assert.That(decision.Rejection, Is.EqualTo(Yme009TargetSoftwareVersionRejection.Missing));
        }

        [Test]
        public void Should_Allow_When_ExistingTargetSoftwareVersionMatches()
        {
            var existing = new List<string> { " SW-1 ", null, "   ", "SW-1" };

            var decision = Yme009TargetSoftwareVersionRule.Evaluate("SW-1", existing);

            Assert.That(decision.IsAllowed, Is.True);
            Assert.That(decision.NormalizedRequestedVersion, Is.EqualTo("SW-1"));
        }

        [Test]
        public void Should_Reject_When_ExistingTargetSoftwareVersionDiffers()
        {
            var decision = Yme009TargetSoftwareVersionRule.Evaluate("SW-2", new[] { "SW-1" });

            Assert.That(decision.IsAllowed, Is.False);
            Assert.That(decision.Rejection, Is.EqualTo(Yme009TargetSoftwareVersionRejection.Conflict));
            Assert.That(decision.EstablishedVersion, Is.EqualTo("SW-1"));
        }

        [Test]
        public void Should_Reject_When_ExistingTargetSoftwareVersionDiffersByCase()
        {
            var decision = Yme009TargetSoftwareVersionRule.Evaluate("sw-1", new[] { "SW-1" });

            Assert.That(decision.IsAllowed, Is.False);
            Assert.That(decision.Rejection, Is.EqualTo(Yme009TargetSoftwareVersionRejection.Conflict));
        }

        [Test]
        public void Should_Reject_When_ExistingTargetSoftwareVersionsDisagree()
        {
            var decision = Yme009TargetSoftwareVersionRule.Evaluate("SW-2", new[] { "SW-1", "SW-2" });

            Assert.That(decision.IsAllowed, Is.False);
            Assert.That(decision.EstablishedVersion, Is.EqualTo("SW-1"));
        }
    }
}
