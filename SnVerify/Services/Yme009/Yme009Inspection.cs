using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;

namespace SnVerify.Services.Yme009
{
    public sealed class Yme009InspectionRequest
    {
        public Yme009InspectionRequest(int sessionId, string targetSoftwareVersion)
        {
            SessionId = sessionId;
            TargetSoftwareVersion = targetSoftwareVersion ?? string.Empty;
        }

        public int SessionId { get; }

        public string TargetSoftwareVersion { get; }
    }

    public interface IYme009PropertyReader
    {
        Task<Yme009RawAttributeSet> ReadAsync(Yme009DeviceConnection connection, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 把已完成的综合判定映射成一条新的 TestRecord。不访问数据库。
    /// </summary>
    public static class Yme009TestRecordMapper
    {
        public static TestRecord Create(
            int sessionId,
            string scannedSn,
            string targetSoftwareVersion,
            Yme009InspectionDecision decision,
            DateTime verifyTime)
        {
            if (decision == null)
                throw new ArgumentNullException(nameof(decision));
            if (decision.IsSessionParameterInvalid || string.IsNullOrWhiteSpace(decision.Result))
                throw new ArgumentException("没有可保存的检测判定。", nameof(decision));

            var attributes = decision.Attributes;
            return new TestRecord
            {
                SessionId = sessionId,
                StickerSN = scannedSn,
                DeviceSN = TextValue(attributes.DeviceSn),
                ActualVersion = TextValue(attributes.SoftwareVersion),
                ExpectedVersion = targetSoftwareVersion,
                WifiMac = TextValue(attributes.WifiMac),
                Imei1 = TextValue(attributes.Imei1),
                Imei2 = TextValue(attributes.Imei2),
                CitValue = BinaryValue(attributes.ProductionTest),
                AgingRebootValue = BinaryValue(attributes.AgingReboot),
                AgingResetValue = BinaryValue(attributes.AgingReset),
                Result = decision.Result,
                FailReason = decision.FailReason,
                VerifyTime = verifyTime,
                SnReadStatus = attributes.DeviceSn.Status.ToString(),
                VersionReadStatus = attributes.SoftwareVersion.Status.ToString(),
                Imei1ReadStatus = attributes.Imei1.Status.ToString(),
                Imei2ReadStatus = attributes.Imei2.Status.ToString(),
                WifiMacReadStatus = attributes.WifiMac.Status.ToString(),
                CitReadStatus = attributes.ProductionTest.Status.ToString(),
                AgingRebootReadStatus = attributes.AgingReboot.Status.ToString(),
                AgingResetReadStatus = attributes.AgingReset.Status.ToString(),
                SnJudgment = decision.DeviceSn.ToString(),
                VersionJudgment = decision.SoftwareVersion.ToString(),
                CitJudgment = decision.ProductionTest.ToString(),
                AgingRebootJudgment = decision.AgingReboot.ToString(),
                AgingResetJudgment = decision.AgingReset.ToString()
            };
        }

        private static string TextValue(Yme009ParsedField field)
        {
            if (field.Status != Yme009ReadStatus.Ok && field.Status != Yme009ReadStatus.Missing)
                return null;
            return field.Text ?? string.Empty;
        }

        private static string BinaryValue(Yme009ParsedField field)
        {
            if (field.Status != Yme009ReadStatus.Ok
                && field.Status != Yme009ReadStatus.ProtocolInvalid
                && field.Status != Yme009ReadStatus.Missing)
                return null;
            return field.Text ?? string.Empty;
        }
    }

    public static class Yme009InspectionSummary
    {
        public static string Describe(
            string scannedSn,
            string targetSoftwareVersion,
            Yme009InspectionDecision decision,
            bool saved,
            int recordId,
            string note)
        {
            if (decision == null || decision.IsSessionParameterInvalid)
                return string.IsNullOrWhiteSpace(note) ? "检测未完成，记录未保存" : note;

            var attributes = decision.Attributes;
            var overall = decision.Result == "PASS"
                ? "PASS"
                : "FAIL：" + FailReasonTextResolver.Resolve(decision.FailReason);
            var saveText = saved && recordId > 0
                ? "记录已保存 Id=" + recordId
                : "记录未保存";
            return "扫描SN=" + (scannedSn ?? string.Empty)
                + Environment.NewLine + Item("设备SN", attributes.DeviceSn, decision.DeviceSn, decision.DeviceSnReason)
                + Environment.NewLine + Item("软件版本", attributes.SoftwareVersion, decision.SoftwareVersion, decision.SoftwareVersionReason) + "，目标=" + (targetSoftwareVersion ?? string.Empty)
                + Environment.NewLine + Field("IMEI1", attributes.Imei1)
                + Environment.NewLine + Field("IMEI2", attributes.Imei2)
                + Environment.NewLine + Field("WiFi MAC", attributes.WifiMac)
                + Environment.NewLine + Item("产测结果", attributes.ProductionTest, decision.ProductionTest, decision.ProductionTestReason)
                + Environment.NewLine + Item("重启老化", attributes.AgingReboot, decision.AgingReboot, decision.AgingRebootReason)
                + Environment.NewLine + Item("恢复出厂老化", attributes.AgingReset, decision.AgingReset, decision.AgingResetReason)
                + Environment.NewLine + "综合=" + overall
                + Environment.NewLine + saveText;
        }

        private static string Field(string name, Yme009ParsedField field)
        {
            var value = field.Status == Yme009ReadStatus.Ok || field.Status == Yme009ReadStatus.Missing || field.Status == Yme009ReadStatus.ProtocolInvalid
                ? field.Text
                : "未取得";
            return name + "=" + value + "（" + Status(field.Status) + "）";
        }

        private static string Status(Yme009ReadStatus status)
        {
            switch (status)
            {
                case Yme009ReadStatus.Ok:
                    return "正常";
                case Yme009ReadStatus.Missing:
                    return "缺失";
                case Yme009ReadStatus.ProtocolInvalid:
                    return "无法识别";
                case Yme009ReadStatus.CommandFailed:
                    return "命令失败";
                case Yme009ReadStatus.TimedOut:
                    return "超时";
                default:
                    return "断开";
            }
        }

        private static string Item(string name, Yme009ParsedField field, Yme009ItemJudgment judgment, string reason)
        {
            var text = Field(name, field) + "，判定=" + (judgment == Yme009ItemJudgment.Pass ? "PASS" : "FAIL");
            if (judgment == Yme009ItemJudgment.Fail)
                text += "，原因=" + (reason ?? string.Empty);
            return text;
        }
    }
}
