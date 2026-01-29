/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly int _maxRecentMessages;
        private readonly object _lockObject = new object();
        private readonly object _snapshotLock = new object();
        private LoggingSnapshot _snapshot;
        private StreamWriter _currentWriter;
        private string _currentBatchId;
        private string _currentLogFilePath;
        private readonly List<string> _recentMessages;

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
        /// <param name="maxRecentMessages">保留的最近日志消息数（用于 UI 显示），默认 100</param>
        public LoggingService(
            string logDirectory,
            int maxLogFileSizeBytes = 10 * 1024 * 1024,
            int maxLogFilesToKeep = 30,
            int maxRecentMessages = 100)
        {
            _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
            _maxLogFileSizeBytes = maxLogFileSizeBytes;
            _maxLogFilesToKeep = maxLogFilesToKeep;
            _maxRecentMessages = maxRecentMessages;
            _recentMessages = new List<string>();
            _snapshot = LoggingSnapshot.Idle(recentMessages: _recentMessages.AsReadOnly());

            // 确保日志目录存在
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        /// <summary>
        /// 开始新的 Session 日志：以 SessionName 作为唯一标识创建日志文件。
        /// 文件命名规则：session_{SessionName}.log（经过文件系统安全处理）。
        /// </summary>
        public void StartSession(string sessionName)
        {
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                Snapshot = LoggingSnapshot.Error("SessionName 不能为空");
                throw new ArgumentException("SessionName 不能为空", nameof(sessionName));
            }

            lock (_lockObject)
            {
                try
                {
                    // 关闭当前日志文件（如果存在）
                    EndBatchInternal();

                    // 创建新的 Session 日志文件
                    _currentBatchId = sessionName;
                    var safeSessionName = ToSafeFileName(sessionName);
                    var fileName = $"session_{safeSessionName}.log";
                    _currentLogFilePath = Path.Combine(_logDirectory, fileName);

                    var fileStream = new FileStream(
                        _currentLogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);

                    _currentWriter = new StreamWriter(fileStream, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };

                    var startMessage = $"Session {sessionName} 开始";
                    Snapshot = LoggingSnapshot.Logged(_currentLogFilePath, sessionName, startMessage, "INFO", GetRecentMessages());
                    LogInfo(startMessage);
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"开始 Session 日志失败: {ex.Message}", sessionName, GetRecentMessages());
                    throw;
                }
            }
        }

        /// <summary>
        /// 获取指定 SessionName 对应的日志文件绝对路径；不存在时返回 null。
        /// </summary>
        public string GetLogFilePath(string sessionName)
        {
            if (string.IsNullOrWhiteSpace(sessionName))
                throw new ArgumentException("SessionName 不能为空", nameof(sessionName));

            var safeSessionName = ToSafeFileName(sessionName);
            var path = Path.Combine(_logDirectory, $"session_{safeSessionName}.log");
            return File.Exists(path) ? path : null;
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

                    // 可选：压缩日志文件（不删除原始 .log，便于按 SessionName 直接导出）
                    CompressLogFile(_currentLogFilePath);

                    _currentLogFilePath = null;
                    _currentBatchId = null;

                    // 更新快照为 Idle 状态
                    Snapshot = LoggingSnapshot.Idle(recentMessages: GetRecentMessages());
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"结束批次日志失败: {ex.Message}", _currentBatchId, GetRecentMessages());
                }
            }
            else if (_currentLogFilePath != null)
            {
                // 如果没有 writer 但还有日志文件路径，也需要清理
                _currentLogFilePath = null;
                _currentBatchId = null;
                Snapshot = LoggingSnapshot.Idle(recentMessages: GetRecentMessages());
            }
        }

        /// <summary>
        /// 压缩日志文件（为磁盘占用做归档备份，但不删除原始 .log）。
        /// </summary>
        private void CompressLogFile(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
                return;

            try
            {
                var zipFilePath = Path.ChangeExtension(logFilePath, ".zip");
                using (var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(logFilePath, Path.GetFileName(logFilePath));
                }
            }
            catch
            {
                // 压缩失败不影响主流程，忽略异常
            }
        }

        /// <summary>
        /// 将 SessionName 转换为文件系统安全的名称：非法字符统一替换为下划线。
        /// </summary>
        private static string ToSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "_";

            var invalidChars = Path.GetInvalidFileNameChars();
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

        /// <summary>
        /// 清理旧日志文件（根据配置策略）
        /// </summary>
        public void CleanupOldLogs()
        {
            lock (_lockObject)
            {
                try
                {
                    var logFiles = Directory.GetFiles(_logDirectory, "session_*.log")
                        .Concat(Directory.GetFiles(_logDirectory, "session_*.zip"))
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
                    Snapshot = LoggingSnapshot.Error($"清理旧日志失败: {ex.Message}", _currentBatchId, GetRecentMessages());
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
                        // 如果没有活动 Session 日志，创建一个默认 Session 日志
                        StartSession($"default_{DateTime.Now:yyyyMMdd_HHmmss}");
                    }

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var logEntry = $"[{timestamp}] [{level}] {message}";
                    
                    // 写入文件
                    _currentWriter.WriteLine(logEntry);
                    _currentWriter.Flush(); // 确保立即写入磁盘，以便其他进程可以读取

                    // 输出到 VS Debug 窗口
                    Debug.WriteLine($"[Logging] {logEntry}");

                    // 更新内存缓冲区（最近 N 条日志）
                    _recentMessages.Add(logEntry);
                    if (_recentMessages.Count > _maxRecentMessages)
                    {
                        _recentMessages.RemoveAt(0); // 移除最旧的日志
                    }

                    // 检查文件大小，如果超过限制则轮换
                    if (new FileInfo(_currentLogFilePath).Length > _maxLogFileSizeBytes)
                    {
                        EndBatchInternal();
                        StartSession(_currentBatchId ?? $"rotated_{DateTime.Now:yyyyMMdd_HHmmss}");
                    }

                    Snapshot = LoggingSnapshot.Logged(_currentLogFilePath, _currentBatchId, message, level, GetRecentMessages());
                }
                catch (Exception ex)
                {
                    Snapshot = LoggingSnapshot.Error($"写入日志失败: {ex.Message}", _currentBatchId, GetRecentMessages());
                }
            }
        }

        /// <summary>
        /// 获取最近的日志消息列表（线程安全）
        /// </summary>
        private IReadOnlyList<string> GetRecentMessages()
        {
            lock (_lockObject)
            {
                return _recentMessages.ToList().AsReadOnly();
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
