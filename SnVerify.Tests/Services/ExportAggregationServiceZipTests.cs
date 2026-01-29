using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
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
        private ExportAggregationService _exportAggregationService;

        [SetUp]
        public void SetUp()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Aggregation_{Guid.NewGuid()}.db");
            _outDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Aggregation_Export_{Guid.NewGuid()}");
            Directory.CreateDirectory(_outDir);

            _storage = new StorageService(_dbPath);
            _exportAggregationService = new ExportAggregationService(_storage, new NullFileLogger());
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

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId1,
                StickerSN = "S1",
                DeviceSN = "D1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId2,
                StickerSN = "S2",
                DeviceSN = "D2",
                Result = "FAIL",
                FailReason = "mismatch",
                VerifyTime = DateTime.Now
            });

            await _exportAggregationService.ExportByOrderIdAsync("Order-001", _outDir);

            var zipPath = Path.Combine(_outDir, "Order-001.zip");
            Assert.That(File.Exists(zipPath), Is.True, "按订单导出应生成 {OrderName}.zip");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var names = archive.Entries.Select(e => e.FullName).ToList();

                Assert.That(names, Does.Contain($"Order-001/{sessionName1}.xlsx"));
                Assert.That(names, Does.Contain($"Order-001/{sessionName1}.txt"));
                Assert.That(names, Does.Contain($"Order-001/{sessionName2}.xlsx"));
                Assert.That(names, Does.Contain($"Order-001/{sessionName2}.txt"));

                // 不使用内部 SessionId / Order 数据库 Id 作为文件名
                Assert.That(names.Any(n => n.Contains($"{sessionId1}.xlsx") || n.Contains($"{sessionId1}.txt")), Is.False);
                Assert.That(names.Any(n => n.Contains($"{sessionId2}.xlsx") || n.Contains($"{sessionId2}.txt")), Is.False);
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

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S1",
                DeviceSN = "D1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            await _exportAggregationService.ExportByProjectIdAsync(productName, _outDir);

            var zipPath = Path.Combine(_outDir, $"{productName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "按项目导出应生成 {ProductName}.zip");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var names = archive.Entries.Select(e => e.FullName).ToList();

                Assert.That(names, Does.Contain($"{productName}/Order-002/{sessionName}.xlsx"));
                Assert.That(names, Does.Contain($"{productName}/Order-002/{sessionName}.txt"));

                Assert.That(names.Any(n => n.Contains($"{sessionId}.xlsx") || n.Contains($"{sessionId}.txt")), Is.False);
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

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S-RAW",
                DeviceSN = "D-RAW",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

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
            Assert.That(zipPath.IndexOfAny(invalidChars), Is.LessThan(0), "ZIP 文件名中不应包含非法字符");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entryNames = archive.Entries.Select(e => e.FullName).ToList();

                // 目录结构与命名均应使用清洗后的名称
                Assert.That(entryNames, Does.Contain($"{safeProductName}/{safeOrderName}/{safeSessionName}.xlsx"));
                Assert.That(entryNames, Does.Contain($"{safeProductName}/{safeOrderName}/{safeSessionName}.txt"));

                foreach (var name in entryNames)
                {
                    Assert.That(name.IndexOfAny(invalidChars), Is.LessThan(0), "ZIP 内部任何条目名称均不应包含非法字符");
                }
            }
        }
    }
}

