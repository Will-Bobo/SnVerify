/// <author>AI Assistant</author>
/// <remarks>按产品配置的导出列集合，PASS/FAIL Sheet 复用同一 RecordColumns。</remarks>

using System.Collections.Generic;

namespace SnVerify.Domain.Export
{
    /// <summary>
    /// 产品导出配置：记录列集合与是否包含 Summary Sheet。
    /// </summary>
    public sealed class ProductExportProfile
    {
        /// <summary>产品代码（如 KM001）。</summary>
        public string ProductCode { get; }

        /// <summary>PASS/FAIL Sheet 共用的列定义顺序。</summary>
        public IReadOnlyList<ExportColumnDefinition> RecordColumns { get; }

        /// <summary>是否生成 Summary Sheet（如 KM001 为 true）。</summary>
        public bool HasSummarySheet { get; }

        /// <summary>
        /// 构造产品导出配置。
        /// </summary>
        public ProductExportProfile(string productCode, IReadOnlyList<ExportColumnDefinition> recordColumns, bool hasSummarySheet = false)
        {
            ProductCode = productCode ?? "";
            RecordColumns = recordColumns ?? new List<ExportColumnDefinition>();
            HasSummarySheet = hasSummarySheet;
        }
    }
}
