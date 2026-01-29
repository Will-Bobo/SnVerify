/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using SnVerify.Domain.State;

namespace SnVerify.Services.Logging
{
    /// <summary>
    /// 日志服务接口，支持基于 Session 的日志文件与轮换管理（Phase2.5）。
    /// </summary>
    public interface ILoggingService : IFileLogger, IDisposable
    {
        /// <summary>
        /// 当前日志服务状态快照
        /// </summary>
        LoggingSnapshot Snapshot { get; }

        /// <summary>
        /// 开始新的 Session 日志（推荐使用的入口）。
        /// </summary>
        /// <param name="sessionName">唯一 SessionName（如 OrderId_yyyyMMdd_HHmmss）</param>
        void StartSession(string sessionName);

        /// <summary>
        /// 获取指定 SessionName 对应的日志文件绝对路径；不存在时返回 null。
        /// </summary>
        /// <param name="sessionName">SessionName（如 OrderId_yyyyMMdd_HHmmss）</param>
        /// <returns>日志文件完整路径或 null</returns>
        string GetLogFilePath(string sessionName);

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
