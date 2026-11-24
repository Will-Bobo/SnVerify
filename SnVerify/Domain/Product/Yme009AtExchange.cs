using System;

namespace SnVerify.Domain.Product
{
    public enum Yme009AtResult
    {
        NotSent = 0,
        Succeeded = 1,
        Uncertain = 2
    }

    public static class Yme009AtCommands
    {
        public const string EnableAdb = "at^optprop setprop persist.sys.skyroam.adb.enable 1";
        public const string DisableAdb = "at^optprop setprop persist.sys.skyroam.adb.enable 0";
        public const string ResultHeader = "SIMO AT COMMAND RESULT:";
    }

    /// <summary>
    /// 成功必须先出现结果行，随后另有一行 OK。同一行里的两个词不算成功。
    /// </summary>
    public static class Yme009AtResponseClassifier
    {
        public static Yme009AtResult Classify(bool commandWritten, string response)
        {
            if (!commandWritten)
                return Yme009AtResult.NotSent;

            if (HasSplitSuccess(response))
                return Yme009AtResult.Succeeded;

            return Yme009AtResult.Uncertain;
        }

        public static bool HasSplitSuccess(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            var seenHeader = false;
            var lines = response.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!seenHeader)
                {
                    if (string.Equals(line, Yme009AtCommands.ResultHeader, StringComparison.Ordinal))
                        seenHeader = true;
                    continue;
                }

                if (string.Equals(line, "OK", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
