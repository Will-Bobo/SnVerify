namespace SnVerify.Domain.Product
{
    /// <summary>
    /// YME009 产品身份。只表达产品代码，不承载检测、设备或保存规则。
    /// </summary>
    public static class Yme009Product
    {
        /// <summary>
        /// 产品注册代码。
        /// </summary>
        public const string Code = "YME009";

        /// <summary>
        /// 当前 Profile 是否为已注册的 YME009 独立轮次。
        /// </summary>
        public static bool IsMatch(ProductProfile profile)
        {
            return profile != null
                && profile.Mode == VerificationMode.Independent
                && string.Equals(profile.ProductCode, Code, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
