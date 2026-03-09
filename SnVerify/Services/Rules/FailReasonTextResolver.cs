using SnVerify.Properties;

namespace SnVerify.Services.Rules
{
    /// <summary>
    /// 将规则失败码转换为 UI 展示文案。未知 Code 回退显示原始值。
    /// </summary>
    public static class FailReasonTextResolver
    {
        public static string Resolve(string failReasonCode)
        {
            if (string.IsNullOrWhiteSpace(failReasonCode))
                return string.Empty;

            switch (failReasonCode)
            {
                case RuleFailReasonCodes.AdbCommandEmpty:
                    return GetResourceOrFallback("Err_AdbCommandEmpty", "ADB命令为空");
                case RuleFailReasonCodes.AdbProtocolInvalid:
                    return GetResourceOrFallback("Err_AdbProtocolInvalid", "ADB读取内容解析无效");
                case RuleFailReasonCodes.AdbReadFail:
                    return GetResourceOrFallback("Err_AdbReadFail", "ADB读取数据错误或者为空");
                case RuleFailReasonCodes.SnNotMatch:
                    return GetResourceOrFallback("Err_SnNotMatch", "设备SN与扫码SN不匹配");
                case RuleFailReasonCodes.SnDuplicate:
                    return GetResourceOrFallback("Err_SnDuplicate", "设备SN已经检验");
                case RuleFailReasonCodes.ChipIdInvalid:
                    return GetResourceOrFallback("Err_ChipIdInvalid", "芯片ID为空或者不是F50开头的ID");
                case RuleFailReasonCodes.ChipIdDuplicate:
                    return GetResourceOrFallback("Err_ChipIdDuplicate", "芯片ID已经储存");
                case RuleFailReasonCodes.ParameterNotConfigured:
                    return GetResourceOrFallback("Err_ParameterNotConfigured", "目标参数值未设定");
                case RuleFailReasonCodes.AndroidVersionMismatch:
                    return GetResourceOrFallback("Err_AndroidVersionMismatch", "设备Android版本号与目标值不匹配");
                case RuleFailReasonCodes.BoardVersionMismatch:
                    return GetResourceOrFallback("Err_BoardVersionMismatch", "芯片版本号与目标值不匹配");
                case RuleFailReasonCodes.ChargeBoardVersionMismatch:
                    return GetResourceOrFallback("Err_ChargeBoardVersionMismatch", "充电板版本号与目标值不匹配");
                default:
                    return failReasonCode;
            }
        }

        private static string GetResourceOrFallback(string key, string fallback)
        {
            var text = Resources.ResourceManager.GetString(key, Resources.Culture);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
    }
}
