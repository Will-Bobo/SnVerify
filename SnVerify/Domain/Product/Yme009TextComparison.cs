using System;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// YME009 文本比较的唯一实现。先 Trim，再做区分大小写的完全比较。
    /// 批次目标版本一致性、扫码 SN 与设备 SN、实际软件版本与目标软件版本都调用这里。
    /// </summary>
    public static class Yme009TextComparison
    {
        public static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        public static bool Equals(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
        }
    }
}
