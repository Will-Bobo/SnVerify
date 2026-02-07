using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class ExportAggregationServiceZipTests
    {
        private string _dbPath;
        private string _outDir;
        private StorageService _storage;
        private LoggingService _loggingService;
        private ExportAggregationService _exportAggregationService;

        [SetUp]
        public void SetUp()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Aggregation_{Guid.NewGuid()}.db");
            _outDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Aggregation_Export_{Guid.NewGuid()}");
            Directory.CreateDirectory(_outDir);

            _storage = new StorageService(_dbPath);
            var logDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Aggregation_Logs_{Guid.NewGuid()}");
            _loggingService = new LoggingService(logDir);
            _exportAggregationService = new ExportAggregationService(_storage, _loggingService, _loggingService);
        }

        [TearDown]
        public void TearDown()
        {
            _exportAggregationService?.GetType(); // avoid CA warning about unused field in some analyzers
            _storage?.Dispose();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
            if (Directory.Exists(_outDir))
            {
                Directory.Delete(_outDir, recursive: true);
            }
            _loggingService?.Dispose();
        }

        [Test]
        public async Task ExportByOrderIdAsync_ShouldCreateZip_WithExpectedStructureAndNames()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdX" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-001", CreatedAt = DateTime.Now });

            var sessionName1 = "Order-001_20260126_100000";
            var sessionName2 = "Order-001_20260126_110000";

            var sessionId1 = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName1, StartTime = DateTime.Now });
            var sessionId2 = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName2, StartTime = DateTime.Now.AddMinutes(10) });

            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId1, StickerSN = "S1", DeviceSN = "D1", Result = "PASS", VerifyTime = DateTime.Now });
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId2, StickerSN = "S2", DeviceSN = "D2", Result = "PASS", VerifyTime = DateTime.Now });

            // 为每个 Session 创建对应的日志文件
            _loggingService.StartSession(sessionName1);
            _loggingService.LogInfo($"LOG for {sessionName1}");
            _loggingService.EndBatch();

            _loggingService.StartSession(sessionName2);
            _loggingService.LogInfo($"LOG for {sessionName2}");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByOrderIdAsync("Order-001", _outDir);

            var zipPath = Path.Combine(_outDir, "Order-001.zip");
            Assert.That(File.Exists(zipPath), Is.True, "按订单导出应生成 {OrderName}.zip");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);

                Assert.That(entries.ContainsKey($"Order-001/{sessionName1}.log"), Is.True);
                Assert.That(entries.ContainsKey($"Order-001/{sessionName2}.log"), Is.True);

                // 内容应与 LoggingService 日志一致
                foreach (var sessionName in new[] { sessionName1, sessionName2 })
                {
                    var logPath = _loggingService.GetLogFilePath(sessionName);
                    Assert.That(logPath, Is.Not.Null);

                    var expected = File.ReadAllText(logPath);
                    using (var entryStream = entries[$"Order-001/{sessionName}.log"].Open())
                    using (var reader = new StreamReader(entryStream))
                    {
                        var actual = reader.ReadToEnd();
                        Assert.That(actual, Is.EqualTo(expected));
                    }
                }

                Assert.That(Path.GetFileName(zipPath), Is.EqualTo("Order-001.zip"));
            }
        }

        [Test]
        public async Task ExportByProjectIdAsync_ShouldCreateZip_WithExpectedStructureAndNames()
        {
            await _storage.InitializeAsync();

            var productName = "Prod-A1";
            var productId = await _storage.CreateProductAsync(new Product { ProductName = productName });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-002", CreatedAt = DateTime.Now });

            var sessionName = "Order-002_20260126_120000";
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName, StartTime = DateTime.Now });

            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S1", DeviceSN = "D1", Result = "PASS", VerifyTime = DateTime.Now });

            _loggingService.StartSession(sessionName);
            _loggingService.LogInfo($"LOG for {sessionName}");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByProjectIdAsync(productName, _outDir);

            var zipPath = Path.Combine(_outDir, $"{productName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "按项目导出应生成 {ProductName}.zip");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);

                var entryName = $"{productName}/Order-002/{sessionName}.log";
                Assert.That(entries.ContainsKey(entryName), Is.True);

                var logPath = _loggingService.GetLogFilePath(sessionName);
                Assert.That(logPath, Is.Not.Null);

                var expected = File.ReadAllText(logPath);
                using (var entryStream = entries[entryName].Open())
                using (var reader = new StreamReader(entryStream))
                {
                    var actual = reader.ReadToEnd();
                    Assert.That(actual, Is.EqualTo(expected));
                }
            }
        }

        [Test]
        public async Task Export_WithInvalidCharactersInNames_ShouldSanitizeToFileSystemSafeNames()
        {
            await _storage.InitializeAsync();

            var rawProductName = "Prod:1*Invalid?Name";
            var rawOrderName = "Order/Name:Invalid|";
            var rawSessionName = "Sess<>\"Name";

            var productId = await _storage.CreateProductAsync(new Product { ProductName = rawProductName });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = rawOrderName, CreatedAt = DateTime.Now });

            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = rawSessionName, StartTime = DateTime.Now });

            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S1", DeviceSN = "D1", Result = "PASS", VerifyTime = DateTime.Now });

            _loggingService.StartSession(rawSessionName);
            _loggingService.LogInfo("INVALID NAME LOG");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByProjectIdAsync(rawProductName, _outDir);

            var invalidChars = Path.GetInvalidFileNameChars();
            string Safe(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return "_";
                var chars = name.ToCharArray();
                for (var i = 0; i < chars.Length; i++)
                {
                    if (invalidChars.Contains(chars[i]))
                    {
                        chars[i] = '_';
                    }
                }
                return new string(chars);
            }

            var safeProductName = Safe(rawProductName);
            var safeOrderName = Safe(rawOrderName);
            var safeSessionName = Safe(rawSessionName);

            var zipPath = Path.Combine(_outDir, $"{safeProductName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "ZIP 文件名应使用已清洗的 ProductName");
            // 仅检查文件名部分（完整路径在 Windows 上含 ':' 如 C:\，属于合法路径字符）
            Assert.That(Path.GetFileName(zipPath).IndexOfAny(invalidChars), Is.LessThan(0), "ZIP 文件名中不应包含非法字符");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entryNames = archive.Entries.Select(e => e.FullName).ToList();

                // 目录结构与命名均应使用清洗后的名称（.log）
                Assert.That(entryNames, Does.Contain($"{safeProductName}/{safeOrderName}/{safeSessionName}.log"));

                // ZIP 条目名使用 '/' 作为路径分隔符，合法；仅校验每个路径段（目录名/文件名）不含非法字符
                foreach (var name in entryNames)
                {
                    foreach (var segment in name.Split('/'))
                    {
                        if (string.IsNullOrEmpty(segment)) continue;
                        Assert.That(segment.IndexOfAny(invalidChars), Is.LessThan(0),
                            "ZIP 内部每个路径段均不应包含非法字符: " + segment);
                    }
                }
            }
        }

        [Test]
        public async Task ExportByOrderId_Should_Include_Excel_For_Each_Session()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdX-Excel" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-Excel-001", CreatedAt = DateTime.Now });

            var sessionName1 = "Order-Excel-001_20260126_100000";
            var sessionName2 = "Order-Excel-001_20260126_110000";

            var sessionId1 = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName1, StartTime = DateTime.Now });
            var sessionId2 = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName2, StartTime = DateTime.Now.AddMinutes(10) });

            // 为每个 Session 写入一条 PASS 记录，便于验证 Excel 内容
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId1,
                StickerSN = "STICK-1-1",
                DeviceSN = "DEV-1-1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId2,
                StickerSN = "STICK-2-1",
                DeviceSN = "DEV-2-1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            // 生成对应的日志文件
            _loggingService.StartSession(sessionName1);
            _loggingService.LogInfo($"LOG for {sessionName1}");
            _loggingService.EndBatch();

            _loggingService.StartSession(sessionName2);
            _loggingService.LogInfo($"LOG for {sessionName2}");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByOrderIdAsync("Order-Excel-001", _outDir);

            var zipPath = Path.Combine(_outDir, "Order-Excel-001.zip");
            Assert.That(File.Exists(zipPath), Is.True, "按订单导出应生成 {OrderName}.zip");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);

                // 同时包含日志和 Excel
                Assert.That(entries.ContainsKey($"Order-Excel-001/{sessionName1}.log"), Is.True);
                Assert.That(entries.ContainsKey($"Order-Excel-001/{sessionName2}.log"), Is.True);
                Assert.That(entries.ContainsKey($"Order-Excel-001/{sessionName1}.xlsx"), Is.True);
                Assert.That(entries.ContainsKey($"Order-Excel-001/{sessionName2}.xlsx"), Is.True);

                // 验证每个 Session 的 Excel PASS Sheet 内容与 TestRecord 数据一致
                using (var entryStream = entries[$"Order-Excel-001/{sessionName1}.xlsx"].Open())
                using (var package = new ExcelPackage(entryStream))
                {
                    var passSheet = package.Workbook.Worksheets["PASS"];
                    Assert.That(passSheet, Is.Not.Null);
                    Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(2));

                    var stickerSn = passSheet.Cells[2, 2].Text;
                    var deviceSn = passSheet.Cells[2, 3].Text;
                    var result = passSheet.Cells[2, 4].Text;

                    Assert.That(stickerSn, Is.EqualTo("STICK-1-1"));
                    Assert.That(deviceSn, Is.EqualTo("DEV-1-1"));
                    Assert.That(result, Is.EqualTo("PASS"));
                }

                using (var entryStream = entries[$"Order-Excel-001/{sessionName2}.xlsx"].Open())
                using (var package = new ExcelPackage(entryStream))
                {
                    var passSheet = package.Workbook.Worksheets["PASS"];
                    Assert.That(passSheet, Is.Not.Null);
                    Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(2));

                    var stickerSn = passSheet.Cells[2, 2].Text;
                    var deviceSn = passSheet.Cells[2, 3].Text;
                    var result = passSheet.Cells[2, 4].Text;

                    Assert.That(stickerSn, Is.EqualTo("STICK-2-1"));
                    Assert.That(deviceSn, Is.EqualTo("DEV-2-1"));
                    Assert.That(result, Is.EqualTo("PASS"));
                }
            }
        }

        [Test]
        public async Task ExportByProjectId_Should_Include_Excel_For_Each_Session()
        {
            await _storage.InitializeAsync();

            var productName = "Prod-Excel-A1";
            var productId = await _storage.CreateProductAsync(new Product { ProductName = productName });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-Excel-002", CreatedAt = DateTime.Now });

            var sessionName = "Order-Excel-002_20260126_120000";
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName, StartTime = DateTime.Now });

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "STICK-P-1",
                DeviceSN = "DEV-P-1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            _loggingService.StartSession(sessionName);
            _loggingService.LogInfo($"LOG for {sessionName}");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByProjectIdAsync(productName, _outDir);

            var zipPath = Path.Combine(_outDir, $"{productName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "按项目导出应生成 {ProductName}.zip");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);

                var logEntryName = $"{productName}/Order-Excel-002/{sessionName}.log";
                var excelEntryName = $"{productName}/Order-Excel-002/{sessionName}.xlsx";

                Assert.That(entries.ContainsKey(logEntryName), Is.True);
                Assert.That(entries.ContainsKey(excelEntryName), Is.True);

                using (var entryStream = entries[excelEntryName].Open())
                using (var package = new ExcelPackage(entryStream))
                {
                    var passSheet = package.Workbook.Worksheets["PASS"];
                    Assert.That(passSheet, Is.Not.Null);
                    Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(2));

                    var stickerSn = passSheet.Cells[2, 2].Text;
                    var deviceSn = passSheet.Cells[2, 3].Text;
                    var result = passSheet.Cells[2, 4].Text;

                    Assert.That(stickerSn, Is.EqualTo("STICK-P-1"));
                    Assert.That(deviceSn, Is.EqualTo("DEV-P-1"));
                    Assert.That(result, Is.EqualTo("PASS"));
                }
            }
        }

        /// <summary>
        /// 空 Session：无 TestRecord 或过滤后为空 → 不生成 Excel/TXT，不导出日志到 ZIP
        /// </summary>
        [Test]
        public async Task ExportByOrderId_EmptySession_DoesNotAddExcelOrLogToZip()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdEmpty" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-Empty", CreatedAt = DateTime.Now });

            var sessionName = "Order-Empty_20260126_100000";
            var sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionName, StartTime = DateTime.Now });
            // 无 TestRecord

            _loggingService.StartSession(sessionName);
            _loggingService.LogInfo("LOG for empty session");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByOrderIdAsync("Order-Empty", _outDir);

            var zipPath = Path.Combine(_outDir, "Order-Empty.zip");
            Assert.That(File.Exists(zipPath), Is.True, "ZIP 文件仍会创建（可能为空）");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);
                var excelEntry = $"{ToSafe("Order-Empty")}/{sessionName}.xlsx";
                var logEntry = $"{ToSafe("Order-Empty")}/{sessionName}.log";

                Assert.That(entries.ContainsKey(excelEntry), Is.False, "空 Session 不应包含 Excel");
                Assert.That(entries.ContainsKey(logEntry), Is.False, "空 Session 不应包含日志");
            }
        }

        /// <summary>
        /// 混合 Session：ZIP 中仅包含非空 Session 的 Excel 和日志
        /// </summary>
        [Test]
        public async Task ExportByOrderId_MixedSessions_ZipContainsOnlyNonEmptySessions()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdMixed" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-Mixed", CreatedAt = DateTime.Now });

            var sessionNameEmpty = "Order-Mixed_20260126_100000";
            var sessionNameNonEmpty = "Order-Mixed_20260126_110000";

            var sessionIdEmpty = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionNameEmpty, StartTime = DateTime.Now });
            var sessionIdNonEmpty = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionNameNonEmpty, StartTime = DateTime.Now.AddMinutes(10) });

            // 仅非空 Session 有记录
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionIdNonEmpty, StickerSN = "S1", DeviceSN = "D1", Result = "PASS", VerifyTime = DateTime.Now });

            _loggingService.StartSession(sessionNameEmpty);
            _loggingService.LogInfo("LOG empty");
            _loggingService.EndBatch();

            _loggingService.StartSession(sessionNameNonEmpty);
            _loggingService.LogInfo("LOG non-empty");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByOrderIdAsync("Order-Mixed", _outDir);

            var zipPath = Path.Combine(_outDir, "Order-Mixed.zip");
            Assert.That(File.Exists(zipPath), Is.True);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);
                var orderPrefix = ToSafe("Order-Mixed");

                Assert.That(entries.ContainsKey($"{orderPrefix}/{sessionNameEmpty}.xlsx"), Is.False, "空 Session 不应有 Excel");
                Assert.That(entries.ContainsKey($"{orderPrefix}/{sessionNameEmpty}.log"), Is.False, "空 Session 不应有日志");
                Assert.That(entries.ContainsKey($"{orderPrefix}/{sessionNameNonEmpty}.xlsx"), Is.True, "非空 Session 应有 Excel");
                Assert.That(entries.ContainsKey($"{orderPrefix}/{sessionNameNonEmpty}.log"), Is.True, "非空 Session 应有日志");
            }
        }

        /// <summary>
        /// Filter SnOnly：仅导出 SnMatch 记录，VersionOnly Session 视为空
        /// </summary>
        [Test]
        public async Task ExportByOrderId_WithFilterSnOnly_ExportsOnlySnMatchSessions()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdFilter" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Order-Filter", CreatedAt = DateTime.Now });

            var sessionSn = "Order-Filter_20260126_100000";
            var sessionVer = "Order-Filter_20260126_110000";

            var sessionIdSn = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionSn, StartTime = DateTime.Now });
            var sessionIdVer = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = sessionVer, StartTime = DateTime.Now.AddMinutes(10) });

            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionIdSn, StickerSN = "SN1", DeviceSN = "D1", Result = "PASS", VerifyTime = DateTime.Now });
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionIdVer, StickerSN = "-", DeviceSN = "-", Result = "PASS", VerifyTime = DateTime.Now });

            _loggingService.StartSession(sessionSn);
            _loggingService.LogInfo("LOG Sn");
            _loggingService.EndBatch();
            _loggingService.StartSession(sessionVer);
            _loggingService.LogInfo("LOG Ver");
            _loggingService.EndBatch();

            await _exportAggregationService.ExportByOrderIdAsync("Order-Filter", _outDir, ExportRecordFilter.SnOnly);

            using (var archive = ZipFile.OpenRead(Path.Combine(_outDir, "Order-Filter.zip")))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);
                var prefix = ToSafe("Order-Filter");

                Assert.That(entries.ContainsKey($"{prefix}/{sessionSn}.xlsx"), Is.True, "SnMatch Session 应有 Excel");
                Assert.That(entries.ContainsKey($"{prefix}/{sessionVer}.xlsx"), Is.False, "VersionMatch Session 在 SnOnly 下为空");
            }
        }

        private static string ToSafe(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_";
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (invalidChars.Contains(chars[i])) chars[i] = '_';
            return new string(chars);
        }
    }
}

