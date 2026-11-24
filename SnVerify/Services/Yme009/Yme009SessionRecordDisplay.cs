using System;
using System.Collections.Generic;
using System.Linq;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;

namespace SnVerify.Services.Yme009
{
    /// <summary>
    /// 把已保存的 TestRecord 投影成当前会话列表的一行。不访问数据库，不改变判定。
    /// </summary>
    public static class Yme009SessionRecordDisplay
    {
        public static IReadOnlyList<Yme009SessionRecordRow> Project(IEnumerable<TestRecord> records)
        {
            var rows = new List<Yme009SessionRecordRow>();
            if (records == null)
                return rows;

            var ordered = records
                .Where(record => record != null)
                .OrderBy(record => record.VerifyTime)
                .ThenBy(record => record.Id)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
                rows.Add(Create(ordered[i]));
            return rows;
        }

        public static Yme009SessionRecordRow Create(TestRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            return new Yme009SessionRecordRow(
                record.Id,
                record.VerifyTime.ToString("yyyy-MM-dd HH:mm:ss"),
                record.StickerSN ?? string.Empty,
                FormatRead(record.DeviceSN, record.SnReadStatus),
                FormatRead(record.ActualVersion, record.VersionReadStatus),
                record.ExpectedVersion ?? string.Empty,
                FormatRead(record.Imei1, record.Imei1ReadStatus),
                FormatRead(record.Imei2, record.Imei2ReadStatus),
                FormatRead(record.WifiMac, record.WifiMacReadStatus),
                FormatRead(record.CitValue, record.CitReadStatus),
                FormatRead(record.AgingRebootValue, record.AgingRebootReadStatus),
                FormatRead(record.AgingResetValue, record.AgingResetReadStatus),
                FormatJudgment(record.SnJudgment),
                TextReason(record.SnJudgment, record.SnReadStatus, Yme009FailReasonCodes.ItemSnMismatch),
                FormatJudgment(record.VersionJudgment),
                TextReason(record.VersionJudgment, record.VersionReadStatus, Yme009FailReasonCodes.ItemVersionMismatch),
                FormatJudgment(record.CitJudgment),
                BinaryReason(record.CitJudgment, record.CitReadStatus, record.CitValue),
                FormatJudgment(record.AgingRebootJudgment),
                BinaryReason(record.AgingRebootJudgment, record.AgingRebootReadStatus, record.AgingRebootValue),
                FormatJudgment(record.AgingResetJudgment),
                BinaryReason(record.AgingResetJudgment, record.AgingResetReadStatus, record.AgingResetValue),
                record.Result ?? string.Empty,
                FailReasonTextResolver.Resolve(record.FailReason));
        }

        public static string FormatRead(string value, string status)
        {
            var statusText = string.IsNullOrEmpty(status) ? "无状态" : status;
            if (value == null)
                return "未取得（" + statusText + "）";
            if (value.Length == 0)
                return "（空）（" + statusText + "）";
            return value + "（" + statusText + "）";
        }

        public static string FormatJudgment(string judgment)
        {
            if (string.Equals(judgment, "Pass", StringComparison.OrdinalIgnoreCase))
                return "PASS";
            if (string.Equals(judgment, "Fail", StringComparison.OrdinalIgnoreCase))
                return "FAIL";
            return judgment ?? string.Empty;
        }

        private static string TextReason(string judgment, string status, string mismatchReason)
        {
            if (!IsFail(judgment))
                return string.Empty;
            if (status == Yme009ReadStatus.Missing.ToString())
                return Yme009FailReasonCodes.ItemRequiredMissing;
            if (IsTransport(status))
                return Yme009FailReasonCodes.ItemReadFailed;
            if (status == Yme009ReadStatus.Ok.ToString())
                return mismatchReason;
            return string.Empty;
        }

        private static string BinaryReason(string judgment, string status, string raw)
        {
            if (!IsFail(judgment))
                return string.Empty;
            if (status == Yme009ReadStatus.Missing.ToString())
                return Yme009FailReasonCodes.ItemUntested;
            if (status == Yme009ReadStatus.ProtocolInvalid.ToString())
                return Yme009FailReasonCodes.ItemUnrecognized;
            if (IsTransport(status))
                return Yme009FailReasonCodes.ItemReadFailed;
            if (status == Yme009ReadStatus.Ok.ToString() && raw == "0")
                return Yme009FailReasonCodes.ItemTestFailed;
            return string.Empty;
        }

        private static bool IsFail(string judgment)
        {
            return string.Equals(judgment, "Fail", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTransport(string status)
        {
            return status == Yme009ReadStatus.CommandFailed.ToString()
                || status == Yme009ReadStatus.TimedOut.ToString()
                || status == Yme009ReadStatus.Disconnected.ToString();
        }
    }

    public sealed class Yme009SessionRecordRow
    {
        public Yme009SessionRecordRow(
            int id,
            string verifyTime,
            string stickerSn,
            string deviceSn,
            string actualVersion,
            string expectedVersion,
            string imei1,
            string imei2,
            string wifiMac,
            string citValue,
            string agingRebootValue,
            string agingResetValue,
            string snJudgment,
            string snReason,
            string versionJudgment,
            string versionReason,
            string citJudgment,
            string citReason,
            string agingRebootJudgment,
            string agingRebootReason,
            string agingResetJudgment,
            string agingResetReason,
            string result,
            string failReason)
        {
            Id = id;
            VerifyTime = verifyTime ?? string.Empty;
            StickerSn = stickerSn ?? string.Empty;
            DeviceSn = deviceSn ?? string.Empty;
            ActualVersion = actualVersion ?? string.Empty;
            ExpectedVersion = expectedVersion ?? string.Empty;
            Imei1 = imei1 ?? string.Empty;
            Imei2 = imei2 ?? string.Empty;
            WifiMac = wifiMac ?? string.Empty;
            CitValue = citValue ?? string.Empty;
            AgingRebootValue = agingRebootValue ?? string.Empty;
            AgingResetValue = agingResetValue ?? string.Empty;
            SnJudgment = snJudgment ?? string.Empty;
            SnReason = snReason ?? string.Empty;
            VersionJudgment = versionJudgment ?? string.Empty;
            VersionReason = versionReason ?? string.Empty;
            CitJudgment = citJudgment ?? string.Empty;
            CitReason = citReason ?? string.Empty;
            AgingRebootJudgment = agingRebootJudgment ?? string.Empty;
            AgingRebootReason = agingRebootReason ?? string.Empty;
            AgingResetJudgment = agingResetJudgment ?? string.Empty;
            AgingResetReason = agingResetReason ?? string.Empty;
            Result = result ?? string.Empty;
            FailReason = failReason ?? string.Empty;
        }

        public int Id { get; }

        public string VerifyTime { get; }

        public string StickerSn { get; }

        public string DeviceSn { get; }

        public string ActualVersion { get; }

        public string ExpectedVersion { get; }

        public string Imei1 { get; }

        public string Imei2 { get; }

        public string WifiMac { get; }

        public string CitValue { get; }

        public string AgingRebootValue { get; }

        public string AgingResetValue { get; }

        public string SnJudgment { get; }

        public string SnReason { get; }

        public string VersionJudgment { get; }

        public string VersionReason { get; }

        public string CitJudgment { get; }

        public string CitReason { get; }

        public string AgingRebootJudgment { get; }

        public string AgingRebootReason { get; }

        public string AgingResetJudgment { get; }

        public string AgingResetReason { get; }

        public string Result { get; }

        public string FailReason { get; }
    }
}
