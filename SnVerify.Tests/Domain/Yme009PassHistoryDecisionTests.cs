using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009PassHistoryDecisionTests
    {
        [Test]
        public void Should_KeepPass_When_NoHistoricalPass()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build());

            var decision = Yme009PassHistoryDecision.Apply(judged, "SN1", Yme009PassHistoryQueryDisposition.NoHistoricalPass);

            Assert.That(decision.Result, Is.EqualTo("PASS"));
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Pass));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_FailDuplicate_When_HistoricalPassAndFiveItemsPass()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build());

            var decision = Yme009PassHistoryDecision.Apply(judged, "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.FailReason, Is.EqualTo(RuleFailReasonCodes.SnDuplicate));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.BusinessFail));
            Assert.That(Yme009FailReasonCodes.IsBusiness(decision.FailReason), Is.True);
            Assert.That(Yme009FailReasonCodes.IsReadException(decision.FailReason), Is.False);
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(FailReasonTextResolver.Resolve(decision.FailReason), Is.EqualTo("设备SN已经检验"));
        }

        [Test]
        public void Should_PreferDuplicate_When_HistoricalPassAndProductionTestFails()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            var decision = Yme009PassHistoryDecision.Apply(judged, "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_KeepExistingFiveItemOrder_When_NoHistoricalPass()
        {
            var version = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .SoftwareVersion(Yme009FieldRaw.Succeeded("OTHER"))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            var decision = Yme009PassHistoryDecision.Apply(version, "SN1", Yme009PassHistoryQueryDisposition.NoHistoricalPass);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.VersionMismatch));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
        }

        [Test]
        public void Should_PreferConfirmedMismatch_When_HistoryAlsoReportsDuplicate()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("OTHER"))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            var decision = Yme009PassHistoryDecision.Apply(judged, "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(decision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(Yme009PassHistoryDecision.CanQueryHistory(decision.Attributes, "SN1"), Is.False);
        }

        [Test]
        public void Should_KeepReadFailure_When_DeviceSnIsMissing()
        {
            var timedOut = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.TimedOut())
                .Build());
            var missing = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded(string.Empty))
                .Build());

            var timedOutDecision = Yme009PassHistoryDecision.Apply(timedOut, "SN1", Yme009PassHistoryQueryDisposition.NotEvaluated);
            var missingDecision = Yme009PassHistoryDecision.Apply(missing, "SN1", Yme009PassHistoryQueryDisposition.QueryFailed);

            Assert.That(timedOutDecision.FailReason, Is.EqualTo(Yme009FailReasonCodes.ReadFail));
            Assert.That(timedOutDecision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(missingDecision.FailReason, Is.EqualTo(Yme009FailReasonCodes.RequiredValueMissing));
            Assert.That(missingDecision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(missingDecision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFail));
            Assert.That(Yme009PassHistoryDecision.CanQueryHistory(timedOut.Attributes, "SN1"), Is.False);
            Assert.That(Yme009PassHistoryDecision.CanQueryHistory(missing.Attributes, "SN1"), Is.False);
        }

        [Test]
        public void Should_FailQuery_When_HistoryQueryFailsAndSnMatches()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReset(Yme009FieldRaw.Succeeded("0"))
                .Build());

            var decision = Yme009PassHistoryDecision.Apply(judged, "SN1", Yme009PassHistoryQueryDisposition.QueryFailed);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFail));
            Assert.That(decision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.ReadException));
            Assert.That(Yme009FailReasonCodes.IsReadException(decision.FailReason), Is.True);
            Assert.That(Yme009FailReasonCodes.IsBusiness(decision.FailReason), Is.False);
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(FailReasonTextResolver.Resolve(decision.FailReason), Is.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFailText));
        }

        [Test]
        public void Should_PreferConfirmedMismatch_When_VersionReadAlsoFails()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("OTHER"))
                .SoftwareVersion(Yme009FieldRaw.TimedOut())
                .Build());

            var decision = Yme009PassHistoryDecision.Apply(judged, "SN1", Yme009PassHistoryQueryDisposition.NotEvaluated);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
        }
    }
}
