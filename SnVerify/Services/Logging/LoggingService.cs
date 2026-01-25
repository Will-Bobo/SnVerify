/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using SnVerify.Domain.State;

namespace SnVerify.Services.Logging
{
    /// <summary>
    /// 日志服务实现，支持批次轮换和日志管理（Phase2 扩展）
    /// </summary>
    public class LoggingService : ILoggingService, IDisposable
    {
        private readonly string _logDirectory;
        private readonly int _maxLogFileSizeBytes;
        private readonly int _maxLogFilesToKeep;
        private readonly object _lockObject = new object();
        private readonly object _snapshotLock = new object();
        private LoggingSnapshot _snapshot;
        private StreamWriter _currentWriter;
        private string _currentBatchId;
        private string _currentLogFilePath;

        /// <summary>
        /// 当前日志服务状态快照
        /// </summary>
        public LoggingSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? LoggingSnapshot.Idle();
                }
            }
            private set
            {
                lock (_snapshotLock)
                {
                    _snapshot = value;
                }
            }
        }

        /// <summary>
        /// 初始化日志服务
        /// </summary>
        /// <param name="logDirectory">日志目录路径</param>
        /// <param name="maxLogFileSizeBytes">单个日志文件最大大小（字节），默认 10MB</param>
        /// <param name="maxLogFilesToKeep">保留的最大日志文件数，默认 30</param>
        public LoggingService(
            string logDirectory,
            int maxLogFileSizeBytes = 10 * 1024 * 1024,
            int maxLogFilesToKeep = 30)
        {
            _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
            _maxLogFileSizeBytes = maxLogFileSizeBytes;
            _maxLogFilesToKeep = maxLogFilesToKeep;
            _snapshot = LoggingSnapshot.Idle();

            // 确保日志目录存在
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        /// <summary>
        /// 开始新批次日志（创建新的日志文件）
        /// </summary>
        public void StartBatch(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
            {
                Snapshot = LoggingSnapshot.Error("批次 ID 不能为空");
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            }

            lock (_lockObject)
            {
                try
                {
                    // 关闭当前日志文件（如果存在）
                    EndBatchInternal();

                    // 创建新日志文件
                    _currentBatchId = batchId;
                    var fileName = $"log_{batchId}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                    _currentLogFilePath = Path.Combine(_logDirectory, fileName);

                    // 使用 FileShare.ReadWrite 模式，允许其他进程读取和写入文件
                    var fileStream = new FileStream(
                        _currentLogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    
                    _currentWriter = new StreamWriter(fileStream, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };

                    Snapshot = LoggingSnapshot.Logged(_currentLogFilePath, batchId, $"批次 {batchId} 开始", "INFO");
                    LogInfo($"批次 {batchId} 开始");
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"开始批次日志失败: {ex.Message}", batchId);
                    throw;
                }
            }
        }

        /// <summary>
        /// 结束当前批次日志（可选：压缩或归档）
        /// </summary>
        public void EndBatch()
        {
            lock (_lockObject)
            {
                EndBatchInternal();
            }
        }

        /// <summary>
        /// 内部方法：结束批次日志
        /// </summary>
        private void EndBatchInternal()
        {
            if (_currentWriter != null)
            {
                try
                {
                    LogInfo($"批次 {_currentBatchId} 结束");
                    _currentWriter.Flush();
                    _currentWriter.Close();
                    _currentWriter.Dispose();
                    _currentWriter = null;

                    // 可选：压缩日志文件
                    CompressLogFile(_currentLogFilePath);

                    _currentLogFilePath = null;
                    _currentBatchId = null;

                    // 更新快照为 Idle 状态
                    Snapshot = LoggingSnapshot.Idle();
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"结束批次日志失败: {ex.Message}", _currentBatchId);
                }
            }
            else if (_currentLogFilePath != null)
            {
                // 如果没有 writer 但还有日志文件路径，也需要清理
                _currentLogFilePath = null;
                _currentBatchId = null;
                Snapshot = LoggingSnapshot.Idle();
            }
        }

        /// <summary>
        /// 压缩日志文件
        /// </summary>
        private void CompressLogFile(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
                return;

            try
            {
                var zipFilePath = logFilePath.Replace(".txt", ".zip");
                using (var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(logFilePath, Path.GetFileName(logFilePath));
                }
                File.Delete(logFilePath);
            }
            catch
            {
                // 压缩失败不影响主流程，忽略异常
            }
        }

        /// <summary>
        /// 清理旧日志文件（根据配置策略）
        /// </summary>
        public void CleanupOldLogs()
        {
            lock (_lockObject)
            {
                try
                {
                    var logFiles = Directory.GetFiles(_logDirectory, "log_*.txt")
                        .Concat(Directory.GetFiles(_logDirectory, "log_*.zip"))
                        .OrderByDescending(f => new FileInfo(f).CreationTime)
                        .ToList();

                    if (logFiles.Count > _maxLogFilesToKeep)
                    {
                        var filesToDelete = logFiles.Skip(_maxLogFilesToKeep);
                        foreach (var file in filesToDelete)
                        {
                            try
                            {
                                File.Delete(file);
                            }
                            catch
                            {
                                // 删除失败不影响主流程
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"清理旧日志失败: {ex.Message}", _currentBatchId);
                }
            }
        }

        /// <summary>
        /// 记录信息日志
        /// </summary>
        public void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        public void LogWarning(string message)
        {
            WriteLog("WARN", message);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        public void LogError(string message, Exception exception = null)
        {
            var fullMessage = exception != null
                ? $"{message}\n异常: {exception}"
                : message;
            WriteLog("ERROR", fullMessage);
        }

        /// <summary>
        /// 写入日志
        /// </summary>
        private void WriteLog(string level, string message)
        {
            lock (_lockObject)
            {
                try
                {
                    if (_currentWriter == null)
                    {
                        // 如果没有活动批次，创建一个默认日志文件
                        StartBatch($"default_{DateTime.Now:yyyyMMdd_HHmmss}");
                    }

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var logEntry = $"[{timestamp}] [{level}] {message}";
                    _currentWriter.WriteLine(logEntry);
                    _currentWriter.Flush(); // 确保立即写入磁盘，以便其他进程可以读取

                    // 检查文件大小，如果超过限制则轮换
                    if (new FileInfo(_currentLogFilePath).Length > _maxLogFileSizeBytes)
                    {
                        EndBatchInternal();
                        StartBatch(_currentBatchId ?? $"rotated_{DateTime.Now:yyyyMMdd_HHmmss}");
                    }

                    Snapshot = LoggingSnapshot.Logged(_currentLogFilePath, _currentBatchId, message, level);
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"写入日志失败: {ex.Message}", _currentBatchId);
                }
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_lockObject)
            {
                EndBatchInternal();
            }
        }
    }
}
