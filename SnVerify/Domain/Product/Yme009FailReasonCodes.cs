namespace SnVerify.Domain.Product
{
    /// <summary>
    /// YME009 主失败码。不复用 KM / Legacy 的失败码。
    /// </summary>
    public static class Yme009FailReasonCodes
    {
        public const string SnMismatch = "YME009_SN_MISMATCH";
        public const string VersionMismatch = "YME009_VERSION_MISMATCH";
        public const string CitFail = "YME009_CIT_FAIL";
        public const string AgingRebootFail = "YME009_AGING_REBOOT_FAIL";
        public const string AgingResetFail = "YME009_AGING_RESET_FAIL";
        public const string ReadFail = "YME009_READ_FAIL";
        public const string ProtocolInvalid = "YME009_PROTOCOL_INVALID";
        public const string RequiredValueMissing = "YME009_REQUIRED_VALUE_MISSING";

        public const string SnMismatchText = "扫码 SN 与设备 SN 不一致";
        public const string VersionMismatchText = "软件版本与目标版本不一致";
        public const string CitFailText = "产测结果未通过";
        public const string AgingRebootFailText = "重启老化结果未通过";
        public const string AgingResetFailText = "恢复出厂老化结果未通过";
        public const string ReadFailText = "设备读取失败";
        public const string ProtocolInvalidText = "产测或老化数据无法识别";
        public const string RequiredValueMissingText = "必要判定数据缺失";
        public const string ItemSnMismatch = "SN 不匹配";
        public const string ItemVersionMismatch = "软件版本不匹配";
        public const string ItemRequiredMissing = "必要字段缺失";
        public const string ItemUntested = "未测试";
        public const string ItemTestFailed = "测试失败";
        public const string ItemUnrecognized = "数据异常，无法识别";
        public const string ItemReadFailed = "读取异常";

        public static bool IsBusiness(string failReason)
        {
            return failReason == SnMismatch
                || failReason == VersionMismatch
                || failReason == CitFail
                || failReason == AgingRebootFail
                || failReason == AgingResetFail;
        }

        public static bool IsReadException(string failReason)
        {
            return failReason == ReadFail
                || failReason == ProtocolInvalid
                || failReason == RequiredValueMissing;
        }
    }
}
