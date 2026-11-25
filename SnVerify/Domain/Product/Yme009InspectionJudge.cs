using System;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 五项必要条件的综合判定。不读取设备，不访问数据库，不生成 TestRecord。
    /// </summary>
    public static class Yme009InspectionJudge
    {
        public static Yme009InspectionDecision Judge(
            string scannedSn,
            string targetSoftwareVersion,
            Yme009ParsedAttributes attributes)
        {
            if (attributes == null)
                throw new ArgumentNullException(nameof(attributes));

            if (string.IsNullOrWhiteSpace(targetSoftwareVersion))
                return Yme009InspectionDecision.SessionParameterInvalid(attributes);

            var sn = JudgeText(attributes.DeviceSn, scannedSn, Yme009FailReasonCodes.ItemSnMismatch);
            var version = JudgeText(attributes.SoftwareVersion, targetSoftwareVersion, Yme009FailReasonCodes.ItemVersionMismatch);
            var productionTest = JudgeBinary(attributes.ProductionTest);
            var agingReboot = JudgeBinary(attributes.AgingReboot);
            var agingReset = JudgeBinary(attributes.AgingReset);
            var failReason = SelectPrimary(attributes, scannedSn, targetSoftwareVersion);
            if (failReason == null && HasItemFailure(sn, version, productionTest, agingReboot, agingReset))
                throw new InvalidOperationException("存在分项 FAIL，但没有综合主失败代码。");

            if (failReason == null)
            {
                return Yme009InspectionDecision.Judged(
                    attributes,
                    Yme009ResultClassifier.PassResult,
                    null,
                    sn.Judgment,
                    version.Judgment,
                    productionTest.Judgment,
                    agingReboot.Judgment,
                    agingReset.Judgment,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            return Yme009InspectionDecision.Judged(
                attributes,
                Yme009ResultClassifier.FailResult,
                failReason,
                sn.Judgment,
                version.Judgment,
                productionTest.Judgment,
                agingReboot.Judgment,
                agingReset.Judgment,
                sn.Reason,
                version.Reason,
                productionTest.Reason,
                agingReboot.Reason,
                agingReset.Reason);
        }

        private static Item JudgeText(Yme009ParsedField field, string expected, string mismatchReason)
        {
            if (IsTransportFailure(field.Status))
                return Item.Fail(Yme009FailReasonCodes.ItemReadFailed);
            if (field.Status == Yme009ReadStatus.Missing)
                return Item.Fail(Yme009FailReasonCodes.ItemRequiredMissing);
            if (field.Status != Yme009ReadStatus.Ok)
                throw new InvalidOperationException("必要文本字段的技术状态无法形成分项判定。");

            return Yme009TextComparison.Equals(field.Text, expected)
                ? Item.Pass()
                : Item.Fail(mismatchReason);
        }

        private static Item JudgeBinary(Yme009ParsedField field)
        {
            if (IsTransportFailure(field.Status))
                return Item.Fail(Yme009FailReasonCodes.ItemReadFailed);
            if (field.Status == Yme009ReadStatus.Missing)
                return Item.Fail(Yme009FailReasonCodes.ItemUntested);
            if (field.Status == Yme009ReadStatus.ProtocolInvalid)
                return Item.Fail(Yme009FailReasonCodes.ItemUnrecognized);
            if (field.Status == Yme009ReadStatus.Ok && field.Text == "1")
                return Item.Pass();
            if (field.Status == Yme009ReadStatus.Ok && field.Text == "0")
                return Item.Fail(Yme009FailReasonCodes.ItemTestFailed);

            throw new InvalidOperationException("二值属性的技术状态无法形成分项判定。");
        }

        private static string SelectPrimary(Yme009ParsedAttributes attributes, string scannedSn, string targetSoftwareVersion)
        {
            if (HasTransportFailure(attributes.DeviceSn)
                || HasTransportFailure(attributes.SoftwareVersion)
                || HasTransportFailure(attributes.ProductionTest)
                || HasTransportFailure(attributes.AgingReboot)
                || HasTransportFailure(attributes.AgingReset))
            {
                return Yme009FailReasonCodes.ReadFail;
            }

            if (attributes.ProductionTest.Status == Yme009ReadStatus.ProtocolInvalid
                || attributes.AgingReboot.Status == Yme009ReadStatus.ProtocolInvalid
                || attributes.AgingReset.Status == Yme009ReadStatus.ProtocolInvalid)
            {
                return Yme009FailReasonCodes.ProtocolInvalid;
            }

            if (attributes.DeviceSn.Status == Yme009ReadStatus.Missing
                || attributes.SoftwareVersion.Status == Yme009ReadStatus.Missing
                || attributes.ProductionTest.Status == Yme009ReadStatus.Missing
                || attributes.AgingReboot.Status == Yme009ReadStatus.Missing
                || attributes.AgingReset.Status == Yme009ReadStatus.Missing)
            {
                return Yme009FailReasonCodes.RequiredValueMissing;
            }

            if (!Yme009TextComparison.Equals(attributes.DeviceSn.Text, scannedSn))
                return Yme009FailReasonCodes.SnMismatch;
            if (!Yme009TextComparison.Equals(attributes.SoftwareVersion.Text, targetSoftwareVersion))
                return Yme009FailReasonCodes.VersionMismatch;
            if (attributes.ProductionTest.Text == "0")
                return Yme009FailReasonCodes.CitFail;
            if (attributes.AgingReboot.Text == "0")
                return Yme009FailReasonCodes.AgingRebootFail;
            if (attributes.AgingReset.Text == "0")
                return Yme009FailReasonCodes.AgingResetFail;

            return null;
        }

        private static bool HasItemFailure(Item sn, Item version, Item productionTest, Item agingReboot, Item agingReset)
        {
            return sn.Judgment == Yme009ItemJudgment.Fail
                || version.Judgment == Yme009ItemJudgment.Fail
                || productionTest.Judgment == Yme009ItemJudgment.Fail
                || agingReboot.Judgment == Yme009ItemJudgment.Fail
                || agingReset.Judgment == Yme009ItemJudgment.Fail;
        }

        private static bool HasTransportFailure(Yme009ParsedField field)
        {
            return IsTransportFailure(field.Status);
        }

        private static bool IsTransportFailure(Yme009ReadStatus status)
        {
            return status == Yme009ReadStatus.CommandFailed
                || status == Yme009ReadStatus.TimedOut
                || status == Yme009ReadStatus.Disconnected;
        }

        private struct Item
        {
            private Item(Yme009ItemJudgment judgment, string reason)
            {
                Judgment = judgment;
                Reason = reason;
            }

            public Yme009ItemJudgment Judgment { get; }

            public string Reason { get; }

            public static Item Pass()
            {
                return new Item(Yme009ItemJudgment.Pass, null);
            }

            public static Item Fail(string reason)
            {
                return new Item(Yme009ItemJudgment.Fail, reason);
            }
        }
    }

    public sealed class Yme009InspectionDecision
    {
        private Yme009InspectionDecision(
            bool isSessionParameterInvalid,
            Yme009ParsedAttributes attributes,
            string result,
            string failReason,
            Yme009ItemJudgment deviceSn,
            Yme009ItemJudgment softwareVersion,
            Yme009ItemJudgment productionTest,
            Yme009ItemJudgment agingReboot,
            Yme009ItemJudgment agingReset,
            string deviceSnReason,
            string softwareVersionReason,
            string productionTestReason,
            string agingRebootReason,
            string agingResetReason,
            Yme009ExpectedIdentityCheck expectedIdentity)
        {
            IsSessionParameterInvalid = isSessionParameterInvalid;
            Attributes = attributes;
            Result = result;
            FailReason = failReason;
            ExpectedIdentity = expectedIdentity;
            DeviceSn = deviceSn;
            SoftwareVersion = softwareVersion;
            ProductionTest = productionTest;
            AgingReboot = agingReboot;
            AgingReset = agingReset;
            DeviceSnReason = deviceSnReason;
            SoftwareVersionReason = softwareVersionReason;
            ProductionTestReason = productionTestReason;
            AgingRebootReason = agingRebootReason;
            AgingResetReason = agingResetReason;
            Bucket = isSessionParameterInvalid
                ? Yme009StatisticBucket.Unregistered
                : Yme009ResultClassifier.Classify(result, failReason);
        }

        public bool IsSessionParameterInvalid { get; }

        public Yme009ParsedAttributes Attributes { get; }

        public string Result { get; }

        public string FailReason { get; }

        public Yme009ItemJudgment DeviceSn { get; }

        public Yme009ItemJudgment SoftwareVersion { get; }

        public Yme009ItemJudgment ProductionTest { get; }

        public Yme009ItemJudgment AgingReboot { get; }

        public Yme009ItemJudgment AgingReset { get; }

        public string DeviceSnReason { get; }

        public string SoftwareVersionReason { get; }

        public string ProductionTestReason { get; }

        public string AgingRebootReason { get; }

        public string AgingResetReason { get; }

        public Yme009ExpectedIdentityCheck ExpectedIdentity { get; }

        public Yme009StatisticBucket Bucket { get; }

        public static Yme009InspectionDecision SessionParameterInvalid(Yme009ParsedAttributes attributes)
        {
            return new Yme009InspectionDecision(
                true,
                attributes,
                null,
                null,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Fail,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        public static Yme009InspectionDecision Judged(
            Yme009ParsedAttributes attributes,
            string result,
            string failReason,
            Yme009ItemJudgment deviceSn,
            Yme009ItemJudgment softwareVersion,
            Yme009ItemJudgment productionTest,
            Yme009ItemJudgment agingReboot,
            Yme009ItemJudgment agingReset,
            string deviceSnReason,
            string softwareVersionReason,
            string productionTestReason,
            string agingRebootReason,
            string agingResetReason,
            Yme009ExpectedIdentityCheck expectedIdentity = null)
        {
            return new Yme009InspectionDecision(
                false,
                attributes,
                result,
                failReason,
                deviceSn,
                softwareVersion,
                productionTest,
                agingReboot,
                agingReset,
                deviceSnReason,
                softwareVersionReason,
                productionTestReason,
                agingRebootReason,
                agingResetReason,
                expectedIdentity);
        }

        public Yme009InspectionDecision WithExpectedIdentity(
            string result,
            string failReason,
            Yme009ExpectedIdentityCheck expectedIdentity)
        {
            return Judged(
                Attributes,
                result,
                failReason,
                DeviceSn,
                SoftwareVersion,
                ProductionTest,
                AgingReboot,
                AgingReset,
                DeviceSnReason,
                SoftwareVersionReason,
                ProductionTestReason,
                AgingRebootReason,
                AgingResetReason,
                expectedIdentity);
        }
    }
}
