/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Models;
using SnVerify.Services.Adb;
using SnVerify.Services.Batch;
using SnVerify.Services.Coordination;
using SnVerify.Services.Input;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// 集成测试：测试多个服务协同工作的完整流程
    /// </summary>
    [TestFixture]
    public class IntegrationTests
    {
        private IStorageService _storageService;
        private IBatchManager _batchManager;
        private ILoggingService _loggingService;
        private IScanInputService _scanInputService;
        private string _testDbPath;
        private string _testLogDirectory;
        private string _testExportDirectory;

        [SetUp]
        public async Task SetUp()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Integration_{Guid.NewGuid()}.db");
            _testLogDirectory = Path.Combine(Path.GetTempPath(), $"SnVerify_Integration_Logs_{Guid.NewGuid()}");
            _testExportDirectory = Path.Combine(Path.GetTempPath(), $"SnVerify_Integration_Export_{Guid.NewGuid()}");
            
            Directory.CreateDirectory(_testLogDirectory);
            Directory.CreateDirectory(_testExportDirectory);

            _storageService = new StorageService(_testDbPath);
            await _storageService.InitializeAsync();

            _batchManager = new BatchManager(_storageService);
            _loggingService = new LoggingService(_testLogDirectory);
            _scanInputService = new ScanInputService();
        }

        [TearDown]
        public void TearDown()
        {
            _scanInputService?.Reset();
            _loggingService?.Dispose();
            _storageService?.Dispose();

            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
            if (Directory.Exists(_testLogDirectory))
            {
                Directory.Delete(_testLogDirectory, true);
            }
            if (Directory.Exists(_testExportDirectory))
            {
                Directory.Delete(_testExportDirectory, true);
            }
        }

        [Test]
        public async Task CompleteBatchFlow_ShouldWorkEndToEnd()
        {
            // Arrange
            const string testBatchId = "INTEGRATION_BATCH_001";
            const string testSn1 = "SN001";
            const string testSn2 = "SN002";

            // Act - Step 1: 创建批次
            var batch = _batchManager.CreateBatch(testBatchId);
            Assert.That(batch.BatchId, Is.EqualTo(testBatchId));

            // Act - Step 2: 开始批次
            _batchManager.StartBatch(testBatchId);
            Assert.That(_batchManager.Snapshot.IsActive, Is.True);
            Assert.That(_batchManager.Snapshot.BatchId, Is.EqualTo(testBatchId));

            // Act - Step 3: 开始批次日志
            _loggingService.StartBatch(testBatchId);
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo(testBatchId));

            // Act - Step 4: 保存校验结果
            var result1 = new SnVerifyResult
            {
                BatchId = testBatchId,
                SN = testSn1,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(result1);

            var result2 = new SnVerifyResult
            {
                BatchId = testBatchId,
                SN = testSn2,
                Result = "FAIL",
                FailReason = "SN mismatch",
                VerifyTime = DateTime.Now
            };
            await _storageService.SaveVerifyResultAsync(result2);

            // Act - Step 5: 记录日志
            _loggingService.LogInfo($"SN {testSn1} 校验通过");
            _loggingService.LogWarning($"SN {testSn2} 校验失败: SN mismatch");

            // Act - Step 6: 导出批次结果
            await _storageService.ExportBatchResultAsync(testBatchId, _testExportDirectory);

            // Act - Step 7: 结束批次日志
            _loggingService.EndBatch();

            // Act - Step 8: 结束批次
            _batchManager.EndBatch();
            Assert.That(_batchManager.Snapshot.IsActive, Is.False);

            // Assert - 验证数据库记录
            var results = await _storageService.GetResultsByBatchAsync(testBatchId);
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.Any(r => r.SN == testSn1 && r.Result == "PASS"), Is.True);
            Assert.That(results.Any(r => r.SN == testSn2 && r.Result == "FAIL"), Is.True);

            // Assert - 验证导出文件存在
            var exportFilePath = Path.Combine(_testExportDirectory, $"{testBatchId}.xlsx");
            Assert.That(File.Exists(exportFilePath), Is.True);

            // Assert - 验证日志文件存在（EndBatch 后会压缩成 .zip 文件）
            var logFiles = Directory.GetFiles(_testLogDirectory, $"log_{testBatchId}_*")
                .Where(f => f.EndsWith(".txt") || f.EndsWith(".zip"))
                .ToArray();
            Assert.That(logFiles.Length, Is.GreaterThan(0), 
                $"应该找到日志文件（.txt 或 .zip），但实际找到的文件：{string.Join(", ", Directory.GetFiles(_testLogDirectory, "log_*"))}");
        }

        [Test]
        public async Task ScanInputService_ShouldIntegrateWithStorage()
        {
            // Arrange
            const string testBatchId = "INTEGRATION_BATCH_002";
            var batch = _batchManager.CreateBatch(testBatchId);
            _batchManager.StartBatch(testBatchId);
            _loggingService.StartBatch(testBatchId);

            // 创建 ProcessCoordinator（需要 Mock AdbAccessService）
            var mockAdbService = new Mock<IAdbAccessService>();
            mockAdbService
                .Setup(x => x.ReadDeviceSnAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(AdbSnReadResult.Success("MOCK_SN_001"));

            var processCoordinator = new ProcessCoordinator(
                testBatchId,
                _storageService,
                mockAdbService.Object,
                _loggingService);

            // 创建 ScanInputService 并关联 ProcessCoordinator
            var scanInputService = new ScanInputService(processCoordinator, testBatchId);

            // Act - 直接调用 OnScanInputAsync 模拟扫码输入
            const string scannedSn = "MOCK_SN_001";
            await scanInputService.OnScanInputAsync(scannedSn);

            // 等待处理完成（异步）
            await Task.Delay(200);

            // Assert - 验证扫码输入服务状态
            Assert.That(scanInputService.Snapshot.LastScanSN, Is.Not.Null);
            Assert.That(scanInputService.Snapshot.LastScanSN, Is.EqualTo(scannedSn));

            // 验证 ProcessCoordinator 已处理
            Assert.That(processCoordinator.Snapshot.LastResult, Is.EqualTo("PASS"));
        }

        [Test]
        public async Task ExportBatchResult_ShouldFormatVerifyTimeCorrectly()
        {
            // Arrange
            const string testBatchId = "INTEGRATION_BATCH_003";
            var batch = _batchManager.CreateBatch(testBatchId);
            _batchManager.StartBatch(testBatchId);

            var verifyTime = new DateTime(2026, 1, 26, 13, 45, 30);
            var expectedFormat = "2026年1月26日 13:45:30";

            var result = new SnVerifyResult
            {
                BatchId = testBatchId,
                SN = "SN001",
                Result = "PASS",
                VerifyTime = verifyTime
            };
            await _storageService.SaveVerifyResultAsync(result);

            // Act
            await _storageService.ExportBatchResultAsync(testBatchId, _testExportDirectory);

            // Assert
            var exportFilePath = Path.Combine(_testExportDirectory, $"{testBatchId}.xlsx");
            Assert.That(File.Exists(exportFilePath), Is.True);

            using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(exportFilePath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                Assert.That(passSheet, Is.Not.Null);

                var verifyTimeCell = passSheet.Cells[2, 5]; // 第 2 行，第 5 列（VerifyTime）
                Assert.That(verifyTimeCell.Value, Is.Not.Null);
                Assert.That(verifyTimeCell.Value, Is.InstanceOf<string>());
                Assert.That(verifyTimeCell.Value.ToString(), Is.EqualTo(expectedFormat));
            }
        }

        [Test]
        public void BatchManager_ShouldIntegrateWithLoggingService()
        {
            // Arrange
            const string testBatchId = "INTEGRATION_BATCH_004";

            // Act
            var batch = _batchManager.CreateBatch(testBatchId);
            _batchManager.StartBatch(testBatchId);
            _loggingService.StartBatch(testBatchId);
            _loggingService.LogInfo("批次开始");
            _batchManager.EndBatch();
            _loggingService.EndBatch();

            // Assert
            Assert.That(_batchManager.Snapshot.IsActive, Is.False);
            Assert.That(_loggingService.Snapshot.BatchId, Is.Null);
            
            // 验证日志文件已创建（EndBatch 后会压缩成 .zip 文件）
            var logFiles = Directory.GetFiles(_testLogDirectory, $"log_{testBatchId}_*")
                .Where(f => f.EndsWith(".txt") || f.EndsWith(".zip"))
                .ToArray();
            Assert.That(logFiles.Length, Is.GreaterThan(0), 
                $"应该找到日志文件（.txt 或 .zip），但实际找到的文件：{string.Join(", ", Directory.GetFiles(_testLogDirectory, "log_*"))}");
        }
    }
}
