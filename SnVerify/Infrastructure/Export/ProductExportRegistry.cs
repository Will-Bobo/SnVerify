/// <author>AI Assistant</author>
/// <remarks>按产品代码返回导出列配置；KM001 为 14 列 + Summary，Legacy 未注册。</remarks>

using System;
using System.Collections.Generic;
using SnVerify.Domain.Export;

namespace SnVerify.Infrastructure.Export
{
    /// <summary>
    /// 产品导出配置注册表实现；KM001 使用配置化 14 列。
    /// </summary>
    public sealed class ProductExportRegistry : IProductExportRegistry
    {
        private static readonly IReadOnlyDictionary<string, ProductExportProfile> Profiles;

        static ProductExportRegistry()
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var dict = new Dictionary<string, ProductExportProfile>(comparer);

            // KM001: 14 列（RowNumber, StickerSn, DeviceSn, WifiMac, ChipId, ExpectedBoardVersion, ActualBoardVersion, ExpectedChargeBoardVersion, ActualChargeBoardVersion, Result, ErrorDetail, VerificationTime, ExpectedVersion, ActualVersion）
            var km001Columns = new List<ExportColumnDefinition>
            {
                new ExportColumnDefinition(ExportFieldId.RowNumber, "Export_Km001_RowNumber"),
                new ExportColumnDefinition(ExportFieldId.StickerSn, "Export_Km001_StickerSn"),
                new ExportColumnDefinition(ExportFieldId.DeviceSn, "Export_Km001_DeviceSn"),
                new ExportColumnDefinition(ExportFieldId.WifiMac, "Export_Km001_WifiMac"),
                new ExportColumnDefinition(ExportFieldId.ChipId, "Export_Km001_ChipId"),
                new ExportColumnDefinition(ExportFieldId.ExpectedBoardVersion, "Export_Km001_ExpectedBoardVersion"),
                new ExportColumnDefinition(ExportFieldId.ActualBoardVersion, "Export_Km001_ActualBoardVersion"),
                new ExportColumnDefinition(ExportFieldId.ExpectedChargeBoardVersion, "Export_Km001_ExpectedChargeBoardVersion"),
                new ExportColumnDefinition(ExportFieldId.ActualChargeBoardVersion, "Export_Km001_ActualChargeBoardVersion"),
                new ExportColumnDefinition(ExportFieldId.Result, "Export_Km001_Result"),
                new ExportColumnDefinition(ExportFieldId.ErrorDetail, "Export_Km001_ErrorDetail"),
                new ExportColumnDefinition(ExportFieldId.VerificationTime, "Export_Km001_VerificationTime"),
                new ExportColumnDefinition(ExportFieldId.ExpectedVersion, "Export_Km001_ExpectedVersion"),
                new ExportColumnDefinition(ExportFieldId.ActualVersion, "Export_Km001_ActualVersion")
            };
            dict["KM001"] = new ProductExportProfile("KM001", km001Columns, hasSummarySheet: false);

            Profiles = dict;
        }

        /// <inheritdoc />
        public ProductExportProfile GetProfile(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return null;
            Profiles.TryGetValue(productCode.Trim(), out var profile);
            return profile;
        }
    }
}
