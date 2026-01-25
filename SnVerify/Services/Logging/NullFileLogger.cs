/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

namespace SnVerify.Services.Logging
{
    /// <summary>
    /// 空日志记录器实现（用于测试或不需要日志的场景）
    /// </summary>
    public class NullFileLogger : IFileLogger
    {
        /// <summary>
        /// 记录信息日志（空实现）
        /// </summary>
        public void LogInfo(string message)
        {
            // 空实现
        }

        /// <summary>
        /// 记录警告日志（空实现）
        /// </summary>
        public void LogWarning(string message)
        {
            // 空实现
        }

        /// <summary>
        /// 记录错误日志（空实现）
        /// </summary>
        public void LogError(string message, System.Exception exception = null)
        {
            // 空实现
        }
    }
}
