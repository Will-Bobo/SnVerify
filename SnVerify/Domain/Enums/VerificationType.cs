/// <author>AI Assistant</author>
/// <remarks>
/// A1 Domain 扩展：版本匹配检验所需的 VerificationType 枚举。
/// </remarks>

namespace SnVerify.Domain.Enums
{
    /// <summary>
    /// 检验类型：SN 匹配 / 版本匹配
    /// </summary>
    public enum VerificationType
    {
        None = 0,
        SnMatch = 1,
        VersionMatch = 2
    }
}
