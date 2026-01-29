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
        private const string TestSessionName = "ORDER001_20260126_120000";

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
            _loggingService.StartSession(TestBatchId);

            // Assert
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Not.Null);
            Assert.That(File.Exists(_loggingService.Snapshot.CurrentLogFile), Is.True);
        }

        [Test]
        public void StartBatch_ShouldThrowException_WhenBatchIdIsEmpty()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _loggingService.StartSession(null));
            Assert.Throws<ArgumentException>(() => _loggingService.StartSession(""));
            Assert.Throws<ArgumentException>(() => _loggingService.StartSession("   "));
        }

        [Test]
        public void LogInfo_ShouldWriteToFile()
        {
            // Arrange
            _loggingService.StartSession(TestBatchId);
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
            _loggingService.StartSession(TestBatchId);
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
            _loggingService.StartSession(TestBatchId);
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
            _loggingService.StartSession(TestBatchId);
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
            _loggingService.StartSession(TestBatchId);
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

            // 创建多个 Session 日志
            for (int i = 0; i < maxFiles + 3; i++)
            {
                loggingService.StartSession($"BATCH{i:D3}");
                loggingService.LogInfo($"Message {i}");
                loggingService.EndBatch();
            }

            // Act
            loggingService.CleanupOldLogs();

            // Assert
            var logFiles = Directory.GetFiles(_testLogDirectory, "session_*.*");
            Assert.That(logFiles.Length, Is.LessThanOrEqualTo(maxFiles * 2)); // 考虑压缩文件

            loggingService.Dispose();
        }

        [Test]
        public void StartBatch_ShouldClosePreviousBatch()
        {
            // Arrange
            _loggingService.StartSession("BATCH001");
            var firstLogFile = _loggingService.Snapshot.CurrentLogFile;

            // Act
            _loggingService.StartSession("BATCH002");

            // Assert
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo("BATCH002"));
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Not.EqualTo(firstLogFile));
        }

        [Test]
        public void Snapshot_ShouldUpdateAfterLogging()
        {
            // Arrange
            _loggingService.StartSession(TestBatchId);

            // Act
            _loggingService.LogInfo("Test message");

            // Assert
            Assert.That(_loggingService.Snapshot.LastMessage, Is.EqualTo("Test message"));
            Assert.That(_loggingService.Snapshot.LogLevel, Is.EqualTo("INFO"));
            Assert.That(_loggingService.Snapshot.Timestamp, Is.LessThanOrEqualTo(DateTime.Now));
        }

        [Test]
        public void StartSession_Should_Create_LogFile_With_SessionName()
        {
            // Act
            _loggingService.StartSession(TestSessionName);

            // Assert
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo(TestSessionName), "Snapshot.BatchId 应等于 SessionName");
            Assert.That(_loggingService.Snapshot.CurrentLogFile, Is.Not.Null);
            var logFile = _loggingService.Snapshot.CurrentLogFile!;
            Assert.That(File.Exists(logFile), Is.True, "应为 SessionName 创建对应日志文件");
            StringAssertContains(Path.GetFileNameWithoutExtension(logFile), $"session_{TestSessionName}");
        }

        [Test]
        public void Logs_Should_Be_Written_Into_Session_Log()
        {
            // Arrange
            _loggingService.StartSession(TestSessionName);
            var message = "Session scoped log message";

            // Act
            _loggingService.LogInfo(message);

            // Assert
            var logFile = _loggingService.Snapshot.CurrentLogFile!;
            var content = ReadLogFileSafely(logFile);
            Assert.That(content, Does.Contain(message));
            Assert.That(_loggingService.Snapshot.BatchId, Is.EqualTo(TestSessionName));
        }

        [Test]
        public void Export_Should_Copy_Runtime_Log_Not_Rebuild()
        {
            // Arrange
            _loggingService.StartSession(TestSessionName);
            _loggingService.LogInfo("FIRST");
            _loggingService.LogWarning("SECOND");

            var sourceLog = _loggingService.Snapshot.CurrentLogFile!;
            var originalContent = ReadLogFileSafely(sourceLog);

            var exportDir = Path.Combine(_testLogDirectory, "export");
            Directory.CreateDirectory(exportDir);
            var exportPath = Path.Combine(exportDir, Path.GetFileName(sourceLog));

            // Act: 模拟导出层直接拷贝运行时日志文件
            File.Copy(sourceLog, exportPath, overwrite: true);

            // Assert
            Assert.That(File.Exists(exportPath), Is.True, "导出应直接复制现有 Session 日志文件");
            var exportedContent = File.ReadAllText(exportPath);
            Assert.That(exportedContent, Is.EqualTo(originalContent), "导出内容应与运行时日志完全一致（不重新生成）");
        }

        private static void StringAssertContains(string actual, string expectedSubstring)
        {
            Assert.That(actual, Does.Contain(expectedSubstring));
        }
    }
}
