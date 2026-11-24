using System;

namespace SnVerify.Services.Yme009
{
    /// <summary>
    /// 同一轮设备接入共享的日志关联信息。不参与门禁、AT 或 Ready 判定。
    /// </summary>
    public sealed class Yme009RoundLogScope
    {
        public string RoundId { get; private set; } = "-";

        public string ScannedSn { get; private set; } = "-";

        public void Begin(string scannedSn)
        {
            RoundId = Guid.NewGuid().ToString("N").Substring(0, 8);
            ScannedSn = string.IsNullOrWhiteSpace(scannedSn) ? "-" : scannedSn.Trim();
        }
    }

    public static class Yme009AccessLog
    {
        public const int MaxDetailLength = 400;

        public static string Line(Yme009RoundLogScope scope, string message)
        {
            var roundId = scope == null || string.IsNullOrEmpty(scope.RoundId) ? "-" : scope.RoundId;
            var sn = scope == null || string.IsNullOrEmpty(scope.ScannedSn) ? "-" : scope.ScannedSn;
            return "[YME009][Round=" + roundId + "][SN=" + sn + "] " + (message ?? string.Empty);
        }

        public static string Detail(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "(empty)";

            var escaped = text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
            if (escaped.Length <= MaxDetailLength)
                return escaped;

            return escaped.Substring(0, MaxDetailLength) + "...(truncated)";
        }
    }
}
