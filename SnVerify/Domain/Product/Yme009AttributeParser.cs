using System;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 把八项原始读取结果分类为字段状态。不连接设备，不给出综合 PASS / FAIL。
    /// </summary>
    public static class Yme009AttributeParser
    {
        public static Yme009ParsedAttributes Parse(Yme009RawAttributeSet raw)
        {
            if (raw == null)
                throw new ArgumentNullException(nameof(raw));

            return new Yme009ParsedAttributes(
                ParseField(Yme009FieldKind.DeviceSn, raw.DeviceSn),
                ParseField(Yme009FieldKind.SoftwareVersion, raw.SoftwareVersion),
                ParseField(Yme009FieldKind.Imei1, raw.Imei1),
                ParseField(Yme009FieldKind.Imei2, raw.Imei2),
                ParseField(Yme009FieldKind.WifiMac, raw.WifiMac),
                ParseField(Yme009FieldKind.ProductionTest, raw.ProductionTest),
                ParseField(Yme009FieldKind.AgingReboot, raw.AgingReboot),
                ParseField(Yme009FieldKind.AgingReset, raw.AgingReset));
        }

        public static string StripTrailingLineEndings(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var end = value.Length;
            while (end > 0)
            {
                var ch = value[end - 1];
                if (ch != '\r' && ch != '\n')
                    break;
                end--;
            }

            return end == value.Length ? value : value.Substring(0, end);
        }

        private static Yme009ParsedField ParseField(Yme009FieldKind kind, Yme009FieldRaw raw)
        {
            if (!raw.CommandSucceeded)
                return new Yme009ParsedField(kind, raw.FailureStatus, string.Empty);

            var text = StripTrailingLineEndings(raw.Output);
            var role = Yme009AttributeCatalog.Get(kind).Role;
            return new Yme009ParsedField(kind, Classify(role, text), text);
        }

        private static Yme009ReadStatus Classify(Yme009FieldRole role, string text)
        {
            if (role == Yme009FieldRole.RequiredBinary)
            {
                if (text.Length == 0)
                    return Yme009ReadStatus.Missing;
                if (text == "0" || text == "1")
                    return Yme009ReadStatus.Ok;
                return Yme009ReadStatus.ProtocolInvalid;
            }

            if (text.Length == 0)
                return Yme009ReadStatus.Missing;

            return Yme009ReadStatus.Ok;
        }
    }
}
