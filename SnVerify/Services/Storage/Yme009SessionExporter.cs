using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OfficeOpenXml;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// YME009 单 Session 导出。PASS 全部保留；FAIL 与 TIMEOUT 按扫码 SN 和设备 SN 保留最新一条。
    /// </summary>
    public sealed class Yme009SessionExporter : ISessionExporter
    {
        private static readonly string[] DetailHeaders =
        {
            "订单号",
            "检测会话",
            "扫码SN",
            "设备SN",
            "实际软件版本",
            "目标软件版本",
            "IMEI1",
            "IMEI2",
            "WiFi MAC",
            "产测",
            "重启老化",
            "恢复出厂老化",
            "设备SN读取状态",
            "软件版本读取状态",
            "IMEI1读取状态",
            "IMEI2读取状态",
            "WiFi MAC读取状态",
            "产测读取状态",
            "重启老化读取状态",
            "恢复出厂老化读取状态",
            "SN判定",
            "版本判定",
            "产测判定",
            "重启老化判定",
            "恢复出厂判定",
            "综合结果",
            "失败原因代码",
            "失败原因",
            "检测时间",
            "身份校验状态",
            "身份校验说明"
        };

        private readonly IStorageService _storage;

        public Yme009SessionExporter(IStorageService storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public static IReadOnlyList<TestRecord> SelectRecords(IEnumerable<TestRecord> records)
        {
            var selected = new List<TestRecord>();
            var latestFail = new Dictionary<(string, string), TestRecord>();
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record == null)
                        continue;
                    if (record.Result == Yme009ResultClassifier.PassResult)
                    {
                        selected.Add(record);
                        continue;
                    }

                    if (record.Result == Yme009ResultClassifier.FailResult || record.Result == "TIMEOUT")
                    {
                        var key = (record.StickerSN ?? string.Empty, record.DeviceSN ?? string.Empty);
                        if (!latestFail.TryGetValue(key, out var current) || IsNewer(record, current))
                            latestFail[key] = record;
                        continue;
                    }

                    selected.Add(record);
                }
            }

            foreach (var record in latestFail.Values)
                selected.Add(record);
            selected.Sort(CompareChronological);
            return selected;
        }

        public async Task ExportAsync(ExportContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(context.OutputDirectory))
                throw new ArgumentException("OutputDirectory 不能为空", nameof(context));

            var records = await _storage.GetTestRecordsBySessionAsync(context.SessionId).ConfigureAwait(false);
            var exported = SelectRecords(records);
            if (exported.Count == 0)
                return;

            var statistics = Yme009SessionStatistics.Summarize(exported);
            if (!Directory.Exists(context.OutputDirectory))
                Directory.CreateDirectory(context.OutputDirectory);

            var xlsxPath = Path.Combine(context.OutputDirectory, context.SessionId + ".xlsx");
            await Task.Run(() => WriteWorkbook(xlsxPath, context, exported, statistics)).ConfigureAwait(false);
        }

        private static void WriteWorkbook(
            string xlsxPath,
            ExportContext context,
            IReadOnlyList<TestRecord> exported,
            Yme009SessionStatistics statistics)
        {
            using (var package = new ExcelPackage())
            {
                var detail = package.Workbook.Worksheets.Add("明细");
                WriteDetail(detail, context, exported);
                var summary = package.Workbook.Worksheets.Add("汇总");
                WriteSummary(summary, statistics);
                package.SaveAs(new FileInfo(xlsxPath));
            }
        }

        private static void WriteDetail(ExcelWorksheet sheet, ExportContext context, IReadOnlyList<TestRecord> exported)
        {
            for (var column = 0; column < DetailHeaders.Length; column++)
                sheet.Cells[1, column + 1].Value = DetailHeaders[column];
            using (var header = sheet.Cells[1, 1, 1, DetailHeaders.Length])
            {
                header.Style.Font.Bold = true;
                header.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }

            for (var i = 0; i < exported.Count; i++)
            {
                var row = i + 2;
                var record = exported[i];
                sheet.Cells[row, 1].Value = context.OrderName ?? string.Empty;
                sheet.Cells[row, 2].Value = context.SessionName ?? string.Empty;
                sheet.Cells[row, 3].Value = record.StickerSN ?? string.Empty;
                WriteRaw(sheet, row, 4, record.DeviceSN);
                WriteRaw(sheet, row, 5, record.ActualVersion);
                WriteRaw(sheet, row, 6, record.ExpectedVersion);
                WriteRaw(sheet, row, 7, record.Imei1);
                WriteRaw(sheet, row, 8, record.Imei2);
                WriteRaw(sheet, row, 9, record.WifiMac);
                WriteRaw(sheet, row, 10, record.CitValue);
                WriteRaw(sheet, row, 11, record.AgingRebootValue);
                WriteRaw(sheet, row, 12, record.AgingResetValue);
                WriteRaw(sheet, row, 13, record.SnReadStatus);
                WriteRaw(sheet, row, 14, record.VersionReadStatus);
                WriteRaw(sheet, row, 15, record.Imei1ReadStatus);
                WriteRaw(sheet, row, 16, record.Imei2ReadStatus);
                WriteRaw(sheet, row, 17, record.WifiMacReadStatus);
                WriteRaw(sheet, row, 18, record.CitReadStatus);
                WriteRaw(sheet, row, 19, record.AgingRebootReadStatus);
                WriteRaw(sheet, row, 20, record.AgingResetReadStatus);
                sheet.Cells[row, 21].Value = record.SnJudgment ?? string.Empty;
                sheet.Cells[row, 22].Value = record.VersionJudgment ?? string.Empty;
                sheet.Cells[row, 23].Value = record.CitJudgment ?? string.Empty;
                sheet.Cells[row, 24].Value = record.AgingRebootJudgment ?? string.Empty;
                sheet.Cells[row, 25].Value = record.AgingResetJudgment ?? string.Empty;
                sheet.Cells[row, 26].Value = record.Result ?? string.Empty;
                WriteRaw(sheet, row, 27, record.FailReason);
                sheet.Cells[row, 28].Value = FailReasonTextResolver.Resolve(record.FailReason);
                sheet.Cells[row, 29].Value = record.VerifyTime.ToString("yyyy-MM-dd HH:mm:ss");
                sheet.Cells[row, 30].Value = record.IdentityDisposition ?? string.Empty;
                sheet.Cells[row, 31].Value = record.IdentityDetail ?? string.Empty;
            }
        }

        private static void WriteSummary(ExcelWorksheet sheet, Yme009SessionStatistics statistics)
        {
            sheet.Cells[1, 1].Value = "PASS数量";
            sheet.Cells[1, 2].Value = "业务FAIL数量";
            sheet.Cells[1, 3].Value = "读取异常数量";
            sheet.Cells[1, 4].Value = "业务通过率";
            sheet.Cells[1, 5].Value = "未决数量";
            using (var header = sheet.Cells[1, 1, 1, 5])
            {
                header.Style.Font.Bold = true;
                header.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }

            sheet.Cells[2, 1].Value = statistics.PassCount;
            sheet.Cells[2, 2].Value = statistics.BusinessFailCount;
            sheet.Cells[2, 3].Value = statistics.ReadExceptionCount;
            if (!string.IsNullOrEmpty(statistics.PassRateText))
                sheet.Cells[2, 4].Value = statistics.PassRateText;
            sheet.Cells[2, 5].Value = statistics.UnresolvedCount;
        }

        private static void WriteRaw(ExcelWorksheet sheet, int row, int column, string value)
        {
            if (value == null)
                return;
            sheet.Cells[row, column].Value = value;
        }

        private static bool IsNewer(TestRecord candidate, TestRecord current)
        {
            var timeCompare = candidate.VerifyTime.CompareTo(current.VerifyTime);
            if (timeCompare != 0)
                return timeCompare > 0;
            return candidate.Id > current.Id;
        }

        private static int CompareChronological(TestRecord left, TestRecord right)
        {
            var timeCompare = left.VerifyTime.CompareTo(right.VerifyTime);
            if (timeCompare != 0)
                return timeCompare;
            return left.Id.CompareTo(right.Id);
        }
    }
}
