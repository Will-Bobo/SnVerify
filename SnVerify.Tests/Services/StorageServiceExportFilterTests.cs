/// <author>AI Assistant</author>
/// <remarks>
/// ExportBySessionAsync 按 ExportRecordFilter 过滤的单元测试（TDD）。
/// 约定：StickerSN=="-" 为 VersionMatch 记录，否则为 SnMatch。
/// </remarks>

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Services.Rules;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class StorageServiceExportFilterTests
    {
        private IStorageService _storage;
        private string _dbPath;
        private string _outDir;

        [SetUp]
        public void SetUp()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_ExportFilter_{Guid.NewGuid()}.db");
            _outDir = Path.Combine(Path.GetTempPath(), $"SnVerify_ExportFilter_Out_{Guid.NewGuid()}");
            Directory.CreateDirectory(_outDir);
            _storage = new StorageService(_dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            _storage?.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_outDir)) Directory.Delete(_outDir, true);
        }

        [Test]
        public async Task ExportBySessionAsync_WithFilterSnOnly_ExportsOnlySnMatchRecords()
        {
            await _storage.InitializeAsync();
            var (productId, orderId, sessionId) = await CreateSessionWithMixedRecordsAsync();

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.SnOnly);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            Assert.That(File.Exists(xlsxPath), Is.True);
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(passSheet, Is.Not.Null);
                Assert.That(failSheet, Is.Not.Null);
                // SnOnly: 仅 S1/D1 (PASS), S2/D2 (FAIL 去重后1条)
                Assert.That(passSheet.Dimension?.Rows ?? 0, Is.EqualTo(2), "PASS: header + 1 SN record");
                Assert.That(failSheet.Dimension?.Rows ?? 0, Is.EqualTo(2), "FAIL: header + 1 SN record");
                Assert.That(passSheet.Cells[2, 2].Text, Is.EqualTo("S1"));
                Assert.That(failSheet.Cells[2, 2].Text, Is.EqualTo("S2"));
                // SnMatch 类型：版本列为空
                Assert.That(passSheet.Cells[2, 7].Text, Is.Empty, "SnMatch 记录目标版本号应为空");
                Assert.That(passSheet.Cells[2, 8].Text, Is.Empty, "SnMatch 记录设备版本号应为空");
                Assert.That(failSheet.Cells[2, 7].Text, Is.Empty, "SnMatch 记录目标版本号应为空");
                Assert.That(failSheet.Cells[2, 8].Text, Is.Empty, "SnMatch 记录设备版本号应为空");
            }
        }

        [Test]
        public async Task ExportBySessionAsync_WithFilterVersionOnly_ExportsOnlyVersionMatchRecords()
        {
            await _storage.InitializeAsync();
            var (productId, orderId, sessionId) = await CreateSessionWithMixedRecordsAsync();

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.VersionOnly);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            Assert.That(File.Exists(xlsxPath), Is.True);
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(passSheet, Is.Not.Null);
                Assert.That(failSheet, Is.Not.Null);
                // VersionOnly: 仅 StickerSN="-"
                Assert.That(passSheet.Dimension?.Rows ?? 0, Is.EqualTo(2), "PASS: header + 1 Version record");
                Assert.That(failSheet.Dimension?.Rows ?? 0, Is.EqualTo(2), "FAIL: header + 1 Version record");
                Assert.That(passSheet.Cells[2, 2].Text, Is.EqualTo("-"));
                Assert.That(failSheet.Cells[2, 2].Text, Is.EqualTo("-"));
                // VersionMatch 类型：目标版本号、设备版本号正确
                Assert.That(passSheet.Cells[2, 7].Text, Is.EqualTo("1.0"), "VersionMatch PASS 目标版本号");
                Assert.That(passSheet.Cells[2, 8].Text, Is.EqualTo("1.0"), "VersionMatch PASS 设备版本号");
                Assert.That(failSheet.Cells[2, 7].Text, Is.EqualTo("1.0"), "VersionMatch FAIL 目标版本号");
                Assert.That(failSheet.Cells[2, 8].Text, Is.EqualTo("1.1"), "VersionMatch FAIL 设备版本号");
            }
        }

        [Test]
        public async Task ExportBySessionAsync_WithFilterAll_ExportsAllRecords()
        {
            await _storage.InitializeAsync();
            var (productId, orderId, sessionId) = await CreateSessionWithMixedRecordsAsync();

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.All);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            Assert.That(File.Exists(xlsxPath), Is.True);
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(passSheet, Is.Not.Null);
                Assert.That(failSheet, Is.Not.Null);
                Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(3), "PASS: header + 2 records");
                Assert.That(failSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(3), "FAIL: header + 2 records");
                // All 导出：PASS/FAIL Sheet 都有版本列；SnMatch 列为空，VersionMatch 有值
                Assert.That(passSheet.Cells[1, 7].Text, Is.EqualTo("目标版本号"));
                Assert.That(passSheet.Cells[1, 8].Text, Is.EqualTo("设备版本号"));
                Assert.That(failSheet.Cells[1, 7].Text, Is.EqualTo("目标版本号"));
                Assert.That(failSheet.Cells[1, 8].Text, Is.EqualTo("设备版本号"));
            }
        }

        /// <summary>
        /// Session 下有记录，但过滤后为空 → 不生成任何文件（Export_Semantics 空记录规则）
        /// </summary>
        [Test]
        public async Task ExportBySessionAsync_WhenFilterReturnsEmpty_DoesNotCreateAnyFiles()
        {
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O", CreatedAt = DateTime.Now });
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O_20260126_120000", StartTime = DateTime.Now });
            // 仅 SnMatch 记录
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "SN1", DeviceSN = "DN1", Result = "PASS", VerifyTime = DateTime.Now });

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.VersionOnly);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            var txtPath = Path.Combine(_outDir, $"{sessionId}.txt");
            Assert.That(File.Exists(xlsxPath), Is.False, "过滤后为空不应生成 XLSX");
            Assert.That(File.Exists(txtPath), Is.False, "过滤后为空不应生成 TXT");
        }

        /// <summary>
        /// Session 下没有任何 TestRecord → 不生成任何文件
        /// </summary>
        [Test]
        public async Task ExportBySessionAsync_WhenSessionHasNoRecords_DoesNotCreateAnyFiles()
        {
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O", CreatedAt = DateTime.Now });
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O_20260126_120000", StartTime = DateTime.Now });
            // 无任何 TestRecord

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.All);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            var txtPath = Path.Combine(_outDir, $"{sessionId}.txt");
            Assert.That(File.Exists(xlsxPath), Is.False, "Session 无记录不应生成 XLSX");
            Assert.That(File.Exists(txtPath), Is.False, "Session 无记录不应生成 TXT");
        }

        /// <summary>
        /// Session 下有记录，过滤后非空 → 生成 XLSX 和 TXT，验证 Sheet 名称、行数、列顺序、TXT 内容
        /// </summary>
        [Test]
        public async Task ExportBySessionAsync_WhenFilterReturnsRecords_CreatesXlsxAndTxtWithCorrectContent()
        {
            await _storage.InitializeAsync();
            var (_, _, sessionId) = await CreateSessionWithMixedRecordsAsync();

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.All);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            var txtPath = Path.Combine(_outDir, $"{sessionId}.txt");
            Assert.That(File.Exists(xlsxPath), Is.True);
            Assert.That(File.Exists(txtPath), Is.True);

            // 验证 Excel Sheet 名称、行数、列顺序
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(passSheet, Is.Not.Null);
                Assert.That(failSheet, Is.Not.Null);
                Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(3), "PASS: header + 至少 2 条记录");
                Assert.That(failSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(3), "FAIL: header + 至少 2 条记录");
                // 列顺序：Id, 条形码SN, 设备SN, 检验结果, 失败原因, 检验时间, 目标版本号, 设备版本号
                Assert.That(passSheet.Cells[1, 1].Text, Is.EqualTo("Id"));
                Assert.That(passSheet.Cells[1, 2].Text, Is.EqualTo("条形码SN"));
                Assert.That(passSheet.Cells[1, 3].Text, Is.EqualTo("设备SN"));
                Assert.That(passSheet.Cells[1, 4].Text, Is.EqualTo("检验结果"));
                Assert.That(passSheet.Cells[1, 5].Text, Is.EqualTo("失败原因"));
                Assert.That(passSheet.Cells[1, 6].Text, Is.EqualTo("检验时间"));
                Assert.That(passSheet.Cells[1, 7].Text, Is.EqualTo("目标版本号"));
                Assert.That(passSheet.Cells[1, 8].Text, Is.EqualTo("设备版本号"));
            }

            // 验证 TXT 文件行数和内容
            var txtLines = File.ReadAllLines(txtPath, System.Text.Encoding.UTF8);
            Assert.That(txtLines.Length, Is.GreaterThanOrEqualTo(3), "TXT: SessionId + PASS/FAIL 统计 + 至少 1 条记录");
            Assert.That(txtLines[0], Does.StartWith("SessionId:"));
            Assert.That(txtLines[1], Does.Contain("PASS:"));
            Assert.That(txtLines[1], Does.Contain("FAIL(去重后):"));
        }

        [Test]
        public async Task ExportBySessionAsync_OriginalOverload_DefaultsToAll()
        {
            await _storage.InitializeAsync();
            var (productId, orderId, sessionId) = await CreateSessionWithMixedRecordsAsync();

            await _storage.ExportBySessionAsync(sessionId, _outDir);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            Assert.That(File.Exists(xlsxPath), Is.True);
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(3), "默认 All 导出全部");
            }
        }

        [Test]
        public async Task ExportBySessionAsync_SnMatchRecord_WithVersionFields_ShouldWriteVersionColumns()
        {
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O", CreatedAt = DateTime.Now });
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O_20260701_120000", StartTime = DateTime.Now });

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S1",
                DeviceSN = "D1",
                Result = "PASS",
                ExpectedVersion = "V1.0",
                ActualVersion = "V1.0",
                VerifyTime = DateTime.Now
            });

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.SnOnly);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                Assert.That(passSheet.Cells[2, 7].Text, Is.EqualTo("V1.0"));
                Assert.That(passSheet.Cells[2, 8].Text, Is.EqualTo("V1.0"));
            }
        }

        [Test]
        public async Task UpdateTestRecordAsync_ShouldPersistExpectedAndActualVersion()
        {
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O", CreatedAt = DateTime.Now });
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O_20260701_130000", StartTime = DateTime.Now });

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S1",
                DeviceSN = "D1",
                Result = "FAIL",
                VerifyTime = DateTime.Now
            });

            var records = await _storage.GetTestRecordsBySessionAsync(sessionId);
            var record = records.Single();
            record.ExpectedVersion = "V2.0";
            record.ActualVersion = "V9.9";
            record.FailReason = "retry";
            await _storage.UpdateTestRecordAsync(record);

            var updated = (await _storage.GetTestRecordsBySessionAsync(sessionId)).Single();
            Assert.That(updated.ExpectedVersion, Is.EqualTo("V2.0"));
            Assert.That(updated.ActualVersion, Is.EqualTo("V9.9"));
        }

        [Test]
        public async Task ExportBySessionAsync_ShouldResolveFailReasonCode_ToLocalizedText()
        {
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O", CreatedAt = DateTime.Now });
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O_20260701_140000", StartTime = DateTime.Now });

            const string legacyChineseReason = "设备SN 与 条形码SN [不匹配]";
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S1",
                DeviceSN = "D1",
                Result = "FAIL",
                FailReason = RuleFailReasonCodes.AndroidVersionMismatch,
                ExpectedVersion = "V1.0",
                ActualVersion = "V9.9",
                VerifyTime = DateTime.Now
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S2",
                DeviceSN = "D2",
                Result = "FAIL",
                FailReason = legacyChineseReason,
                VerifyTime = DateTime.Now
            });

            await _storage.ExportBySessionAsync(sessionId, _outDir, ExportRecordFilter.All);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(failSheet.Cells[2, 5].Text, Is.EqualTo("设备Android版本号与目标值不匹配"));
                Assert.That(failSheet.Cells[3, 5].Text, Is.EqualTo(legacyChineseReason));
            }

            var txtPath = Path.Combine(_outDir, $"{sessionId}.txt");
            var txt = File.ReadAllText(txtPath, System.Text.Encoding.UTF8);
            Assert.That(txt, Does.Contain("设备Android版本号与目标值不匹配"));
            Assert.That(txt, Does.Contain(legacyChineseReason));
        }

        private async Task<(int productId, int orderId, int sessionId)> CreateSessionWithMixedRecordsAsync()
        {
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O", CreatedAt = DateTime.Now });
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O_20260126_120000", StartTime = DateTime.Now });
            var baseTime = DateTime.Now;

            // SnMatch
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S1", DeviceSN = "D1", Result = "PASS", VerifyTime = baseTime });
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S2", DeviceSN = "D2", Result = "FAIL", FailReason = "mismatch", VerifyTime = baseTime.AddSeconds(1) });
            // VersionMatch (StickerSN="-")
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "-", DeviceSN = "-", Result = "PASS", ExpectedVersion = "1.0", ActualVersion = "1.0", VerifyTime = baseTime.AddSeconds(2) });
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "-", DeviceSN = "-", Result = "FAIL", FailReason = "ver mismatch", ExpectedVersion = "1.0", ActualVersion = "1.1", VerifyTime = baseTime.AddSeconds(3) });

            return (productId, orderId, sessionId);
        }
    }
}
