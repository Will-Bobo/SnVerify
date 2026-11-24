using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services.Storage
{
    [TestFixture]
    public class Yme009SessionExporterTests
    {
        private string _outputDir;

        [SetUp]
        public void SetUp()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            _outputDir = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_outputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir))
            {
                try { Directory.Delete(_outputDir, true); } catch { }
            }
        }

        [Test]
        public void Should_KeepLatestFail_When_SameKeyHasMultipleFails()
        {
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(1, "SN", "DEV", "FAIL", Yme009FailReasonCodes.CitFail, T(0)),
                Row(2, "SN", "DEV", "FAIL", Yme009FailReasonCodes.SnMismatch, T(2))
            });

            Assert.That(selected.Select(record => record.Id), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void Should_KeepLatest_When_FailAndTimeoutShareKey()
        {
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(1, "SN", "DEV", "TIMEOUT", "TIMEOUT", T(0)),
                Row(2, "SN", "DEV", "FAIL", Yme009FailReasonCodes.ReadFail, T(1))
            });

            Assert.That(selected.Select(record => record.Id), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void Should_KeepEachKey_When_FailKeysDiffer()
        {
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(1, "SN-A", "DEV-A", "FAIL", Yme009FailReasonCodes.CitFail, T(1)),
                Row(2, "SN-B", "DEV-B", "FAIL", Yme009FailReasonCodes.VersionMismatch, T(0))
            });

            Assert.That(selected.Select(record => record.Id), Is.EqualTo(new[] { 2, 1 }));
        }

        [Test]
        public void Should_KeepEveryPass_When_SameKeyIsRepeated()
        {
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(2, "SN", "DEV", "PASS", null, T(1)),
                Row(1, "SN", "DEV", "PASS", null, T(0))
            });

            Assert.That(selected.Select(record => record.Id), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Should_PreferLargerId_When_VerifyTimeIsEqual()
        {
            var time = T(0);
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(4, "SN", "DEV", "FAIL", "older-id", time),
                Row(9, "SN", "DEV", "FAIL", "newer-id", time)
            });

            Assert.That(selected.Single().Id, Is.EqualTo(9));
            Assert.That(selected.Single().FailReason, Is.EqualTo("newer-id"));
        }

        [Test]
        public void Should_MergeNullAndEmptyDeviceSn_When_BuildingDedupKey()
        {
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(1, "SN", null, "FAIL", "null-device", T(0)),
                Row(2, "SN", string.Empty, "TIMEOUT", "empty-device", T(1))
            });

            Assert.That(selected.Single().Id, Is.EqualTo(2));
        }

        [Test]
        public void Should_KeepPassAndFail_When_TheyShareTheSameKey()
        {
            var selected = Yme009SessionExporter.SelectRecords(new[]
            {
                Row(1, "SN", "DEV", "PASS", null, T(0)),
                Row(2, "SN", "DEV", "FAIL", Yme009FailReasonCodes.AgingResetFail, T(1))
            });

            Assert.That(selected.Select(record => record.Result), Is.EqualTo(new[] { "PASS", "FAIL" }));
        }

        [Test]
        public async Task Should_WriteDetailAndSummaryFromExportedRows_When_SessionHasRecords()
        {
            var olderFail = Row(3, "SN-B", "DEV-B", "FAIL", Yme009FailReasonCodes.CitFail, T(0));
            var latestFail = Row(4, "SN-B", "DEV-B", "FAIL", Yme009FailReasonCodes.ReadFail, T(2));
            latestFail.CitJudgment = "Fail";
            latestFail.CitValue = null;
            latestFail.CitReadStatus = "CommandFailed";
            latestFail.AgingRebootValue = string.Empty;
            latestFail.AgingRebootReadStatus = "Missing";
            latestFail.DeviceSN = "DEV-B";
            latestFail.SnReadStatus = "Ok";
            var pass = Row(1, "SN-A", "DEV-A", "PASS", null, T(1));
            pass.Imei1 = "111";
            pass.Imei1ReadStatus = "Ok";
            pass.ActualVersion = "V-ACT";
            pass.ExpectedVersion = "V-EXP";
            pass.WifiMac = "AA";
            var unregistered = Row(8, "SN-C", "DEV-C", "FAIL", "OTHER", T(3));
            var records = new List<TestRecord> { olderFail, pass, latestFail, unregistered };
            var exported = Yme009SessionExporter.SelectRecords(records);
            var expected = Yme009SessionStatistics.Summarize(exported);
            var beforeDedup = Yme009SessionStatistics.Summarize(records);
            Assert.That(beforeDedup.BusinessFailCount, Is.EqualTo(1));
            Assert.That(expected.BusinessFailCount, Is.EqualTo(0));

            var storage = new Mock<IStorageService>(MockBehavior.Strict);
            storage.Setup(s => s.GetTestRecordsBySessionAsync(14)).ReturnsAsync(records);
            var exporter = new Yme009SessionExporter(storage.Object);
            await exporter.ExportAsync(new ExportContext
            {
                SessionId = 14,
                SessionName = "ORD-1_20261124_100000",
                OrderName = "ORD-1",
                OutputDirectory = _outputDir,
                ProductCode = Yme009Product.Code
            });

            using (var package = new ExcelPackage(new FileInfo(Path.Combine(_outputDir, "14.xlsx"))))
            {
                var detail = package.Workbook.Worksheets["明细"];
                var summary = package.Workbook.Worksheets["汇总"];
                Assert.That(detail.Cells[1, 1].Text, Is.EqualTo("订单号"));
                Assert.That(detail.Cells[1, 5].Text, Is.EqualTo("实际软件版本"));
                Assert.That(detail.Cells[1, 13].Text, Is.EqualTo("设备SN读取状态"));
                Assert.That(detail.Cells[1, 21].Text, Is.EqualTo("SN判定"));
                Assert.That(detail.Cells[1, 27].Text, Is.EqualTo("失败原因代码"));
                Assert.That(detail.Cells[1, 29].Text, Is.EqualTo("检测时间"));
                Assert.That(detail.Dimension.Rows, Is.EqualTo(exported.Count + 1));

                var passRow = FindRow(detail, "SN-A");
                Assert.That(detail.Cells[passRow, 1].Text, Is.EqualTo("ORD-1"));
                Assert.That(detail.Cells[passRow, 2].Text, Is.EqualTo("ORD-1_20261124_100000"));
                Assert.That(detail.Cells[passRow, 5].Text, Is.EqualTo("V-ACT"));
                Assert.That(detail.Cells[passRow, 6].Text, Is.EqualTo("V-EXP"));
                Assert.That(detail.Cells[passRow, 7].Text, Is.EqualTo("111"));
                Assert.That(detail.Cells[passRow, 9].Text, Is.EqualTo("AA"));

                var failRow = FindRow(detail, "SN-B");
                Assert.That(detail.Cells[failRow, 26].Text, Is.EqualTo("FAIL"));
                Assert.That(detail.Cells[failRow, 27].Text, Is.EqualTo(Yme009FailReasonCodes.ReadFail));
                Assert.That(detail.Cells[failRow, 28].Text, Is.EqualTo(FailReasonTextResolver.Resolve(Yme009FailReasonCodes.ReadFail)));
                Assert.That(detail.Cells[failRow, 10].Value, Is.Null);
                Assert.That(detail.Cells[failRow, 18].Text, Is.EqualTo("CommandFailed"));
                Assert.That(detail.Cells[failRow, 11].Value, Is.EqualTo(string.Empty));
                Assert.That(detail.Cells[failRow, 19].Text, Is.EqualTo("Missing"));
                Assert.That(detail.Cells[failRow, 23].Text, Is.EqualTo("Fail"));

                Assert.That(FindRow(detail, "SN-C"), Is.GreaterThan(1));
                Assert.That(summary.Cells[2, 1].GetValue<int>(), Is.EqualTo(expected.PassCount));
                Assert.That(summary.Cells[2, 2].GetValue<int>(), Is.EqualTo(expected.BusinessFailCount));
                Assert.That(summary.Cells[2, 3].GetValue<int>(), Is.EqualTo(expected.ReadExceptionCount));
                Assert.That(summary.Cells[2, 4].Text, Is.EqualTo(expected.PassRateText));
                Assert.That(expected.PassCount, Is.EqualTo(1));
                Assert.That(expected.ReadExceptionCount, Is.EqualTo(1));
                Assert.That(expected.PassRateText, Is.EqualTo("100.00%"));
                Assert.That(SheetText(detail, 27), Does.Not.Contain(Yme009FailReasonCodes.CitFail));
            }
        }

        [Test]
        public async Task Should_LeavePassRateBlank_When_ExportedRowsHaveNoBusinessDenominator()
        {
            var records = new List<TestRecord>
            {
                Row(1, "SN", "DEV", "FAIL", Yme009FailReasonCodes.RequiredValueMissing, T(0))
            };
            var storage = new Mock<IStorageService>(MockBehavior.Strict);
            storage.Setup(s => s.GetTestRecordsBySessionAsync(3)).ReturnsAsync(records);
            await new Yme009SessionExporter(storage.Object).ExportAsync(new ExportContext
            {
                SessionId = 3,
                SessionName = "S",
                OrderName = "O",
                OutputDirectory = _outputDir,
                ProductCode = Yme009Product.Code
            });

            using (var package = new ExcelPackage(new FileInfo(Path.Combine(_outputDir, "3.xlsx"))))
            {
                var summary = package.Workbook.Worksheets["汇总"];
                Assert.That(summary.Cells[2, 1].GetValue<int>(), Is.EqualTo(0));
                Assert.That(summary.Cells[2, 2].GetValue<int>(), Is.EqualTo(0));
                Assert.That(summary.Cells[2, 3].GetValue<int>(), Is.EqualTo(1));
                Assert.That(summary.Cells[2, 4].Value, Is.Null);
                Assert.That(summary.Cells[2, 4].Text, Is.Empty);
            }
        }

        private static string SheetText(ExcelWorksheet sheet, int column)
        {
            var text = string.Empty;
            for (var row = 2; row <= sheet.Dimension.Rows; row++)
                text += sheet.Cells[row, column].Text;
            return text;
        }

        private static int FindRow(ExcelWorksheet sheet, string stickerSn)
        {
            for (var row = 2; row <= sheet.Dimension.Rows; row++)
            {
                if (sheet.Cells[row, 3].Text == stickerSn)
                    return row;
            }

            Assert.Fail("未找到扫码 SN " + stickerSn);
            return -1;
        }

        private static TestRecord Row(int id, string stickerSn, string deviceSn, string result, string failReason, DateTime verifyTime)
        {
            return new TestRecord
            {
                Id = id,
                SessionId = 14,
                StickerSN = stickerSn,
                DeviceSN = deviceSn,
                Result = result,
                FailReason = failReason,
                VerifyTime = verifyTime,
                SnJudgment = result == "PASS" ? "Pass" : "Fail"
            };
        }

        private static DateTime T(int seconds)
        {
            return new DateTime(2026, 11, 24, 10, 0, 0).AddSeconds(seconds);
        }
    }
}
