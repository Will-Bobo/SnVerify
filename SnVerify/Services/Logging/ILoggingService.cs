/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using SnVerify.Domain.State;
using SnVerify.Services.Batch;

namespace SnVerify.Services.Logging
{
    /// <summary>
    /// 日志服务接口，支持批次轮换和日志管理（Phase2 扩展）
    /// </summary>
    public interface ILoggingService : IFileLogger, IDisposable
    {
        /// <summary>
        /// 当前日志服务状态快照
        /// </summary>
        LoggingSnapshot Snapshot { get; }

        /// <summary>
        /// 开始新批次日志（创建新的日志文件）
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        void StartBatch(string batchId);

        /// <summary>
        /// 结束当前批次日志（可选：压缩或归档）
        /// </summary>
        void EndBatch();

        /// <summary>
        /// 清理旧日志文件（根据配置策略）
        /// </summary>
        void CleanupOldLogs();
    }
}
