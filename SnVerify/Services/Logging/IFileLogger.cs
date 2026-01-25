/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

namespace SnVerify.Services.Logging
{
    /// <summary>
    /// 文件日志记录器接口
    /// </summary>
    public interface IFileLogger
    {
        /// <summary>
        /// 记录信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        void LogInfo(string message);

        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        void LogWarning(string message);

        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象（可选）</param>
        void LogError(string message, System.Exception exception = null);
    }
}
