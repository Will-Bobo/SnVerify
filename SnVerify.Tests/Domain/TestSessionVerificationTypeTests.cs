/// <author>AI Assistant</author>
/// <remarks>
/// A1 Domain 扩展：TestSession VerificationType 单元测试。
/// 先写测试，再实现（TDD）。
/// </remarks>

using System;
using NUnit.Framework;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Models;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class TestSessionVerificationTypeTests
    {
        [Test]
        public void CanCreateTestSession_WithExplicitVerificationType()
        {
            var session = new TestSession
            {
                SessionName = "Order1_20260126_143000",
                OrderId = 1,
                StartTime = DateTime.Now,
                VerificationType = VerificationType.SnMatch
            };

            Assert.That(session.VerificationType, Is.EqualTo(VerificationType.SnMatch));
        }

        [Test]
        public void TestSession_WithoutExplicitVerificationType_IsIllegal()
        {
            var session = new TestSession
            {
                SessionName = "Order1_20260126_143000",
                OrderId = 1,
                StartTime = DateTime.Now
            };

            // 未显式指定 VerificationType 时，为 default(VerificationType) = None (0)
            Assert.That((int)session.VerificationType, Is.EqualTo(0));
            Assert.That(session.VerificationType, Is.EqualTo(VerificationType.None));
        }

        [Test]
        public void SnFlow_MustExplicitlyUseVerificationType_SnMatch()
        {
            var session = new TestSession
            {
                SessionName = "Order1_20260126_143000",
                OrderId = 1,
                StartTime = DateTime.Now,
                VerificationType = VerificationType.SnMatch
            };

            Assert.That(session.VerificationType, Is.EqualTo(VerificationType.SnMatch));
        }

        [Test]
        public void CanCreateTestSession_WithVerificationType_VersionMatch()
        {
            var session = new TestSession
            {
                SessionName = "Order1_20260126_143000",
                OrderId = 1,
                StartTime = DateTime.Now,
                VerificationType = VerificationType.VersionMatch
            };

            Assert.That(session.VerificationType, Is.EqualTo(VerificationType.VersionMatch));
        }
    }
}
