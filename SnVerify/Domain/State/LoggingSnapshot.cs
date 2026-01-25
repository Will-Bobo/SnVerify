/// <author>
/// AI Assistant
/// </author>

using System;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// 日志服务状态快照（不可变对象）
    /// </summary>
    public class LoggingSnapshot
    {
        /// <summary>
        /// 当前日志文件路径
        /// </summary>
        public string CurrentLogFile { get; }

        /// <summary>
        /// 当前批次 ID
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 最后一条日志消息
        /// </summary>
        public string LastMessage { get; }

        /// <summary>
        /// 错误消息（如果有）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 日志级别（Info/Warn/Error）
        /// </summary>
        public string LogLevel { get; }

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态
        /// </summary>
        public static LoggingSnapshot Idle(string batchId = null)
        {
            return new LoggingSnapshot(null, batchId, null, null, null, DateTime.Now);
        }

        /// <summary>
        /// 创建日志记录状态
        /// </summary>
        public static LoggingSnapshot Logged(string logFile, string batchId, string message, string logLevel)
        {
            return new LoggingSnapshot(logFile, batchId, message, null, logLevel, DateTime.Now);
        }

        /// <summary>
        /// 创建错误状态
        /// </summary>
        public static LoggingSnapshot Error(string errorMessage, string batchId = null)
        {
            return new LoggingSnapshot(null, batchId, null, errorMessage, null, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private LoggingSnapshot(
            string currentLogFile,
            string batchId,
            string lastMessage,
            string errorMessage,
            string logLevel,
            DateTime timestamp)
        {
            CurrentLogFile = currentLogFile;
            BatchId = batchId;
            LastMessage = lastMessage;
            ErrorMessage = errorMessage;
            LogLevel = logLevel;
            Timestamp = timestamp;
        }
    }
}
