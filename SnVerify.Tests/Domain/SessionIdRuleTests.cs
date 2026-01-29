/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 1 TDD：SessionId 规则单元测试。先写测试，再实现 SessionIdGenerator。
/// </remarks>

using System;
using NUnit.Framework;
using SnVerify.Domain.Validation;

namespace SnVerify.Tests.Domain
{
    /// <summary>
    /// SessionId 规则单元测试。契约：SessionId = OrderId + "_" + yyyyMMdd_HHmmss。
    /// </summary>
    [TestFixture]
    public class SessionIdRuleTests
    {
        [Test]
        public void Format_OrderIdAndDateTime_ReturnsOrderId_Underscore_yyyyMMdd_HHmmss()
        {
            var orderId = "ORD001";
            var dt = new DateTime(2026, 1, 26, 14, 30, 0);

            var result = SessionIdGenerator.Format(orderId, dt);

            Assert.That(result, Is.EqualTo("ORD001_20260126_143000"));
        }

        [Test]
        public void Format_SingleDigitMonthDayHourMinuteSec_PadsWithZeros()
        {
            var orderId = "X";
            var dt = new DateTime(2026, 1, 5, 9, 8, 7);

            var result = SessionIdGenerator.Format(orderId, dt);

            Assert.That(result, Is.EqualTo("X_20260105_090807"));
        }

        [Test]
        public void Format_NullOrderId_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionIdGenerator.Format(null, DateTime.Now));
        }

        [Test]
        public void Format_EmptyOrderId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                SessionIdGenerator.Format("", DateTime.Now));
        }
    }
}
