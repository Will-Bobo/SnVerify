using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;
using SnVerify.Services.Yme009;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009InspectionJudgeTests
    {
        [Test]
        public void Should_Pass_When_FiveConditionsAreSatisfied()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build());

            Assert.That(decision.IsSessionParameterInvalid, Is.False);
            Assert.That(decision.Result, Is.EqualTo("PASS"));
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Pass));
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_Pass_When_ComparedTextsDifferOnlyByOuterWhitespace()
        {
            var raw = new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded(" SN1\r\n"))
                .SoftwareVersion(Yme009FieldRaw.Succeeded("SW-1\n"))
                .Build();

            var decision = Yme009AttributeSamples.Decide(raw, "SN1", " SW-1 ");

            Assert.That(decision.Result, Is.EqualTo("PASS"));
            Assert.That(decision.FailReason, Is.Null);
        }

        [Test]
        public void Should_FailSn_When_DeviceSnDiffersByCaseOrInternalSpace()
        {
            var byCase = Yme009AttributeSamples.Decide(
                new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded("sn1")).Build());
            var bySpace = Yme009AttributeSamples.Decide(
                new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded("SN 1")).Build());

            AssertBusiness(byCase, Yme009FailReasonCodes.SnMismatch);
            Assert.That(byCase.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(byCase.DeviceSnReason, Is.EqualTo(Yme009FailReasonCodes.ItemSnMismatch));
            Assert.That(byCase.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(byCase.SoftwareVersionReason, Is.Null);
            AssertBusiness(bySpace, Yme009FailReasonCodes.SnMismatch);
        }

        [Test]
        public void Should_FailVersion_When_ActualVersionDiffersByCaseOrInternalSpace()
        {
            var byCase = Yme009AttributeSamples.Decide(
                new Yme009RawBuilder().SoftwareVersion(Yme009FieldRaw.Succeeded("sw-1")).Build());
            var bySpace = Yme009AttributeSamples.Decide(
                new Yme009RawBuilder().SoftwareVersion(Yme009FieldRaw.Succeeded("SW 1")).Build());

            AssertBusiness(byCase, Yme009FailReasonCodes.VersionMismatch);
            Assert.That(byCase.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(byCase.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(byCase.SoftwareVersionReason, Is.EqualTo(Yme009FailReasonCodes.ItemVersionMismatch));
            AssertBusiness(bySpace, Yme009FailReasonCodes.VersionMismatch);
        }

        [Test]
        public void Should_FailProductionTest_When_ValueIsZero()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded("0\r\n"))
                .Build());

            AssertBusiness(decision, Yme009FailReasonCodes.CitFail);
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_FailAgingReboot_When_ValueIsZeroAndOtherRequiredFieldsAreReadable()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReboot(Yme009FieldRaw.Succeeded("0"))
                .Build());

            AssertBusiness(decision, Yme009FailReasonCodes.AgingRebootFail);
            Assert.That(decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public void Should_FailAgingReset_When_ValueIsZeroAndOtherRequiredFieldsAreReadable()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReset(Yme009FieldRaw.Succeeded("0"))
                .Build());

            AssertBusiness(decision, Yme009FailReasonCodes.AgingResetFail);
        }

        [Test]
        public void Should_Pass_When_BothAgingResultsAreOne()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReboot(Yme009FieldRaw.Succeeded("1"))
                .AgingReset(Yme009FieldRaw.Succeeded("1"))
                .Build());

            Assert.That(decision.Result, Is.EqualTo("PASS"));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Pass));
        }

        [Test]
        public void Should_UseFirstFailedCondition_When_SeveralBusinessConditionsFail()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("OTHER"))
                .SoftwareVersion(Yme009FieldRaw.Succeeded("OTHER"))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .AgingReboot(Yme009FieldRaw.Succeeded("0"))
                .AgingReset(Yme009FieldRaw.Succeeded("0"))
                .Build());

            AssertBusiness(decision, Yme009FailReasonCodes.SnMismatch);
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.SoftwareVersionReason, Is.EqualTo(Yme009FailReasonCodes.ItemVersionMismatch));
            Assert.That(decision.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.AgingResetReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
        }

        [Test]
        public void Should_UseVersionBeforeLaterFailures_When_SnAlreadyPasses()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .SoftwareVersion(Yme009FieldRaw.Succeeded("SW-2"))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            AssertBusiness(decision, Yme009FailReasonCodes.VersionMismatch);
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
        }

        [Test]
        public void Should_ReportReadFailure_When_RequiredCommandFailsTimesOutOrDisconnects()
        {
            var failed = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .SoftwareVersion(Yme009FieldRaw.CommandFailed())
                .Build());
            var timedOut = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReboot(Yme009FieldRaw.TimedOut())
                .Build());
            var disconnected = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReset(Yme009FieldRaw.Disconnected())
                .Build());

            AssertRead(failed, Yme009FailReasonCodes.ReadFail);
            Assert.That(failed.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(failed.SoftwareVersionReason, Is.EqualTo(Yme009FailReasonCodes.ItemReadFailed));
            Assert.That(failed.Result, Is.EqualTo("FAIL"));
            AssertRead(timedOut, Yme009FailReasonCodes.ReadFail);
            AssertRead(disconnected, Yme009FailReasonCodes.ReadFail);
            Assert.That(failed.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.VersionMismatch));
        }

        [Test]
        public void Should_ReportUntested_When_BinaryValueIsEmpty()
        {
            var empty = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded(string.Empty))
                .Build());

            AssertRead(empty, Yme009FailReasonCodes.RequiredValueMissing);
            Assert.That(empty.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(empty.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemUntested));
            Assert.That(empty.Attributes.ProductionTest.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(empty.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.CitFail));
            Assert.That(empty.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.ProtocolInvalid));
        }

        [Test]
        public void Should_ReportProtocolInvalid_When_BinaryValueIsIllegal()
        {
            var illegal = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .AgingReset(Yme009FieldRaw.Succeeded("2"))
                .Build());

            AssertRead(illegal, Yme009FailReasonCodes.ProtocolInvalid);
            Assert.That(illegal.AgingReset, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(illegal.AgingResetReason, Is.EqualTo(Yme009FailReasonCodes.ItemUnrecognized));
            Assert.That(illegal.AgingResetReason, Is.Not.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
        }

        [Test]
        public void Should_ReportRequiredValueMissing_When_RequiredTextIsEmpty()
        {
            var missingSn = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded(string.Empty))
                .Build());
            var missingVersion = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .SoftwareVersion(Yme009FieldRaw.Succeeded("\n"))
                .Build());

            AssertRead(missingSn, Yme009FailReasonCodes.RequiredValueMissing);
            Assert.That(missingSn.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(missingSn.DeviceSnReason, Is.EqualTo(Yme009FailReasonCodes.ItemRequiredMissing));
            Assert.That(missingSn.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnMismatch));
            AssertRead(missingVersion, Yme009FailReasonCodes.RequiredValueMissing);
            Assert.That(missingVersion.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(missingVersion.SoftwareVersionReason, Is.EqualTo(Yme009FailReasonCodes.ItemRequiredMissing));
            Assert.That(missingVersion.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.VersionMismatch));
        }

        [Test]
        public void Should_KeepIndependentItemReasons_When_MissingOutranksMismatchAndZero()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("OTHER"))
                .ProductionTest(Yme009FieldRaw.Succeeded(string.Empty))
                .AgingReboot(Yme009FieldRaw.Succeeded("0"))
                .AgingReset(Yme009FieldRaw.Succeeded("1"))
                .Build());

            AssertRead(decision, Yme009FailReasonCodes.RequiredValueMissing);
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.DeviceSnReason, Is.EqualTo(Yme009FailReasonCodes.ItemSnMismatch));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.SoftwareVersionReason, Is.Null);
            Assert.That(decision.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemUntested));
            Assert.That(decision.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.AgingResetReason, Is.Null);
        }

        [Test]
        public void Should_UseProductionTest_When_AllBinaryValuesAreZero()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .AgingReboot(Yme009FieldRaw.Succeeded("0"))
                .AgingReset(Yme009FieldRaw.Succeeded("0"))
                .Build());

            AssertBusiness(decision, Yme009FailReasonCodes.CitFail);
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(decision.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.AgingResetReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
        }

        [Test]
        public void Should_ShowPassOrFailWithItemReason_When_SummaryIsDisplayed()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("OTHER"))
                .ProductionTest(Yme009FieldRaw.Succeeded(string.Empty))
                .AgingReboot(Yme009FieldRaw.Succeeded("0"))
                .AgingReset(Yme009FieldRaw.Succeeded("1"))
                .Build());

            var text = Yme009InspectionSummary.Describe("SN1", "SW-1", decision, true, 7, null);

            Assert.That(text, Does.Contain("设备SN=OTHER（正常），判定=FAIL，原因=SN 不匹配"));
            Assert.That(text, Does.Contain("软件版本=SW-1（正常），判定=PASS"));
            Assert.That(text, Does.Contain("产测结果=（缺失），判定=FAIL，原因=未测试"));
            Assert.That(text, Does.Contain("重启老化=0（正常），判定=FAIL，原因=测试失败"));
            Assert.That(text, Does.Contain("恢复出厂老化=1（正常），判定=PASS"));
            Assert.That(text, Does.Contain("综合=FAIL：必要判定数据缺失"));
            Assert.That(text, Does.Not.Contain("未判定"));
        }

        [Test]
        public void Should_PreferReadFailOverProtocolAndMissing_When_SeveralRequiredReadsFail()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded(string.Empty))
                .ProductionTest(Yme009FieldRaw.Succeeded("2"))
                .AgingReboot(Yme009FieldRaw.Disconnected())
                .Build());

            AssertRead(decision, Yme009FailReasonCodes.ReadFail);
            Assert.That(decision.DeviceSnReason, Is.EqualTo(Yme009FailReasonCodes.ItemRequiredMissing));
            Assert.That(decision.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemUnrecognized));
            Assert.That(decision.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemReadFailed));
        }

        [Test]
        public void Should_PreferProtocolInvalidOverMissing_When_NoTransportFailure()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .SoftwareVersion(Yme009FieldRaw.Succeeded(string.Empty))
                .AgingReset(Yme009FieldRaw.Succeeded("x"))
                .Build());

            AssertRead(decision, Yme009FailReasonCodes.ProtocolInvalid);
            Assert.That(decision.SoftwareVersionReason, Is.EqualTo(Yme009FailReasonCodes.ItemRequiredMissing));
            Assert.That(decision.AgingResetReason, Is.EqualTo(Yme009FailReasonCodes.ItemUnrecognized));
        }

        [Test]
        public void Should_KeepReadExceptionPrimary_When_AnotherItemIsAlreadyBusinessFail()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.CommandFailed())
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            AssertRead(decision, Yme009FailReasonCodes.ReadFail);
            Assert.That(decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.DeviceSnReason, Is.EqualTo(Yme009FailReasonCodes.ItemReadFailed));
            Assert.That(decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(decision.ProductionTestReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(decision.Bucket, Is.Not.EqualTo(Yme009StatisticBucket.BusinessFail));
        }

        [Test]
        public void Should_Pass_When_InformationFieldsFailOrDiffer()
        {
            var failed = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .Imei1(Yme009FieldRaw.CommandFailed())
                .Imei2(Yme009FieldRaw.TimedOut())
                .WifiMac(Yme009FieldRaw.Disconnected())
                .Build());
            var empty = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .Imei1(Yme009FieldRaw.Succeeded(string.Empty))
                .Imei2(Yme009FieldRaw.Succeeded("other"))
                .WifiMac(Yme009FieldRaw.Succeeded("00:11"))
                .Build());

            Assert.That(failed.Result, Is.EqualTo("PASS"));
            Assert.That(failed.FailReason, Is.Null);
            Assert.That(failed.Attributes.Imei1.Status, Is.EqualTo(Yme009ReadStatus.CommandFailed));
            Assert.That(failed.Attributes.Imei2.Status, Is.EqualTo(Yme009ReadStatus.TimedOut));
            Assert.That(failed.Attributes.WifiMac.Status, Is.EqualTo(Yme009ReadStatus.Disconnected));
            Assert.That(empty.Result, Is.EqualTo("PASS"));
            Assert.That(empty.Attributes.Imei1.Status, Is.EqualTo(Yme009ReadStatus.Missing));
            Assert.That(empty.Bucket, Is.EqualTo(Yme009StatisticBucket.Pass));
        }

        [Test]
        public void Should_ReturnSessionParameterInvalid_When_TargetSoftwareVersionIsEmpty()
        {
            var decision = Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build(), "SN1", "  ");

            Assert.That(decision.IsSessionParameterInvalid, Is.True);
            Assert.That(decision.Result, Is.Null);
            Assert.That(decision.FailReason, Is.Null);
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.Unregistered));
            Assert.That(decision.Result, Is.Null);
        }

        [Test]
        public void Should_JudgeEachCallIndependently_When_SameSnIsInspectedAgain()
        {
            var first = Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build());
            var second = Yme009AttributeSamples.Decide(new Yme009RawBuilder()
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build());

            Assert.That(first.Result, Is.EqualTo("PASS"));
            AssertBusiness(second, Yme009FailReasonCodes.CitFail);
        }

        [Test]
        public void Should_ClassifyOnlyThePrimaryReason_When_ResultAndFailReasonAreGiven()
        {
            Assert.That(
                Yme009ResultClassifier.Classify("PASS", null),
                Is.EqualTo(Yme009StatisticBucket.Pass));
            Assert.That(
                Yme009ResultClassifier.Classify("FAIL", Yme009FailReasonCodes.CitFail),
                Is.EqualTo(Yme009StatisticBucket.BusinessFail));
            Assert.That(
                Yme009ResultClassifier.Classify("FAIL", Yme009FailReasonCodes.ReadFail),
                Is.EqualTo(Yme009StatisticBucket.ReadException));
            Assert.That(
                Yme009ResultClassifier.Classify("FAIL", RuleFailReasonCodes.SnNotMatch),
                Is.EqualTo(Yme009StatisticBucket.Unregistered));
            Assert.That(
                Yme009ResultClassifier.Classify("PASS", Yme009FailReasonCodes.SnMismatch),
                Is.EqualTo(Yme009StatisticBucket.Unregistered));
        }

        [Test]
        public void Should_ResolveYme009TextsWithoutChangingExistingCodes_When_FailReasonIsDisplayed()
        {
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.SnMismatch),
                Is.EqualTo("扫码 SN 与设备 SN 不一致"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.VersionMismatch),
                Is.EqualTo("软件版本与目标版本不一致"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.CitFail),
                Is.EqualTo("产测结果未通过"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.AgingRebootFail),
                Is.EqualTo("重启老化结果未通过"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.AgingResetFail),
                Is.EqualTo("恢复出厂老化结果未通过"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.ReadFail),
                Is.EqualTo("设备读取失败"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.ProtocolInvalid),
                Is.EqualTo("产测或老化数据无法识别"));
            Assert.That(
                FailReasonTextResolver.Resolve(Yme009FailReasonCodes.RequiredValueMissing),
                Is.EqualTo("必要判定数据缺失"));
            Assert.That(
                FailReasonTextResolver.Resolve(RuleFailReasonCodes.SnNotMatch),
                Is.EqualTo("设备SN与扫码SN不匹配"));
            Assert.That(
                FailReasonTextResolver.Resolve(RuleFailReasonCodes.AndroidVersionMismatch),
                Is.EqualTo("设备Android版本号与目标值不匹配"));
        }

        private static void AssertBusiness(Yme009InspectionDecision decision, string failReason)
        {
            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(failReason));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.BusinessFail));
            Assert.That(Yme009FailReasonCodes.IsBusiness(decision.FailReason), Is.True);
            Assert.That(Yme009FailReasonCodes.IsReadException(decision.FailReason), Is.False);
        }

        private static void AssertRead(Yme009InspectionDecision decision, string failReason)
        {
            Assert.That(decision.Result, Is.EqualTo("FAIL"));
            Assert.That(decision.FailReason, Is.EqualTo(failReason));
            Assert.That(decision.Bucket, Is.EqualTo(Yme009StatisticBucket.ReadException));
            Assert.That(Yme009FailReasonCodes.IsReadException(decision.FailReason), Is.True);
            Assert.That(Yme009FailReasonCodes.IsBusiness(decision.FailReason), Is.False);
        }
    }
}
