using System;
using System.Collections.Generic;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 预期数据读取状态。Unresolved 只表示 OQ-015 仍未决，不是通过，也不是已确认的导入失败。
    /// </summary>
    public enum Yme009ExpectedReadStatus
    {
        Accepted = 0,
        Rejected = 1,
        Unresolved = 2
    }

    /// <summary>
    /// 预期数据基础能力使用的原因码。它们不写入 TestRecord，也不进入需求 A 的失败优先级。
    /// </summary>
    public static class Yme009ExpectedReadReasons
    {
        public const string FileUnreadable = "EXPECTED_FILE_UNREADABLE";
        public const string RequiredHeaderMissing = "EXPECTED_HEADER_MISSING";
        public const string RequiredHeaderDuplicate = "EXPECTED_HEADER_DUPLICATE";
        public const string RequiredValueMissing = "EXPECTED_VALUE_MISSING";
        public const string SnDuplicate = "EXPECTED_SN_DUPLICATE";
        public const string FingerprintMismatch = "EXPECTED_FINGERPRINT_MISMATCH";
        public const string WriteFailed = "EXPECTED_WRITE_FAILED";
        public const string ParseFailed = "EXPECTED_PARSE_FAILED";
        public const string UnresolvedNoDeviceRows = "EXPECTED_UNRESOLVED_NO_DEVICE_ROWS";
        public const string UnresolvedBlankRow = "EXPECTED_UNRESOLVED_BLANK_ROW";
        public const string SessionStillRunning = "EXPECTED_SESSION_STILL_RUNNING";
    }

    public static class Yme009ExpectedFields
    {
        public const string Sn = "SN";
        public const string WifiMac = "WIFIMAC";
        public const string Imei1 = "IMEI1";
        public const string Imei2 = "IMEI2";
    }

    public sealed class Yme009ExpectedDeviceRow
    {
        public Yme009ExpectedDeviceRow(string sn, string wifiMac, string imei1, string imei2)
        {
            Sn = sn ?? string.Empty;
            WifiMac = wifiMac ?? string.Empty;
            Imei1 = imei1 ?? string.Empty;
            Imei2 = imei2 ?? string.Empty;
        }

        public string Sn { get; }
        public string WifiMac { get; }
        public string Imei1 { get; }
        public string Imei2 { get; }
    }

    public sealed class Yme009ExpectedReadResult
    {
        public Yme009ExpectedReadResult(Yme009ExpectedReadStatus status, string reason, int recordCount, string sha256)
        {
            Status = status;
            Reason = reason;
            RecordCount = recordCount;
            Sha256 = sha256 ?? string.Empty;
        }

        public Yme009ExpectedReadStatus Status { get; }
        public string Reason { get; }
        public int RecordCount { get; }
        public string Sha256 { get; }
    }

    public sealed class Yme009ExpectedHeaderMap
    {
        public Yme009ExpectedHeaderMap(
            Yme009ExpectedReadStatus status,
            string reason,
            int snColumn,
            int wifiMacColumn,
            int imei1Column,
            int imei2Column)
        {
            Status = status;
            Reason = reason;
            SnColumn = snColumn;
            WifiMacColumn = wifiMacColumn;
            Imei1Column = imei1Column;
            Imei2Column = imei2Column;
        }

        public Yme009ExpectedReadStatus Status { get; }
        public string Reason { get; }
        public int SnColumn { get; }
        public int WifiMacColumn { get; }
        public int Imei1Column { get; }
        public int Imei2Column { get; }
    }

    public sealed class Yme009ExpectedRowDecision
    {
        public Yme009ExpectedRowDecision(Yme009ExpectedReadStatus status, string reason, Yme009ExpectedDeviceRow row)
        {
            Status = status;
            Reason = reason;
            Row = row;
        }

        public Yme009ExpectedReadStatus Status { get; }
        public string Reason { get; }
        public Yme009ExpectedDeviceRow Row { get; }
    }

    public sealed class Yme009ExpectedIdentity
    {
        public Yme009ExpectedIdentity(string sn, string wifiMac, string imei1, string imei2)
        {
            Sn = sn;
            WifiMac = wifiMac;
            Imei1 = imei1;
            Imei2 = imei2;
        }

        public string Sn { get; }
        public string WifiMac { get; }
        public string Imei1 { get; }
        public string Imei2 { get; }
    }

    public sealed class Yme009ExpectedFieldDifference
    {
        public Yme009ExpectedFieldDifference(string fieldName, string expected, string actual, bool actualValueMissing)
        {
            FieldName = fieldName ?? string.Empty;
            Expected = expected ?? string.Empty;
            Actual = actual ?? string.Empty;
            ActualValueMissing = actualValueMissing;
        }

        public string FieldName { get; }
        public string Expected { get; }
        public string Actual { get; }
        public bool ActualValueMissing { get; }
    }

    public sealed class Yme009ExpectedIdentityComparison
    {
        public Yme009ExpectedIdentityComparison(
            bool passed,
            bool actualValueMissing,
            IReadOnlyList<Yme009ExpectedFieldDifference> differences)
        {
            Passed = passed;
            ActualValueMissing = actualValueMissing;
            Differences = differences ?? new Yme009ExpectedFieldDifference[0];
        }

        public bool Passed { get; }
        public bool ActualValueMissing { get; }
        public IReadOnlyList<Yme009ExpectedFieldDifference> Differences { get; }

        public static Yme009ExpectedIdentityComparison Compare(
            Yme009ExpectedIdentity expected,
            Yme009ExpectedIdentity actual)
        {
            if (expected == null)
                throw new ArgumentNullException(nameof(expected));
            if (actual == null)
                throw new ArgumentNullException(nameof(actual));

            var differences = new List<Yme009ExpectedFieldDifference>(4);
            var missing = false;
            CompareText(Yme009ExpectedFields.Sn, expected.Sn, actual.Sn, differences, ref missing);
            CompareMac(expected.WifiMac, actual.WifiMac, differences, ref missing);
            CompareText(Yme009ExpectedFields.Imei1, expected.Imei1, actual.Imei1, differences, ref missing);
            CompareText(Yme009ExpectedFields.Imei2, expected.Imei2, actual.Imei2, differences, ref missing);
            return new Yme009ExpectedIdentityComparison(differences.Count == 0, missing, differences);
        }

        private static void CompareText(
            string fieldName,
            string expected,
            string actual,
            ICollection<Yme009ExpectedFieldDifference> differences,
            ref bool missing)
        {
            var actualText = Yme009TextComparison.Normalize(actual);
            var expectedText = Yme009TextComparison.Normalize(expected);
            if (actualText.Length == 0)
            {
                missing = true;
                differences.Add(new Yme009ExpectedFieldDifference(fieldName, expectedText, actualText, true));
                return;
            }

            if (!string.Equals(actualText, expectedText, StringComparison.Ordinal))
                differences.Add(new Yme009ExpectedFieldDifference(fieldName, expectedText, actualText, false));
        }

        private static void CompareMac(
            string expected,
            string actual,
            ICollection<Yme009ExpectedFieldDifference> differences,
            ref bool missing)
        {
            var actualMac = NormalizeMac(actual);
            var expectedMac = NormalizeMac(expected);
            if (actualMac.Length == 0)
            {
                missing = true;
                differences.Add(new Yme009ExpectedFieldDifference(Yme009ExpectedFields.WifiMac, expectedMac, actualMac, true));
                return;
            }

            if (!string.Equals(actualMac, expectedMac, StringComparison.OrdinalIgnoreCase))
                differences.Add(new Yme009ExpectedFieldDifference(Yme009ExpectedFields.WifiMac, expectedMac, actualMac, false));
        }

        private static string NormalizeMac(string value)
        {
            var trimmed = Yme009TextComparison.Normalize(value);
            if (trimmed.Length == 0)
                return string.Empty;

            var buffer = new char[trimmed.Length];
            var count = 0;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var character = trimmed[i];
                if (character == ':')
                    continue;
                buffer[count++] = character;
            }

            return new string(buffer, 0, count);
        }
    }

    /// <summary>
    /// 预期 Excel 的表头、空值和 SN 重复规则。不检查长度、校验位或 MAC 格式。
    /// </summary>
    public static class Yme009ExpectedDataRules
    {
        public static Yme009ExpectedHeaderMap MatchHeaders(IReadOnlyDictionary<int, string> cells)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            var sn = 0;
            var wifiMac = 0;
            var imei1 = 0;
            var imei2 = 0;
            foreach (var pair in cells)
            {
                var name = Yme009TextComparison.Normalize(pair.Value);
                if (name.Length == 0)
                    continue;

                if (Is(name, Yme009ExpectedFields.Sn))
                {
                    if (sn != 0)
                        return RejectedHeader(Yme009ExpectedReadReasons.RequiredHeaderDuplicate);
                    sn = pair.Key;
                }
                else if (Is(name, Yme009ExpectedFields.WifiMac))
                {
                    if (wifiMac != 0)
                        return RejectedHeader(Yme009ExpectedReadReasons.RequiredHeaderDuplicate);
                    wifiMac = pair.Key;
                }
                else if (Is(name, Yme009ExpectedFields.Imei1))
                {
                    if (imei1 != 0)
                        return RejectedHeader(Yme009ExpectedReadReasons.RequiredHeaderDuplicate);
                    imei1 = pair.Key;
                }
                else if (Is(name, Yme009ExpectedFields.Imei2))
                {
                    if (imei2 != 0)
                        return RejectedHeader(Yme009ExpectedReadReasons.RequiredHeaderDuplicate);
                    imei2 = pair.Key;
                }
            }

            if (sn == 0 || wifiMac == 0 || imei1 == 0 || imei2 == 0)
                return RejectedHeader(Yme009ExpectedReadReasons.RequiredHeaderMissing);

            return new Yme009ExpectedHeaderMap(
                Yme009ExpectedReadStatus.Accepted,
                null,
                sn,
                wifiMac,
                imei1,
                imei2);
        }

        public static Yme009ExpectedRowDecision EvaluateRow(
            IReadOnlyDictionary<int, string> cells,
            Yme009ExpectedHeaderMap headers,
            ISet<string> seenSn)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (headers == null)
                throw new ArgumentNullException(nameof(headers));
            if (seenSn == null)
                throw new ArgumentNullException(nameof(seenSn));
            if (headers.Status != Yme009ExpectedReadStatus.Accepted)
                throw new ArgumentException("表头尚未通过识别。", nameof(headers));

            if (IsBlank(cells))
            {
                return new Yme009ExpectedRowDecision(
                    Yme009ExpectedReadStatus.Unresolved,
                    Yme009ExpectedReadReasons.UnresolvedBlankRow,
                    null);
            }

            var sn = Yme009TextComparison.Normalize(Cell(cells, headers.SnColumn));
            var wifiMac = Yme009TextComparison.Normalize(Cell(cells, headers.WifiMacColumn));
            var imei1 = Yme009TextComparison.Normalize(Cell(cells, headers.Imei1Column));
            var imei2 = Yme009TextComparison.Normalize(Cell(cells, headers.Imei2Column));
            if (sn.Length == 0 || wifiMac.Length == 0 || imei1.Length == 0 || imei2.Length == 0)
            {
                return new Yme009ExpectedRowDecision(
                    Yme009ExpectedReadStatus.Rejected,
                    Yme009ExpectedReadReasons.RequiredValueMissing,
                    null);
            }

            if (!seenSn.Add(sn))
            {
                return new Yme009ExpectedRowDecision(
                    Yme009ExpectedReadStatus.Rejected,
                    Yme009ExpectedReadReasons.SnDuplicate,
                    null);
            }

            return new Yme009ExpectedRowDecision(
                Yme009ExpectedReadStatus.Accepted,
                null,
                new Yme009ExpectedDeviceRow(sn, wifiMac, imei1, imei2));
        }

        /// <summary>
        /// 正式导入只按列取出字段。空值和 SN 重复不在这里拒绝；整行空白仍保持未决。
        /// </summary>
        public static Yme009ExpectedRowDecision ExtractRow(
            IReadOnlyDictionary<int, string> cells,
            Yme009ExpectedHeaderMap headers)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (headers == null)
                throw new ArgumentNullException(nameof(headers));
            if (headers.Status != Yme009ExpectedReadStatus.Accepted)
                throw new ArgumentException("表头尚未通过识别。", nameof(headers));

            if (IsBlank(cells))
            {
                return new Yme009ExpectedRowDecision(
                    Yme009ExpectedReadStatus.Unresolved,
                    Yme009ExpectedReadReasons.UnresolvedBlankRow,
                    null);
            }

            return new Yme009ExpectedRowDecision(
                Yme009ExpectedReadStatus.Accepted,
                null,
                new Yme009ExpectedDeviceRow(
                    Yme009TextComparison.Normalize(Cell(cells, headers.SnColumn)),
                    Yme009TextComparison.Normalize(Cell(cells, headers.WifiMacColumn)),
                    Yme009TextComparison.Normalize(Cell(cells, headers.Imei1Column)),
                    Yme009TextComparison.Normalize(Cell(cells, headers.Imei2Column))));
        }

        private static bool IsBlank(IReadOnlyDictionary<int, string> cells)
        {
            foreach (var pair in cells)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    return false;
            }

            return true;
        }

        private static string Cell(IReadOnlyDictionary<int, string> cells, int column)
        {
            string value;
            if (!cells.TryGetValue(column, out value) || value == null)
                return string.Empty;
            return value;
        }

        private static bool Is(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static Yme009ExpectedHeaderMap RejectedHeader(string reason)
        {
            return new Yme009ExpectedHeaderMap(Yme009ExpectedReadStatus.Rejected, reason, 0, 0, 0, 0);
        }
    }

    public sealed class Yme009ExpectedImportRequest
    {
        public int OrderId { get; set; }
        public string SessionName { get; set; }
        public DateTime StartTime { get; set; }
        public string FilePath { get; set; }
        public string ExpectedSha256 { get; set; }
    }

    public sealed class Yme009ExpectedImportResult
    {
        public Yme009ExpectedImportResult(
            Yme009ExpectedReadStatus status,
            string reason,
            bool committed,
            int sessionId,
            int recordCount,
            string sha256)
        {
            Status = status;
            Reason = reason;
            Committed = committed;
            SessionId = sessionId;
            RecordCount = recordCount;
            Sha256 = sha256 ?? string.Empty;
        }

        public Yme009ExpectedReadStatus Status { get; }
        public string Reason { get; }
        public bool Committed { get; }
        public int SessionId { get; }
        public int RecordCount { get; }
        public string Sha256 { get; }
    }

    public sealed class Yme009ExpectedDeviceLookup
    {
        private Yme009ExpectedDeviceLookup(bool found, Yme009ExpectedDeviceRow device)
        {
            Found = found;
            Device = device;
        }

        public bool Found { get; }
        public Yme009ExpectedDeviceRow Device { get; }

        public static Yme009ExpectedDeviceLookup NotFound { get; } = new Yme009ExpectedDeviceLookup(false, null);

        public static Yme009ExpectedDeviceLookup Hit(Yme009ExpectedDeviceRow device)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            return new Yme009ExpectedDeviceLookup(true, device);
        }
    }

    /// <summary>
    /// 选择文件后的内存暂存。只保留路径、文件名、记录数、指纹和通过状态。
    /// </summary>
    public sealed class Yme009ExpectedFileStaging
    {
        public Yme009ExpectedFileStaging(string filePath, string fileName, int recordCount, string sha256)
        {
            FilePath = filePath ?? string.Empty;
            FileName = fileName ?? string.Empty;
            RecordCount = recordCount;
            Sha256 = sha256 ?? string.Empty;
            PrecheckPassed = true;
        }

        public string FilePath { get; }
        public string FileName { get; }
        public int RecordCount { get; }
        public string Sha256 { get; }
        public bool PrecheckPassed { get; }
    }

    public sealed class Yme009ExpectedStageResult
    {
        public Yme009ExpectedStageResult(
            Yme009ExpectedReadStatus status,
            string reason,
            Yme009ExpectedFileStaging file,
            string operatorMessage)
        {
            Status = status;
            Reason = reason;
            File = file;
            OperatorMessage = operatorMessage ?? string.Empty;
        }

        public Yme009ExpectedReadStatus Status { get; }
        public string Reason { get; }
        public Yme009ExpectedFileStaging File { get; }
        public string OperatorMessage { get; }
    }

    public sealed class Yme009ExpectedStartRequest
    {
        public string ProjectId { get; set; }
        public string OrderId { get; set; }
        public string ProductCode { get; set; }
        public Yme009ExpectedFileStaging File { get; set; }
    }

    public sealed class Yme009ExpectedStartResult
    {
        public Yme009ExpectedStartResult(
            bool committed,
            string sessionName,
            int sessionId,
            int recordCount,
            string reason,
            string operatorMessage)
        {
            Committed = committed;
            SessionName = sessionName ?? string.Empty;
            SessionId = sessionId;
            RecordCount = recordCount;
            Reason = reason;
            OperatorMessage = operatorMessage ?? string.Empty;
        }

        public bool Committed { get; }
        public string SessionName { get; }
        public int SessionId { get; }
        public int RecordCount { get; }
        public string Reason { get; }
        public string OperatorMessage { get; }
    }

    public static class Yme009ExpectedOperatorText
    {
        public const string ImportFailedTitle = "导入失败";
        public const string UnresolvedTitle = "尚未确认";
        public const string StartFailedTitle = "无法开始测试";
        public const string ImportingTitle = "正在导入数据";
        public const string ImportingDetail = "正在导入 Excel 预期数据，请稍候。";
        public const string ImportDoneTitle = "导入完成";
        public const string ImportDoneDetail = "预期数据导入完成。";
        public const string ReadyTitle = "待测试";
        public const string FingerprintMismatch = "文件内容已变化，请重新导入 Excel。";
        public const string FileUnavailable = "预期数据文件不存在或无法访问。";
        public const string ParseFailed = "预期数据解析失败，测试未开始。";
        public const string WriteFailed = "预期数据写入失败，测试未开始。";
        public const string UnresolvedFile = "只有表头或整行空白时如何处理尚未确认，该文件不能成为当前待用配置。";
        public const string CommittedButStartFailed = "预期数据已经写入，但测试启动未能完成。";
        public const string ExpectedDataLocked = "当前检测任务的预期数据已锁定，不能替换。";
        public const string SessionStillRunning = "已有未结束的检测任务，不能导入新的预期数据。";
        public const string MultipleRunningSessions = "存在多个未结束的检测任务，已停止自动恢复。请先处理重复任务后再继续。";
        public const string MultipleRunningSessionsTitle = "无法恢复检测任务";

        public static string ForPrecheck(string reason)
        {
            if (reason == Yme009ExpectedReadReasons.RequiredHeaderMissing)
                return "缺少必要表头 SN、WIFIMAC、IMEI1 或 IMEI2。";
            if (reason == Yme009ExpectedReadReasons.RequiredHeaderDuplicate)
                return "必要表头重复。";
            if (reason == Yme009ExpectedReadReasons.RequiredValueMissing)
                return "存在空的必要字段。";
            if (reason == Yme009ExpectedReadReasons.SnDuplicate)
                return "SN 去掉首尾空格后重复。";
            if (reason == Yme009ExpectedReadReasons.UnresolvedNoDeviceRows
                || reason == Yme009ExpectedReadReasons.UnresolvedBlankRow)
                return UnresolvedFile;
            return "文件不存在、无法打开，或不是有效的 xlsx。";
        }

        public static string ImportDoneDetailWithCount(int recordCount)
        {
            return ImportDoneDetail + "共 " + recordCount + " 条。";
        }
    }
}
