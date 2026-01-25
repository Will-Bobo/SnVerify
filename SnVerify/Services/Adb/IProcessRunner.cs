/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SnVerify.Services.Adb
{
    /// <summary>
    /// 进程执行器接口，用于抽象进程执行逻辑，便于测试
    /// </summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// 执行进程命令
        /// </summary>
        /// <param name="fileName">可执行文件路径</param>
        /// <param name="arguments">命令行参数</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>进程执行结果</returns>
        Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            int timeout,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 进程执行结果
    /// </summary>
    public class ProcessExecutionResult
    {
        /// <summary>
        /// 是否成功执行
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 标准输出内容
        /// </summary>
        public string StandardOutput { get; }

        /// <summary>
        /// 标准错误输出内容
        /// </summary>
        public string StandardError { get; }

        /// <summary>
        /// 退出代码
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// 是否超时
        /// </summary>
        public bool IsTimeout { get; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static ProcessExecutionResult Success(string standardOutput, int exitCode = 0)
        {
            return new ProcessExecutionResult(true, standardOutput, string.Empty, exitCode, false, null);
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static ProcessExecutionResult Failure(string errorMessage, string standardError = null, int exitCode = -1)
        {
            return new ProcessExecutionResult(false, null, standardError ?? string.Empty, exitCode, false, errorMessage);
        }

        /// <summary>
        /// 创建超时结果
        /// </summary>
        public static ProcessExecutionResult Timeout()
        {
            return new ProcessExecutionResult(false, null, string.Empty, -1, true, "Process execution timeout");
        }

        /// <summary>
        /// 私有构造函数
        /// </summary>
        private ProcessExecutionResult(
            bool isSuccess,
            string standardOutput,
            string standardError,
            int exitCode,
            bool isTimeout,
            string errorMessage)
        {
            IsSuccess = isSuccess;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            ExitCode = exitCode;
            IsTimeout = isTimeout;
            ErrorMessage = errorMessage ?? string.Empty;
        }
    }
}
