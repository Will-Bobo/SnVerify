/// <author>AI Assistant</author>
/// <remarks>
/// 导出记录过滤默认勾选逻辑：根据 VerificationType 列表计算 SN/版本 复选框默认状态。
/// 纯逻辑，无 WPF 依赖，便于单元测试。
/// </remarks>

using System.Collections.Generic;
using System.Linq;
using SnVerify.Domain.Enums;

namespace SnVerify.Domain.Export
{
    /// <summary>
    /// 导出记录过滤默认勾选逻辑。
    /// - 仅 SnMatch → 勾选 SN，不勾选版本
    /// - 仅 VersionMatch → 勾选版本，不勾选 SN
    /// - 混合或空 → 两个都勾选
    /// </summary>
    public static class ExportRecordFilterDefaults
    {
        /// <summary>
        /// 根据 VerificationType 列表计算默认勾选状态（可单独做单元测试）。
        /// </summary>
        public static (bool snChecked, bool verChecked) GetDefaultCheckState(IReadOnlyList<VerificationType> types)
        {
            if (types == null || types.Count == 0)
                return (true, true);
            var hasSn = types.Any(t => t == VerificationType.SnMatch);
            var hasVer = types.Any(t => t == VerificationType.VersionMatch);
            if (hasSn && !hasVer)
                return (true, false);
            if (hasVer && !hasSn)
                return (false, true);
            return (true, true);
        }

        /// <summary>
        /// 根据勾选状态转换为 ExportRecordFilter（可单独做单元测试）。
        /// </summary>
        public static ExportRecordFilter ToFilter(bool snChecked, bool verChecked)
        {
            if (!snChecked && !verChecked) return null;
            if (snChecked && verChecked) return ExportRecordFilter.All;
            if (snChecked) return ExportRecordFilter.SnOnly;
            return ExportRecordFilter.VersionOnly;
        }
    }
}
