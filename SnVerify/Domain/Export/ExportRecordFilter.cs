/// <author>AI Assistant</author>
/// <remarks>
/// 导出记录过滤模型：按 VerificationType 过滤 TestRecord。
/// 不可变对象。
/// </remarks>

namespace SnVerify.Domain.Export
{
    /// <summary>
    /// 导出记录过滤：控制导出 SN 检验 / 版本检验 / 全部记录。
    /// </summary>
    public class ExportRecordFilter
    {
        /// <summary>
        /// 是否包含 SnMatch 记录
        /// </summary>
        public bool IncludeSnMatch { get; }

        /// <summary>
        /// 是否包含 VersionMatch 记录
        /// </summary>
        public bool IncludeVersionMatch { get; }

        private ExportRecordFilter(bool includeSnMatch, bool includeVersionMatch)
        {
            IncludeSnMatch = includeSnMatch;
            IncludeVersionMatch = includeVersionMatch;
        }

        /// <summary>
        /// 全部：SN + Version
        /// </summary>
        public static ExportRecordFilter All { get; } = new ExportRecordFilter(true, true);

        /// <summary>
        /// 仅 SN 检验记录（VerificationType.SnMatch）
        /// </summary>
        public static ExportRecordFilter SnOnly { get; } = new ExportRecordFilter(true, false);

        /// <summary>
        /// 仅版本检验记录（VerificationType.VersionMatch）
        /// </summary>
        public static ExportRecordFilter VersionOnly { get; } = new ExportRecordFilter(false, true);
    }
}
