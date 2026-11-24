using System;
using System.Collections.Generic;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009SessionStatisticsTests
    {
        [Test]
        public void Should_CountPass_When_ResultIsPassWithoutFailReason()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "PASS", null),
                Record("SN-2", "PASS", string.Empty)
            });

            Assert.That(statistics.PassCount, Is.EqualTo(2));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(0));
            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(0));
            Assert.That(statistics.PassRateText, Is.EqualTo("100.00%"));
        }

        [Test]
        public void Should_CountEachBusinessCode_When_PrimaryReasonIsBusinessFail()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "FAIL", Yme009FailReasonCodes.SnMismatch),
                Record("SN-2", "FAIL", Yme009FailReasonCodes.VersionMismatch),
                Record("SN-3", "FAIL", Yme009FailReasonCodes.CitFail),
                Record("SN-4", "FAIL", Yme009FailReasonCodes.AgingRebootFail),
                Record("SN-5", "FAIL", Yme009FailReasonCodes.AgingResetFail)
            });

            Assert.That(statistics.PassCount, Is.EqualTo(0));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(5));
            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(0));
            Assert.That(statistics.PassRateText, Is.EqualTo("0.00%"));
        }

        [Test]
        public void Should_CountEachReadExceptionCode_When_PrimaryReasonIsReadException()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "FAIL", Yme009FailReasonCodes.ReadFail),
                Record("SN-2", "FAIL", Yme009FailReasonCodes.ProtocolInvalid),
                Record("SN-3", "FAIL", Yme009FailReasonCodes.RequiredValueMissing)
            });

            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(3));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(0));
            Assert.That(statistics.PassCount, Is.EqualTo(0));
            Assert.That(statistics.PassRateText, Is.Empty);
        }

        [Test]
        public void Should_IgnoreUnregisteredRows_When_ResultAndFailReasonDoNotMatch()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "PASS", null),
                Record("SN-2", "FAIL", Yme009FailReasonCodes.CitFail),
                Record("SN-3", "FAIL", "SN_NOT_MATCH"),
                Record("SN-4", "PASS", Yme009FailReasonCodes.SnMismatch)
            });

            Assert.That(statistics.PassCount, Is.EqualTo(1));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(1));
            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(0));
            Assert.That(statistics.PassRateText, Is.EqualTo("50.00%"));
        }

        [Test]
        public void Should_CountEveryRow_When_SameSnIsSavedMoreThanOnce()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "PASS", null),
                Record("SN-1", "FAIL", Yme009FailReasonCodes.VersionMismatch),
                Record("SN-1", "FAIL", Yme009FailReasonCodes.ReadFail)
            });

            Assert.That(statistics.PassCount, Is.EqualTo(1));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(1));
            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(1));
            Assert.That(statistics.PassRateText, Is.EqualTo("50.00%"));
        }

        [Test]
        public void Should_CountReadExceptionOnce_When_ItemJudgmentIsAlsoBusinessFail()
        {
            var record = Record("SN-1", "FAIL", Yme009FailReasonCodes.RequiredValueMissing);
            record.SnJudgment = "Fail";
            record.CitJudgment = "Fail";
            record.AgingRebootJudgment = "Fail";

            var statistics = Yme009SessionStatistics.Summarize(new[] { record });

            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(1));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(0));
            Assert.That(statistics.PassRateText, Is.Empty);
        }

        [Test]
        public void Should_LeavePassRateEmpty_When_OnlyReadExceptionsExist()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "FAIL", Yme009FailReasonCodes.ReadFail),
                Record("SN-2", "FAIL", Yme009FailReasonCodes.ProtocolInvalid)
            });

            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(2));
            Assert.That(statistics.PassRateText, Is.Empty);
        }

        [Test]
        public void Should_LeavePassRateEmpty_When_RecordSetIsEmpty()
        {
            var statistics = Yme009SessionStatistics.Summarize(new List<TestRecord>());

            Assert.That(statistics.PassCount, Is.EqualTo(0));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(0));
            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(0));
            Assert.That(statistics.PassRateText, Is.Empty);
        }

        [Test]
        public void Should_FormatPassRateToTwoDecimals_When_DenominatorIsPositive()
        {
            var statistics = Yme009SessionStatistics.Summarize(new[]
            {
                Record("SN-1", "PASS", null),
                Record("SN-2", "FAIL", Yme009FailReasonCodes.SnMismatch),
                Record("SN-3", "FAIL", Yme009FailReasonCodes.CitFail),
                Record("SN-4", "FAIL", Yme009FailReasonCodes.ReadFail)
            });

            Assert.That(statistics.PassCount, Is.EqualTo(1));
            Assert.That(statistics.BusinessFailCount, Is.EqualTo(2));
            Assert.That(statistics.ReadExceptionCount, Is.EqualTo(1));
            Assert.That(statistics.PassRateText, Is.EqualTo("33.33%"));
        }

        private static TestRecord Record(string stickerSn, string result, string failReason)
        {
            return new TestRecord
            {
                Id = 1,
                StickerSN = stickerSn,
                Result = result,
                FailReason = failReason,
                VerifyTime = new DateTime(2026, 11, 24, 10, 0, 0)
            };
        }
    }
}
