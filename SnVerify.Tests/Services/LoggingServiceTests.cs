/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// LoggingService 单元测试
    /// </summary>
    [TestFixture]
    public class LoggingServiceTests
    {
        private ILoggingService _loggingService;
        private string _testLogDirectory;
        private const string TestBatchId = "BATCH001";

        [SetUp]
        public void SetUp()
        {
            _testLogDirectory = Path.Combine(Path.GetTempPath(), $"SnVerify_Logs_{Guid.NewGuid()}");
            _loggingService = new LoggingService(_testLogDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            _loggingService?.Dispose();
            if (Directory.Exists(_testLogDirectory))
            {
                Directory.Delete(_testLogDirectory, true);
            }
        }

        /// <summary>
        /// 安全地读取日志文件内容（即使文件正在被写入）
        /// </summary>
        private string ReadLogFileSafely(string filePath)
        {
            // 使用 FileStream 以只读模式打开，并允许其他进程写入
            using (var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new StreamReader(fileStream))
            {
                return reader.ReadToEnd();
            }
        }

        [Test]
        public void Snapshot_ShouldReturnInitialIdleState()
        {
            // Assert
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Null);
            Assert.That(_loggingService.Snapshot.BatchId, Is.Null);
            Assert.That(_loggingService.Snapshot.LastMessage, Is.Null);
            Assert.That(_loggingService.Snapshot.ErrorMessage, Is.Null);
        }

        [Test]
        public void StartBatch_ShouldCreateLogFile()
        {
            // Act
            _loggingService.StartBatch(TestBatchId);

            // Assert
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Not.Null);
            Assert.That(File.Exists(_loggingService.Snapshot.CurrentLogFile), Is.True);
        }

        [Test]
        public void StartBatch_ShouldThrowException_WhenBatchIdIsEmpty()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _loggingService.StartBatch(null));
            Assert.Throws<ArgumentException>(() => _loggingService.StartBatch(""));
            Assert.Throws<ArgumentException>(() => _loggingService.StartBatch("   "));
        }

        [Test]
        public void LogInfo_ShouldWriteToFile()
        {
            // Arrange
            _loggingService.StartBatch(TestBatchId);
            var message = "Test info message";

            // Act
            _loggingService.LogInfo(message);

            // Assert
            var logContent = ReadLogFileSafely(_loggingService.Snapshot.CurrentLogFile);
            Assert.That(logContent, Contains.Substring(message));
            Assert.That(_loggingService.Snapshot.LastMessage, Is.EqualTo(message));
            Assert.That(_loggingService.Snapshot.LogLevel, Is.EqualTo("INFO"));
        }

        [Test]
        public void LogWarning_ShouldWriteToFile()
        {
            // Arrange
            _loggingService.StartBatch(TestBatchId);
            var message = "Test warning message";

            // Act
            _loggingService.LogWarning(message);

            // Assert
            var logContent = ReadLogFileSafely(_loggingService.Snapshot.CurrentLogFile);
            Assert.That(logContent, Contains.Substring(message));
            Assert.That(_loggingService.Snapshot.LogLevel, Is.EqualTo("WARN"));
        }

        [Test]
        public void LogError_ShouldWriteToFile()
        {
            // Arrange
            _loggingService.StartBatch(TestBatchId);
            var message = "Test error message";

            // Act
            _loggingService.LogError(message);

            // Assert
            var logContent = ReadLogFileSafely(_loggingService.Snapshot.CurrentLogFile);
            Assert.That(logContent, Contains.Substring(message));
            Assert.That(_loggingService.Snapshot.LogLevel, Is.EqualTo("ERROR"));
        }

        [Test]
        public void LogError_ShouldIncludeException()
        {
            // Arrange
            _loggingService.StartBatch(TestBatchId);
            var message = "Test error";
            var exception = new InvalidOperationException("Test exception");

            // Act
            _loggingService.LogError(message, exception);

            // Assert
            var logContent = ReadLogFileSafely(_loggingService.Snapshot.CurrentLogFile);
            Assert.That(logContent, Contains.Substring(message));
            Assert.That(logContent, Contains.Substring("Test exception"));
        }

        [Test]
        public void EndBatch_ShouldCloseLogFile()
        {
            // Arrange
            _loggingService.StartBatch(TestBatchId);
            var logFilePath = _loggingService.Snapshot.CurrentLogFile;

            // Act
            _loggingService.EndBatch();

            // Assert
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Null);
            Assert.That(_loggingService.Snapshot.BatchId, Is.Null);
        }

        [Test]
        public void LogInfo_ShouldCreateDefaultBatch_WhenNoBatchStarted()
        {
            // Act
            _loggingService.LogInfo("Test message");

            // Assert
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Not.Null);
            Assert.That(_loggingService.Snapshot.BatchId, Is.Not.Null);
            Assert.That(_loggingService.Snapshot.BatchId, Does.StartWith("default_"));
        }

        [Test]
        public void CleanupOldLogs_ShouldRemoveOldFiles()
        {
            // Arrange
            var maxFiles = 5;
            var loggingService = new LoggingService(_testLogDirectory, maxLogFilesToKeep: maxFiles);

            // 创建多个批次日志
            for (int i = 0; i < maxFiles + 3; i++)
            {
                loggingService.StartBatch($"BATCH{i:D3}");
                loggingService.LogInfo($"Message {i}");
                loggingService.EndBatch();
            }

            // Act
            loggingService.CleanupOldLogs();

            // Assert
            var logFiles = Directory.GetFiles(_testLogDirectory, "log_*.*");
            Assert.That(logFiles.Length, Is.LessThanOrEqualTo(maxFiles * 2)); // 考虑压缩文件

            loggingService.Dispose();
        }

        [Test]
        public void StartBatch_ShouldClosePreviousBatch()
        {
            // Arrange
            _loggingService.StartBatch("BATCH001");
            var firstLogFile = _loggingService.Snapshot.CurrentLogFile;

            // Act
            _loggingService.StartBatch("BATCH002");

            // Assert
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo("BATCH002"));
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Not.EqualTo(firstLogFile));
        }

        [Test]
        public void Snapshot_ShouldUpdateAfterLogging()
        {
            // Arrange
            _loggingService.StartBatch(TestBatchId);

            // Act
            _loggingService.LogInfo("Test message");

            // Assert
            Assert.That(_loggingService.Snapshot.LastMessage, Is.EqualTo("Test message"));
            Assert.That(_loggingService.Snapshot.LogLevel, Is.EqualTo("INFO"));
            Assert.That(_loggingService.Snapshot.Timestamp, Is.LessThanOrEqualTo(DateTime.Now));
        }
    }
}
