using NUnit.Framework;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009ExpectedIdentityDecisionTests
    {
        [Test]
        public void Should_LeavePassUnchanged_When_ExpectedDataIsNotEnabled()
        {
            var decision = Yme009ExpectedIdentityDecision.Apply(Pass(), Yme009ExpectedIdentityCheck.NotEnabled());

            Assert.That(decision.Result, Is.EqualTo("PASS"));
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Pass));
            Assert.That(decision.ExpectedIdentity.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.NotEnabled));
            Assert.That(decision.ExpectedIdentity.JudgmentText, Is.EqualTo("未启用"));
            Assert.That(decision.ExpectedIdentity.Detail, Is.EqualTo(Yme009ExpectedIdentityCheck.NotEnabledDetail));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_LeaveDuplicateUnchanged_When_ExpectedDataIsNotEnabled()
        {
            var duplicate = Yme009PassHistoryDecision.Apply(Pass(), "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);

            var decision = Yme009ExpectedIdentityDecision.Apply(duplicate, Yme009ExpectedIdentityCheck.NotEnabled());

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.ExpectedIdentity.JudgmentText, Is.EqualTo("未启用"));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.BusinessFail));
        }

        [Test]
        public void Should_NotExecute_When_DeviceSnIsValidButDifferent()
        {
            var attributes = Parse(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("DEVICE"))
                .Imei1(Yme009FieldRaw.CommandFailed()));

            var check = Yme009ExpectedIdentityGate.TryResolveWithoutDeviceLookup(
                Yme009PassHistoryQueryDisposition.NotEvaluated,
                "SN1",
                attributes);

            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.NotExecuted));
            Assert.That(check.JudgmentText, Is.EqualTo("未执行"));
            Assert.That(check.ReasonText, Is.Empty);
            Assert.That(check.Detail, Is.EqualTo(Yme009ExpectedIdentityCheck.NotExecutedDetail));
        }

        [Test]
        public void Should_LeavePassUnchanged_When_IdentityWasNotExecuted()
        {
            var decision = Yme009ExpectedIdentityDecision.Apply(Pass(), Yme009ExpectedIdentityCheck.NotExecuted());

            Assert.That(decision.Result, Is.EqualTo("PASS"));
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Pass));
        }

        [Test]
        public void Should_FailIdentity_When_DeviceSnCannotBeRead()
        {
            var attributes = Parse(new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.TimedOut()));

            var check = Yme009ExpectedIdentityGate.TryResolveWithoutDeviceLookup(
                Yme009PassHistoryQueryDisposition.NotEvaluated,
                "SN1",
                attributes);

            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.Failed));
            Assert.That(check.JudgmentText, Is.EqualTo("FAIL"));
            Assert.That(check.Detail, Is.EqualTo("无法取得有效值：SN"));
            Assert.That(check.JudgmentText, Is.Not.EqualTo("未执行"));
        }

        [Test]
        public void Should_FailIdentity_When_Imei1IsMissing()
        {
            var check = ResolveUnavailable(new Yme009RawBuilder().Imei1(Yme009FieldRaw.Succeeded(string.Empty)));

            Assert.That(check.Detail, Is.EqualTo("无法取得有效值：IMEI1"));
        }

        [Test]
        public void Should_FailIdentity_When_Imei2ReadFails()
        {
            var check = ResolveUnavailable(new Yme009RawBuilder().Imei2(Yme009FieldRaw.CommandFailed()));

            Assert.That(check.Detail, Is.EqualTo("无法取得有效值：IMEI2"));
        }

        [Test]
        public void Should_FailIdentity_When_WifiMacReadFails()
        {
            var check = ResolveUnavailable(new Yme009RawBuilder().WifiMac(Yme009FieldRaw.Disconnected()));

            Assert.That(check.Detail, Is.EqualTo("无法取得有效值：WIFIMAC"));
            Assert.That(check.Detail, Does.Not.Contain("不匹配字段"));
        }

        [Test]
        public void Should_AskForLookup_When_ScannedSnMatchesAndFieldsAreUsable()
        {
            var check = Yme009ExpectedIdentityGate.TryResolveWithoutDeviceLookup(
                Yme009PassHistoryQueryDisposition.NoHistoricalPass,
                "SN1",
                Parse());

            Assert.That(check, Is.Null);
        }

        [Test]
        public void Should_AskForLookup_When_HistoryQueryFailedAndFieldsAreUsable()
        {
            var check = Yme009ExpectedIdentityGate.TryResolveWithoutDeviceLookup(
                Yme009PassHistoryQueryDisposition.QueryFailed,
                "SN1",
                Parse());

            Assert.That(check, Is.Null);
        }

        [Test]
        public void Should_Pass_When_FourFieldsMatch()
        {
            var check = Yme009ExpectedIdentityGate.EvaluateLookup(Parse(), Hit("SN1", "AA:BB", "IMEI1", "IMEI2"));

            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.Passed));
            Assert.That(check.JudgmentText, Is.EqualTo("PASS"));
            Assert.That(check.Detail, Is.EqualTo(Yme009ExpectedIdentityCheck.PassedDetail));
        }

        [Test]
        public void Should_Fail_When_ExcelDoesNotContainScannedSn()
        {
            var check = Yme009ExpectedIdentityGate.EvaluateLookup(Parse(), Yme009ExpectedDeviceLookup.NotFound);

            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.Failed));
            Assert.That(check.Detail, Is.EqualTo("Excel 中不存在该设备"));
        }

        [Test]
        public void Should_Fail_When_SnDoesNotMatch()
        {
            var check = Yme009ExpectedIdentityGate.EvaluateLookup(Parse(), Hit("sn1", "AA:BB", "IMEI1", "IMEI2"));

            Assert.That(check.Detail, Is.EqualTo("不匹配字段：SN"));
        }

        [Test]
        public void Should_Fail_When_Imei1DoesNotMatch()
        {
            var check = Yme009ExpectedIdentityGate.EvaluateLookup(Parse(), Hit("SN1", "AA:BB", "OTHER", "IMEI2"));

            Assert.That(check.Detail, Is.EqualTo("不匹配字段：IMEI1"));
        }

        [Test]
        public void Should_Fail_When_Imei2DoesNotMatch()
        {
            var check = Yme009ExpectedIdentityGate.EvaluateLookup(Parse(), Hit("SN1", "AA:BB", "IMEI1", "OTHER"));

            Assert.That(check.Detail, Is.EqualTo("不匹配字段：IMEI2"));
        }

        [Test]
        public void Should_Fail_When_WifiMacDoesNotMatch()
        {
            var check = Yme009ExpectedIdentityGate.EvaluateLookup(Parse(), Hit("SN1", "BB:CC", "IMEI1", "IMEI2"));

            Assert.That(check.Detail, Is.EqualTo("不匹配字段：WIFIMAC"));
        }

        [Test]
        public void Should_Pass_When_WifiMacDiffersOnlyByCaseOrColon()
        {
            var attributes = Parse(new Yme009RawBuilder().WifiMac(Yme009FieldRaw.Succeeded("48C86216BF1F")));

            var check = Yme009ExpectedIdentityGate.EvaluateLookup(
                attributes,
                Hit("SN1", "48:c8:62:16:bf:1f", "IMEI1", "IMEI2"));

            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.Passed));
        }

        [Test]
        public void Should_Fail_When_WifiMacUsesHyphen()
        {
            var attributes = Parse(new Yme009RawBuilder().WifiMac(Yme009FieldRaw.Succeeded("48:C8:62:16:BF:1F")));

            var check = Yme009ExpectedIdentityGate.EvaluateLookup(
                attributes,
                Hit("SN1", "48-C8-62-16-BF-1F", "IMEI1", "IMEI2"));

            Assert.That(check.Detail, Is.EqualTo("不匹配字段：WIFIMAC"));
        }

        [Test]
        public void Should_Fail_When_WifiMacUsesDot()
        {
            var attributes = Parse(new Yme009RawBuilder().WifiMac(Yme009FieldRaw.Succeeded("48:C8:62:16:BF:1F")));

            var check = Yme009ExpectedIdentityGate.EvaluateLookup(
                attributes,
                Hit("SN1", "48.C8.62.16.BF.1F", "IMEI1", "IMEI2"));

            Assert.That(check.Detail, Is.EqualTo("不匹配字段：WIFIMAC"));
        }

        [Test]
        public void Should_FailOverallWithoutRegisteredCode_When_IdentityIsTheOnlyFailure()
        {
            var check = Yme009ExpectedIdentityCheck.Failed("不匹配字段：IMEI1");

            var decision = Yme009ExpectedIdentityDecision.Apply(Pass(), check);

            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(Yme009FailReasonCodes.IsBusiness(decision.FailReason), Is.False);
            Assert.That(Yme009FailReasonCodes.IsReadException(decision.FailReason), Is.False);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Unregistered));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.ExpectedIdentity.JudgmentText, Is.EqualTo("FAIL"));
        }

        [Test]
        public void Should_KeepSnMismatch_When_IdentityAlsoFails()
        {
            var judged = Yme009AttributeSamples.Decide(
                new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded("DEVICE")).Build(),
                "SN1");

            var decision = Yme009ExpectedIdentityDecision.Apply(
                judged,
                Yme009ExpectedIdentityCheck.Failed("不匹配字段：IMEI1"));

            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ExpectedIdentity.JudgmentText, Is.EqualTo("FAIL"));
        }

        [Test]
        public void Should_KeepDuplicate_When_IdentityAlsoFails()
        {
            var duplicate = Yme009PassHistoryDecision.Apply(Pass(), "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);

            var decision = Yme009ExpectedIdentityDecision.Apply(
                duplicate,
                Yme009ExpectedIdentityCheck.Failed("Excel 中不存在该设备"));

            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.BusinessFail));
        }

        [Test]
        public void Should_KeepHistoryQueryFailure_When_IdentityAlsoFails()
        {
            var queryFailed = Yme009PassHistoryDecision.Apply(Pass(), "SN1", Yme009PassHistoryQueryDisposition.QueryFailed);

            var decision = Yme009ExpectedIdentityDecision.Apply(
                queryFailed,
                Yme009ExpectedIdentityCheck.Failed("不匹配字段：SN"));

            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFail));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.ReadException));
        }

        [Test]
        public void Should_KeepFiveItemPriority_When_IdentityAlsoFails()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .SoftwareVersion(Yme009FieldRaw.Succeeded("OTHER"))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            var decision = Yme009ExpectedIdentityDecision.Apply(
                judged,
                Yme009ExpectedIdentityCheck.Failed("不匹配字段：IMEI2"));

            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.VersionMismatch));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ExpectedIdentity.Detail, Is.EqualTo("不匹配字段：IMEI2"));
        }

        [Test]
        public void Should_StayUnresolved_When_HistoricalPassAndExcelIsEnabled()
        {
            var attributes = Parse(new Yme009RawBuilder().Imei1(Yme009FieldRaw.CommandFailed()));

            var check = Yme009ExpectedIdentityGate.TryResolveWithoutDeviceLookup(
                Yme009PassHistoryQueryDisposition.HistoricalPass,
                "SN1",
                attributes);

            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.HistoricalPassUnresolved));
            Assert.That(check.JudgmentText, Is.Empty);
            Assert.That(check.JudgmentText, Is.Not.EqualTo("未启用").And.Not.EqualTo("未执行").And.Not.EqualTo("PASS").And.Not.EqualTo("FAIL"));
            var decision = Yme009ExpectedIdentityDecision.Apply(
                Yme009PassHistoryDecision.Apply(Pass(), "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass),
                check);
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.ExpectedIdentity.Detail, Is.EqualTo(Yme009ExpectedIdentityCheck.HistoricalPassUnresolvedDetail));
            Assert.That(decision.ExpectedIdentity.JudgmentText, Is.Empty);
        }

        [Test]
        public void Should_SetUnresolved_When_CompletedPassHasQueryUnresolved()
        {
            var decision = Yme009ExpectedIdentityDecision.Apply(Pass(), Yme009ExpectedIdentityCheck.UnresolvedQuery("InvalidOperationException：expected"));

            Assert.That(decision.Result, Is.EqualTo(Yme009ResultClassifier.UnresolvedResult));
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Unresolved));
            Assert.That(decision.ExpectedIdentity.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.QueryUnresolved));
            Assert.That(decision.ExpectedIdentity.JudgmentText, Is.Empty);
            Assert.That(decision.ExpectedIdentity.Detail, Is.EqualTo("InvalidOperationException：expected"));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_KeepFiveItemFailure_When_QueryIsUnresolved()
        {
            var judged = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            var decision = Yme009ExpectedIdentityDecision.Apply(judged, Yme009ExpectedIdentityCheck.UnresolvedQuery());

            Assert.That(decision.Result, Is.EqualTo(Yme009ResultClassifier.FailResult));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.CitFail));
            Assert.That(decision.ExpectedIdentity.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.QueryUnresolved));
            Assert.That(decision.ExpectedIdentity.Detail, Is.EqualTo(Yme009ExpectedIdentityCheck.QueryUnresolvedFallbackDetail));
        }

        [Test]
        public void Should_KeepHistoricalPassFailure_When_QueryIsUnresolved()
        {
            var duplicate = Yme009PassHistoryDecision.Apply(Pass(), "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);

            var decision = Yme009ExpectedIdentityDecision.Apply(duplicate, Yme009ExpectedIdentityCheck.UnresolvedQuery());

            Assert.That(decision.Result, Is.EqualTo(Yme009ResultClassifier.FailResult));
            Assert.That(decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.BusinessFail));
            Assert.That(decision.ExpectedIdentity.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.QueryUnresolved));
        }

        private static Yme009ExpectedIdentityCheck ResolveUnavailable(Yme009RawBuilder builder)
        {
            var check = Yme009ExpectedIdentityGate.TryResolveWithoutDeviceLookup(
                Yme009PassHistoryQueryDisposition.NoHistoricalPass,
                "SN1",
                Parse(builder));
            Assert.That(check, Is.Not.Null);
            Assert.That(check.Disposition, Is.EqualTo(Yme009ExpectedIdentityDisposition.Failed));
            return check;
        }

        private static Yme009InspectionDecision Pass()
        {
            return Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build());
        }

        private static Yme009ParsedAttributes Parse(Yme009RawBuilder builder = null)
        {
            return Yme009AttributeParser.Parse((builder ?? new Yme009RawBuilder()).Build());
        }

        private static Yme009ExpectedDeviceLookup Hit(string sn, string mac, string imei1, string imei2)
        {
            return Yme009ExpectedDeviceLookup.Hit(new Yme009ExpectedDeviceRow(sn, mac, imei1, imei2));
        }
    }
}
