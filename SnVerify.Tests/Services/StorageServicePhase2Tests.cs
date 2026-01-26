/// <author>
/// AI Assistant
/// </author>

using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// StorageService Phase2 单元测试
    /// </summary>
    [TestFixture]
    public class StorageServicePhase2Tests
    {
        private IStorageService _storageService;
        private string _testDbPath;
        private string _testOutputDir;
        private const string TestBatchId = "BATCH001";
        private const string TestSn1 = "SN001";
        private const string TestSn2 = "SN002";

        [SetUp]
        public async Task SetUp()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Phase2_Test_{Guid.NewGuid()}.db");
            _testOutputDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Phase2_Export_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testOutputDir);

            _storageService = new StorageService(_testDbPath);
            await _storageService.InitializeAsync();

            // 创建测试批次
            await _storageService.CreateBatchAsync(new BatchInfo
            {
                BatchId = TestBatchId,
                StartTime = DateTime.Now,
                Operator = "TestOperator",
                Remark = "Test Batch"
            });
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
        public void Snapshot_ShouldReturnInitialIdleState()
        {
            // Assert
            Assert.That(_storageService.Snapshot.IsProcessing, Is.False);
            Assert.That(_storageService.Snapshot.LastSavedSN, Is.Null);
            Assert.That(_storageService.Snapshot.BatchId, Is.Null);
            Assert.That(_storageService.Snapshot.ErrorMessage, Is.Null);
            Assert.That(_storageService.Snapshot.RecordCount, Is.EqualTo(0));
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldUpdateSnapshot_WhenSuccess()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // Act
            await _storageService.SaveVerifyResultAsync(result);

            // Assert
            Assert.That(_storageService.Snapshot.IsProcessing, Is.False);
            Assert.That(_storageService.Snapshot.LastSavedSN, Is.EqualTo(TestSn1));
            Assert.That(_storageService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_storageService.Snapshot.ErrorMessage, Is.Null);
            Assert.That(_storageService.Snapshot.RecordCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldUpdateSnapshot_WhenDuplicateSn()
        {
            // Arrange
            var result1 = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(result1);

            var result2 = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1, // 重复 SN
                Result = "FAIL",
                VerifyTime = DateTime.Now
            };

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _storageService.SaveVerifyResultAsync(result2));

            Assert.That(_storageService.Snapshot.IsProcessing, Is.False);
            Assert.That(_storageService.Snapshot.LastSavedSN, Is.EqualTo(TestSn1));
            Assert.That(_storageService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_storageService.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("already exists"));
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldUpdateRecordCount_WhenMultipleResults()
        {
            // Arrange
            var result1 = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            var result2 = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn2,
                Result = "FAIL",
                FailReason = "SN mismatch",
                VerifyTime = DateTime.Now
            };

            // Act
            await _storageService.SaveVerifyResultAsync(result1);
            await _storageService.SaveVerifyResultAsync(result2);

            // Assert
            Assert.That(_storageService.Snapshot.RecordCount, Is.EqualTo(2));
            Assert.That(_storageService.Snapshot.LastSavedSN, Is.EqualTo(TestSn2));
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldUpdateSnapshot_WhenDatabaseError()
        {
            // Arrange - 使用格式正确但无法访问的路径（不存在的驱动器）
            var invalidService = new StorageService(@"Z:\NonExistentDrive\test.db");
            
            // InitializeAsync 可能会抛出异常，使用 try-catch 捕获
            Exception initException = null;
            try
            {
                await invalidService.InitializeAsync();
            }
            catch (Exception ex)
            {
                initException = ex;
            }

            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // Act & Assert - 如果 InitializeAsync 失败，SaveVerifyResultAsync 也会失败
            Exception caughtException = null;
            try
            {
                await invalidService.SaveVerifyResultAsync(result);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            Assert.That(caughtException, Is.Not.Null);
            invalidService.Dispose();
        }

        [Test]
        public async Task ExportBatchResultAsync_ShouldUpdateSnapshot_WhenSuccess()
        {
            // Arrange
            var passResult = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            var failResult = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn2,
                Result = "FAIL",
                FailReason = "SN mismatch",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(passResult);
            await _storageService.SaveVerifyResultAsync(failResult);

            // Act
            await _storageService.ExportBatchResultAsync(TestBatchId, _testOutputDir);

            // Assert
            Assert.That(_storageService.Snapshot.IsProcessing, Is.False);
            Assert.That(_storageService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_storageService.Snapshot.RecordCount, Is.EqualTo(2));
            Assert.That(_storageService.Snapshot.ErrorMessage, Is.Null);

            // 验证文件已生成
            var expectedFilePath = Path.Combine(_testOutputDir, $"{TestBatchId}.xlsx");
            Assert.That(File.Exists(expectedFilePath), Is.True);
        }

        [Test]
        public async Task ExportBatchResultAsync_ShouldCreatePassAndFailSheets()
        {
            // Arrange
            var passResult = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            var failResult = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn2,
                Result = "FAIL",
                FailReason = "SN mismatch",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(passResult);
            await _storageService.SaveVerifyResultAsync(failResult);

            // Act
            await _storageService.ExportBatchResultAsync(TestBatchId, _testOutputDir);

            // Assert
            var filePath = Path.Combine(_testOutputDir, $"{TestBatchId}.xlsx");
            Assert.That(File.Exists(filePath), Is.True);

            // 验证 Excel 文件包含 PASS 和 FAIL Sheet
            using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(filePath)))
            {
                Assert.That(package.Workbook.Worksheets.Count, Is.EqualTo(2));
                Assert.That(package.Workbook.Worksheets.Any(w => w.Name == "PASS"), Is.True);
                Assert.That(package.Workbook.Worksheets.Any(w => w.Name == "FAIL"), Is.True);

                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];

                // PASS Sheet 应包含 1 条记录（表头 + 1 行数据）
                Assert.That(passSheet.Dimension.Rows, Is.EqualTo(2));
                Assert.That(passSheet.Cells[2, 2].Value.ToString(), Is.EqualTo(TestSn1));

                // FAIL Sheet 应包含 1 条记录（表头 + 1 行数据）
                Assert.That(failSheet.Dimension.Rows, Is.EqualTo(2));
                Assert.That(failSheet.Cells[2, 2].Value.ToString(), Is.EqualTo(TestSn2));
            }
        }

        [Test]
        public async Task ExportBatchResultAsync_ShouldFormatVerifyTimeAsYyyyMmDdHhMmSs()
        {
            // Arrange - 使用固定的时间以便验证格式
            var verifyTime = new DateTime(2026, 1, 26, 13, 45, 30);
            var expectedFormat = "2026年1月26日 13:45:30";

            var passResult = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = verifyTime
            };
            var failResult = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn2,
                Result = "FAIL",
                FailReason = "SN mismatch",
                VerifyTime = verifyTime
            };
            await _storageService.SaveVerifyResultAsync(passResult);
            await _storageService.SaveVerifyResultAsync(failResult);

            // Act
            await _storageService.ExportBatchResultAsync(TestBatchId, _testOutputDir);

            // Assert
            var filePath = Path.Combine(_testOutputDir, $"{TestBatchId}.xlsx");
            Assert.That(File.Exists(filePath), Is.True);

            using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(filePath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                var failSheet = package.Workbook.Worksheets["FAIL"];

                // 验证 PASS Sheet 中的 VerifyTime 格式（第 5 列，VerifyTime 列）
                var passVerifyTimeCell = passSheet.Cells[2, 5];
                Assert.That(passVerifyTimeCell.Value, Is.Not.Null, "PASS Sheet 的 VerifyTime 应该有值");
                Assert.That(passVerifyTimeCell.Value, Is.InstanceOf<string>(), "VerifyTime 应该是字符串类型，而不是日期序列号");
                Assert.That(passVerifyTimeCell.Value.ToString(), Is.EqualTo(expectedFormat), 
                    $"PASS Sheet 的 VerifyTime 应该是 yyyy年M月d日 HH:mm:ss 格式，期望: {expectedFormat}");

                // 验证 FAIL Sheet 中的 VerifyTime 格式
                var failVerifyTimeCell = failSheet.Cells[2, 5];
                Assert.That(failVerifyTimeCell.Value, Is.Not.Null, "FAIL Sheet 的 VerifyTime 应该有值");
                Assert.That(failVerifyTimeCell.Value, Is.InstanceOf<string>(), "VerifyTime 应该是字符串类型，而不是日期序列号");
                Assert.That(failVerifyTimeCell.Value.ToString(), Is.EqualTo(expectedFormat), 
                    $"FAIL Sheet 的 VerifyTime 应该是 yyyy年M月d日 HH:mm:ss 格式，期望: {expectedFormat}");
            }
        }

        [Test]
        public async Task ExportBatchResultAsync_ShouldUpdateSnapshot_WhenError()
        {
            // Arrange
            var invalidDirectory = Path.Combine("Z:", "InvalidPath"); // 假设 Z: 盘不存在

            // Act & Assert - 使用 try-catch 捕获异常（允许捕获派生异常类型）
            Exception caughtException = null;
            try
            {
                await _storageService.ExportBatchResultAsync(TestBatchId, invalidDirectory);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            Assert.That(caughtException, Is.Not.Null);
            Assert.That(_storageService.Snapshot.IsProcessing, Is.False);
            Assert.That(_storageService.Snapshot.ErrorMessage, Is.Not.Null);
        }

        [Test]
        public async Task SaveVerifyResultAsync_ShouldHandleConcurrentWrites()
        {
            // Arrange
            var tasks = Enumerable.Range(1, 10).Select(i => new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = $"SN{i:D3}",
                Result = i % 2 == 0 ? "PASS" : "FAIL",
                VerifyTime = DateTime.Now
            }).Select(result => _storageService.SaveVerifyResultAsync(result));

            // Act
            await Task.WhenAll(tasks);

            // Assert
            Assert.That(_storageService.Snapshot.RecordCount, Is.EqualTo(10));
            var results = await _storageService.GetResultsByBatchAsync(TestBatchId);
            Assert.That(results.Count, Is.EqualTo(10));
        }

        [Test]
        public async Task Snapshot_ShouldReflectCurrentBatchState()
        {
            // Arrange
            var result1 = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // Act
            await _storageService.SaveVerifyResultAsync(result1);

            // Assert
            var snapshot = _storageService.Snapshot;
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(snapshot.LastSavedSN, Is.EqualTo(TestSn1));
            Assert.That(snapshot.RecordCount, Is.EqualTo(1));
            Assert.That(snapshot.Timestamp, Is.LessThanOrEqualTo(DateTime.Now));
        }
    }
}
