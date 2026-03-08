/// <author>AI Assistant</author>
/// <remarks>Phase3 KM001 单 Session 导出：无 Filter，Summary（含 PassRate/FailRate/ExportTime）+ PASS/FAIL 12 列。</remarks>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// KM001 单 Session 导出：全量记录，Summary Sheet + PASS/FAIL 含设备字段（WifiMac、ChipId、BoardVersion、ChargeBoardVersion）。
    /// </summary>
    public class Phase3Km001SessionExporter : ISessionExporter
    {
        private readonly IStorageService _storage;

        public Phase3Km001SessionExporter(IStorageService storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <inheritdoc />
        public async Task ExportAsync(ExportContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(context.OutputDirectory))
                throw new ArgumentException("OutputDirectory 不能为空", nameof(context));

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
                    var summarySheet = package.Workbook.Worksheets.Add("Summary");
                    WriteSummarySheet(summarySheet, context.SessionId, context.SessionName ?? context.SessionId.ToString(),
                        total, passCount, failCount, exportTime);

                    var passSheet = package.Workbook.Worksheets.Add("PASS");
                    WriteKm001SheetHeader(passSheet);
                    WriteKm001SheetData(passSheet, passRecords, startRow: 2);

                    var failSheet = package.Workbook.Worksheets.Add("FAIL");
                    WriteKm001SheetHeader(failSheet);
                    WriteKm001SheetData(failSheet, failRecordsDeduped, startRow: 2);

                    package.SaveAs(new FileInfo(xlsxPath));
                }
            }).ConfigureAwait(false);
        }

        private static void WriteSummarySheet(ExcelWorksheet sheet, int sessionId, string sessionName,
            int total, int passCount, int failCount, DateTime exportTime)
        {
            sheet.Cells[1, 1].Value = "SessionId";
            sheet.Cells[1, 2].Value = "SessionName";
            sheet.Cells[1, 3].Value = "Total";
            sheet.Cells[1, 4].Value = "Pass";
            sheet.Cells[1, 5].Value = "Fail";
            sheet.Cells[1, 6].Value = "PassRate";
            sheet.Cells[1, 7].Value = "FailRate";
            sheet.Cells[1, 8].Value = "ExportTime";
            using (var range = sheet.Cells[1, 1, 1, 8])
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

        private static void WriteKm001SheetHeader(ExcelWorksheet sheet)
        {
            var headers = new[] { "Id", "条形码SN", "设备SN", "WifiMac", "ChipId", "BoardVersion", "ChargeBoardVersion", "Result", "FailReason", "VerifyTime", "目标版本号", "设备版本号" };
            for (int c = 0; c < headers.Length; c++)
                sheet.Cells[1, c + 1].Value = headers[c];
            using (var range = sheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
        }

        private static void WriteKm001SheetData(ExcelWorksheet sheet, IList<TestRecord> records, int startRow)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var row = startRow + i;
                var r = records[i];
                sheet.Cells[row, 1].Value = r.Id;
                sheet.Cells[row, 2].Value = r.StickerSN ?? "";
                sheet.Cells[row, 3].Value = r.DeviceSN ?? "";
                sheet.Cells[row, 4].Value = r.WifiMac ?? "";
                sheet.Cells[row, 5].Value = r.ChipId ?? "";
                sheet.Cells[row, 6].Value = r.BoardVersion ?? "";
                sheet.Cells[row, 7].Value = r.ChargeBoardVersion ?? "";
                sheet.Cells[row, 8].Value = r.Result ?? "";
                sheet.Cells[row, 9].Value = r.FailReason ?? "";
                sheet.Cells[row, 10].Value = r.VerifyTime.ToString("yyyy年M月d日 HH:mm:ss");
                sheet.Cells[row, 11].Value = r.ExpectedVersion ?? "";
                sheet.Cells[row, 12].Value = r.ActualVersion ?? "";
            }
            if (records.Count > 0 && sheet.Dimension != null)
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }
    }
}
