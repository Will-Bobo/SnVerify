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
using SnVerify.Infrastructure.Export;
using SnVerify.Services.Storage;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Tests.Services.Storage
{
    [TestFixture]
    public class Km001SessionExporterTests
    {
        private string _outputDir;

        [SetUp]
        public void SetUp()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            _outputDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Km001Export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_outputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir))
            {
                try { Directory.Delete(_outputDir, true); } catch { /* ignore */ }
            }
        }

        [Test]
        public async Task ExportAsync_With_Records_Writes_Summary_PASS_FAIL_Sheets()
        {
            var sessionId = 100;
            var records = new List<TestRecord>
            {
                new TestRecord
                {
                    Id = 1,
                    StickerSN = "STICK1",
                    DeviceSN = "DEV1",
                    Result = "PASS",
                    VerifyTime = new DateTime(2026, 3, 4, 10, 0, 0)
                }
            };

            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            storageMock
                .Setup(s => s.GetTestRecordsBySessionAsync(sessionId))
                .ReturnsAsync(records);

            var registry = new ProductExportRegistry();
            var resolver = new DefaultExportValueResolver();
            var exporter = new Km001SessionExporter(storageMock.Object, registry, resolver);

            var context = new ExportContext
            {
                SessionId = sessionId,
                SessionName = "TestSession",
                OutputDirectory = _outputDir
            };

            await exporter.ExportAsync(context);

            var xlsxPath = Path.Combine(_outputDir, $"{sessionId}.xlsx");
            Assert.That(File.Exists(xlsxPath), Is.True);

            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var summary = package.Workbook.Worksheets["Summary"];
                Assert.That(summary, Is.Not.Null);
                Assert.That(summary.Cells[2, 1].GetValue<int>(), Is.EqualTo(sessionId));
                Assert.That(summary.Cells[2, 3].GetValue<int>(), Is.EqualTo(1));
                Assert.That(summary.Cells[2, 4].GetValue<int>(), Is.EqualTo(1));

                var passSheet = package.Workbook.Worksheets["PASS"];
                Assert.That(passSheet, Is.Not.Null);
                Assert.That(passSheet.Dimension?.Columns, Is.EqualTo(14));
                Assert.That(passSheet.Cells[2, 1].GetValue<int>(), Is.EqualTo(1));
                Assert.That(passSheet.Cells[2, 2].GetValue<string>(), Is.EqualTo("STICK1"));
                Assert.That(passSheet.Cells[2, 3].GetValue<string>(), Is.EqualTo("DEV1"));
                Assert.That(passSheet.Cells[2, 10].GetValue<string>(), Is.EqualTo("PASS"));
                Assert.That(passSheet.Cells[2, 12].GetValue<string>(), Is.EqualTo("2026年3月4日 10:00:00"));

                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(failSheet, Is.Not.Null);
                Assert.That(failSheet.Dimension?.Rows, Is.EqualTo(1), "Only header row when no FAIL records");
            }
        }

        [Test]
        public async Task ExportAsync_Empty_Records_Does_Not_Create_File()
        {
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            storageMock
                .Setup(s => s.GetTestRecordsBySessionAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<TestRecord>());

            var registry = new ProductExportRegistry();
            var exporter = new Km001SessionExporter(storageMock.Object, registry, new DefaultExportValueResolver());
            var context = new ExportContext { SessionId = 99, OutputDirectory = _outputDir };

            await exporter.ExportAsync(context);

            Assert.That(File.Exists(Path.Combine(_outputDir, "99.xlsx")), Is.False);
        }

        [Test]
        public void ExportAsync_NullContext_Throws()
        {
            var storageMock = new Mock<IStorageService>(MockBehavior.Loose);
            var exporter = new Km001SessionExporter(storageMock.Object, new ProductExportRegistry(), new DefaultExportValueResolver());
            Assert.ThrowsAsync<ArgumentNullException>(async () => await exporter.ExportAsync(null));
        }

        [Test]
        public void ExportAsync_Empty_OutputDirectory_Throws()
        {
            var storageMock = new Mock<IStorageService>(MockBehavior.Loose);
            var exporter = new Km001SessionExporter(storageMock.Object, new ProductExportRegistry(), new DefaultExportValueResolver());
            var context = new ExportContext { SessionId = 1, OutputDirectory = "" };
            Assert.ThrowsAsync<ArgumentException>(async () => await exporter.ExportAsync(context));
        }
    }
}
