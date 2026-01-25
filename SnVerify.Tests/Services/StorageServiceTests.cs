/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Models;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// StorageService 单元测试
    /// </summary>
    [TestFixture]
    public class StorageServiceTests
    {
        private IStorageService _storageService;
        private string _testDbPath;
        private string _testOutputDir;

        [SetUp]
        public void SetUp()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Test_{Guid.NewGuid()}.db");
            _testOutputDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Export_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testOutputDir);

            _storageService = new StorageService(_testDbPath);
        }

        [TearDown]
        public void TearDown()
        {
            _storageService?.Dispose();
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
            if (Directory.Exists(_testOutputDir))
            {
                Directory.Delete(_testOutputDir, true);
            }
        }

        [Test]
        public async Task InitializeAsync_ShouldCreateDatabaseAndTables()
        {
            // Act
            await _storageService.InitializeAsync();

            // Assert
            Assert.That(File.Exists(_testDbPath), Is.True, "数据库文件应该被创建");

            using (var connection = new SQLiteConnection($"Data Source={_testDbPath}"))
            {
                connection.Open();
                var tables = connection.GetSchema("Tables");
                var tableNames = tables.Rows.Cast<System.Data.DataRow>()
                    .Select(row => row["TABLE_NAME"].ToString())
                    .ToList();

                Assert.That(tableNames, Does.Contain("Batch"), "应该包含 Batch 表");
                Assert.That(tableNames, Does.Contain("SnVerifyResult"), "应该包含 SnVerifyResult 表");
            }
        }

        [Test]
        public async Task CreateBatchAsync_ShouldSaveBatchInfo()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo
            {
                BatchId = "BATCH001",
                StartTime = DateTime.Now,
                Operator = "TestOperator",
                Remark = "Test Remark"
            };

            // Act
            await _storageService.CreateBatchAsync(batch);

            // Assert
            var exists = await _storageService.BatchExistsAsync("BATCH001");
            Assert.That(exists, Is.True, "批次应该存在");
        }

        [Test]
        public async Task BatchExistsAsync_ShouldReturnFalseForNonExistentBatch()
        {
            // Arrange
            await _storageService.InitializeAsync();

            // Act
            var exists = await _storageService.BatchExistsAsync("NONEXISTENT");

            // Assert
            Assert.That(exists, Is.False, "不存在的批次应该返回 false");
        }

        [Test]
        public async Task IsSnDuplicateAsync_ShouldReturnFalseForNewSn()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch);

            // Act
            var isDuplicate = await _storageService.IsSnDuplicateAsync("BATCH001", "SN001");

            // Assert
            Assert.That(isDuplicate, Is.False, "新 SN 不应该重复");
        }

        [Test]
        public async Task IsSnDuplicateAsync_ShouldReturnTrueForDuplicateSn()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch);

            var result1 = new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN001",
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(result1);

            // Act
            var isDuplicate = await _storageService.IsSnDuplicateAsync("BATCH001", "SN001");

            // Assert
            Assert.That(isDuplicate, Is.True, "重复的 SN 应该返回 true");
        }

        [Test]
        public async Task IsSnDuplicateAsync_ShouldNotDetectDuplicateAcrossBatches()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch1 = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            var batch2 = new BatchInfo { BatchId = "BATCH002", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch1);
            await _storageService.CreateBatchAsync(batch2);

            var result1 = new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN001",
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(result1);

            // Act
            var isDuplicate = await _storageService.IsSnDuplicateAsync("BATCH002", "SN001");

            // Assert
            Assert.That(isDuplicate, Is.False, "不同批次间的相同 SN 不应该被视为重复");
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldPersistResult()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch);

            var result = new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN001",
                Result = "PASS",
                FailReason = null,
                VerifyTime = DateTime.Now
            };

            // Act
            await _storageService.SaveVerifyResultAsync(result);

            // Assert
            var results = await _storageService.GetResultsByBatchAsync("BATCH001");
            Assert.That(results.Count, Is.EqualTo(1), "应该有一条结果记录");
            Assert.That(results[0].SN, Is.EqualTo("SN001"), "SN 应该匹配");
            Assert.That(results[0].Result, Is.EqualTo("PASS"), "结果应该匹配");
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldSaveFailResultWithReason()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch);

            var result = new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN001",
                Result = "FAIL",
                FailReason = "DUPLICATE_SN",
                VerifyTime = DateTime.Now
            };

            // Act
            await _storageService.SaveVerifyResultAsync(result);

            // Assert
            var results = await _storageService.GetResultsByBatchAsync("BATCH001");
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Result, Is.EqualTo("FAIL"));
            Assert.That(results[0].FailReason, Is.EqualTo("DUPLICATE_SN"));
        }

        [Test]
        public async Task GetResultsByBatchAsync_ShouldReturnOnlyResultsForSpecifiedBatch()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch1 = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            var batch2 = new BatchInfo { BatchId = "BATCH002", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch1);
            await _storageService.CreateBatchAsync(batch2);

            await _storageService.SaveVerifyResultAsync(new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN001",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            await _storageService.SaveVerifyResultAsync(new SnVerifyResult
            {
                BatchId = "BATCH002",
                SN = "SN002",
                Result = "FAIL",
                FailReason = "MISMATCH",
                VerifyTime = DateTime.Now
            });

            // Act
            var results1 = await _storageService.GetResultsByBatchAsync("BATCH001");
            var results2 = await _storageService.GetResultsByBatchAsync("BATCH002");

            // Assert
            Assert.That(results1.Count, Is.EqualTo(1));
            Assert.That(results1[0].SN, Is.EqualTo("SN001"));
            Assert.That(results2.Count, Is.EqualTo(1));
            Assert.That(results2[0].SN, Is.EqualTo("SN002"));
        }

        [Test]
        public async Task ExportBatchResultAsync_ShouldCreateExcelFileWithTwoSheets()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch);

            await _storageService.SaveVerifyResultAsync(new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN001",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            await _storageService.SaveVerifyResultAsync(new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN002",
                Result = "FAIL",
                FailReason = "MISMATCH",
                VerifyTime = DateTime.Now
            });

            await _storageService.SaveVerifyResultAsync(new SnVerifyResult
            {
                BatchId = "BATCH001",
                SN = "SN003",
                Result = "TIMEOUT",
                FailReason = "TIMEOUT",
                VerifyTime = DateTime.Now
            });

            // Act
            await _storageService.ExportBatchResultAsync("BATCH001", _testOutputDir);

            // Assert
            var expectedFilePath = Path.Combine(_testOutputDir, "BATCH001.xlsx");
            Assert.That(File.Exists(expectedFilePath), Is.True, "Excel 文件应该被创建");

            using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(expectedFilePath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];

                Assert.That(passSheet, Is.Not.Null, "应该包含 PASS Sheet");
                Assert.That(failSheet, Is.Not.Null, "应该包含 FAIL Sheet");

                // PASS Sheet 应该有 1 条记录（SN001）
                Assert.That(passSheet.Dimension.Rows, Is.EqualTo(2), "PASS Sheet 应该有标题行 + 1 条数据");
                Assert.That(passSheet.Cells[2, 2].Value?.ToString(), Is.EqualTo("SN001"), "PASS Sheet 应该包含 SN001");

                // FAIL Sheet 应该有 2 条记录（SN002, SN003）
                Assert.That(failSheet.Dimension.Rows, Is.EqualTo(3), "FAIL Sheet 应该有标题行 + 2 条数据");
            }
        }

        [Test]
        public async Task ExportBatchResultAsync_ShouldHandleEmptyBatch()
        {
            // Arrange
            await _storageService.InitializeAsync();
            var batch = new BatchInfo { BatchId = "BATCH001", StartTime = DateTime.Now };
            await _storageService.CreateBatchAsync(batch);

            // Act
            await _storageService.ExportBatchResultAsync("BATCH001", _testOutputDir);

            // Assert
            var expectedFilePath = Path.Combine(_testOutputDir, "BATCH001.xlsx");
            Assert.That(File.Exists(expectedFilePath), Is.True, "Excel 文件应该被创建");

            using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(expectedFilePath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];

                Assert.That(passSheet, Is.Not.Null);
                Assert.That(failSheet, Is.Not.Null);

                // 只有标题行
                Assert.That(passSheet.Dimension.Rows, Is.EqualTo(1));
                Assert.That(failSheet.Dimension.Rows, Is.EqualTo(1));
            }
        }
    }
}
