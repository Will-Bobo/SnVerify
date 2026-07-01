using System;

namespace SnVerify.Services.Rules
{
    /// <summary>
    /// 规则层统一失败码常量。规则执行仅返回 Code，不直接返回中文文案。
    /// </summary>
    public static class RuleFailReasonCodes
    {
        public const string AdbCommandEmpty = "ADB_COMMAND_EMPTY";
        public const string AdbProtocolInvalid = "ADB_PROTOCOL_INVALID";
        public const string AdbReadFail = "ADB_READ_FAIL";
        public const string SnNotMatch = "SN_NOT_MATCH";
        public const string SnDuplicate = "SN_DUPLICATE";
        public const string ChipIdInvalid = "CHIPID_INVALID";
        public const string ChipIdDuplicate = "CHIPID_DUPLICATE";
        public const string ParameterNotConfigured = "PARAMETER_NOT_CONFIGURED";
        public const string AndroidVersionMismatch = "ANDROID_VERSION_MISMATCH";
        public const string BoardVersionMismatch = "BOARD_VERSION_MISMATCH";
        public const string ChargeBoardVersionMismatch = "CHARGE_BOARD_VERSION_MISMATCH";
        public const string ProductProfileNotFound = "PRODUCT_PROFILE_NOT_FOUND";
    }
}
