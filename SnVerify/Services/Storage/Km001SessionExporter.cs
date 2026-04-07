/// <author>AI Assistant</author>
/// <remarks>KM001 单 Session 导出：配置化列（ProductExportRegistry + IExportValueResolver），Summary 列头资源化。</remarks>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Infrastructure.Export;
using SnVerify.Infrastructure.Product;
using SnVerify.Properties;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// KM001 单 Session 导出：通过 ProductExportProfile 与 IExportValueResolver 生成 PASS/FAIL 表头与数据，Summary 仅列头资源化。
    /// </summary>
    public sealed class Km001SessionExporter : ISessionExporter
    {
        private readonly IStorageService _storage;
        private readonly IProductExportRegistry _exportRegistry;
        private readonly IExportValueResolver _valueResolver;
        private readonly IProductRegistry _productRegistry;

        public Km001SessionExporter(IStorageService storage, IProductExportRegistry exportRegistry, IExportValueResolver valueResolver, IProductRegistry productRegistry)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _exportRegistry = exportRegistry ?? throw new ArgumentNullException(nameof(exportRegistry));
            _valueResolver = valueResolver ?? throw new ArgumentNullException(nameof(valueResolver));
            _productRegistry = productRegistry ?? throw new ArgumentNullException(nameof(productRegistry));
        }

        /// <inheritdoc />
        public async Task ExportAsync(ExportContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(context.OutputDirectory))
                throw new ArgumentException("OutputDirectory 不能为空", nameof(context));
            if (string.IsNullOrWhiteSpace(context.ProductCode))
                throw new InvalidOperationException("ProductCode is null or empty in export context");

            var productCode = context.ProductCode.Trim();
            var product = _productRegistry.Get(productCode);
            if (product == null)
                throw new InvalidOperationException($"Unknown productCode: {context.ProductCode}");
            if (product.Mode != VerificationMode.Phase3)
                throw new InvalidOperationException($"Phase3 export requires Phase3 product profile: {context.ProductCode}");

            var profile = _exportRegistry.GetProfile(productCode);
            if (profile == null || profile.RecordColumns == null || profile.RecordColumns.Count == 0)
                throw new InvalidOperationException($"No export profile columns for productCode: {context.ProductCode}");

            var records = await _storage.GetTestRecordsBySessionAsync(context.SessionId).ConfigureAwait(false);
            if (records == null || records.Count == 0)
                return;

            var passRecords = records.Where(r => r.Result == "PASS").ToList();
            var failRecordsRaw = records.Where(r => r.Result == "FAIL" || r.Result == "TIMEOUT").ToList();
            var seen = new HashSet<(string, string)>();
            var failRecordsDeduped = new List<TestRecord>();
            foreach (var r in failRecordsRaw)
            {
                var key = (r.StickerSN ?? "", r.DeviceSN ?? "");
                if (seen.Add(key))
                    failRecordsDeduped.Add(r);
            }

            var total = records.Count;
            var passCount = passRecords.Count;
            var failCount = failRecordsDeduped.Count;
            var exportTime = DateTime.Now;

            if (!Directory.Exists(context.OutputDirectory))
                Directory.CreateDirectory(context.OutputDirectory);

            var xlsxPath = Path.Combine(context.OutputDirectory, $"{context.SessionId}.xlsx");
            await Task.Run(() =>
            {
                using (var package = new ExcelPackage())
                {
                    if (profile.HasSummarySheet)
                    {
                        var summarySheet = package.Workbook.Worksheets.Add("Summary");
                        WriteSummarySheet(summarySheet, context.SessionId, context.SessionName ?? context.SessionId.ToString(),
                            total, passCount, failCount, exportTime);
                    }

                    var passSheet = package.Workbook.Worksheets.Add("PASS");
                    WriteRecordSheetHeader(passSheet, profile);
                    WriteRecordSheetData(passSheet, profile, passRecords, startRow: 2);

                    var failSheet = package.Workbook.Worksheets.Add("FAIL");
                    WriteRecordSheetHeader(failSheet, profile);
                    WriteRecordSheetData(failSheet, profile, failRecordsDeduped, startRow: 2);

                    package.SaveAs(new FileInfo(xlsxPath));
                }
            }).ConfigureAwait(false);
        }

        private static string GetHeaderText(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
                return "";
            try
            {
                var s = Resources.ResourceManager.GetString(resourceKey);
                return s ?? resourceKey;
            }
            catch
            {
                return resourceKey;
            }
        }

        private static void WriteSummarySheet(ExcelWorksheet sheet, int sessionId, string sessionName,
            int total, int passCount, int failCount, DateTime exportTime)
        {
            var summaryKeys = new[] { "Export_Summary_SessionId", "Export_Summary_SessionName", "Export_Summary_Total", "Export_Summary_Pass", "Export_Summary_Fail", "Export_Summary_PassRate", "Export_Summary_FailRate", "Export_Summary_ExportTime" };
            for (int c = 0; c < summaryKeys.Length; c++)
                sheet.Cells[1, c + 1].Value = GetHeaderText(summaryKeys[c]);
            using (var range = sheet.Cells[1, 1, 1, summaryKeys.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }

            var passRate = total > 0 ? $"{passCount * 100.0 / total:F0}%" : "0%";
            var failRate = total > 0 ? $"{failCount * 100.0 / total:F0}%" : "0%";
            sheet.Cells[2, 1].Value = sessionId;
            sheet.Cells[2, 2].Value = sessionName;
            sheet.Cells[2, 3].Value = total;
            sheet.Cells[2, 4].Value = passCount;
            sheet.Cells[2, 5].Value = failCount;
            sheet.Cells[2, 6].Value = passRate;
            sheet.Cells[2, 7].Value = failRate;
            sheet.Cells[2, 8].Value = exportTime.ToString("yyyy-MM-dd HH:mm:ss");
            if (sheet.Dimension != null)
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        private static void WriteRecordSheetHeader(ExcelWorksheet sheet, ProductExportProfile profile)
        {
            var columns = profile.RecordColumns;
            for (int c = 0; c < columns.Count; c++)
                sheet.Cells[1, c + 1].Value = GetHeaderText(columns[c].HeaderResourceKey);
            if (columns.Count > 0)
            {
                using (var range = sheet.Cells[1, 1, 1, columns.Count])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
            }
        }

        private void WriteRecordSheetData(ExcelWorksheet sheet, ProductExportProfile profile, IList<TestRecord> records, int startRow)
        {
            var columns = profile.RecordColumns;
            for (int i = 0; i < records.Count; i++)
            {
                var row = startRow + i;
                var r = records[i];
                for (int c = 0; c < columns.Count; c++)
                {
                    var column = columns[c];
                    if (column.FieldId == ExportFieldId.RowNumber)
                    {
                        // RowNumber：每个 Sheet 内部从 1 开始按行递增的序号
                        sheet.Cells[row, c + 1].Value = i + 1;
                    }
                    else
                    {
                        sheet.Cells[row, c + 1].Value = _valueResolver.Resolve(column.FieldId, r);
                    }
                }
            }
            if (records.Count > 0 && sheet.Dimension != null)
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }
    }
}
