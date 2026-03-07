/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：执行单条 ADB 命令。</remarks>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Services.Adb; // IProcessRunner, ProcessRunner

namespace SnVerify.Infrastructure.DeviceAccess.Command
{
    /// <summary>
    /// 执行单条 ADB 命令并返回标准输出。
    /// </summary>
    public class DeviceCommandExecutor
    {
        private readonly string _adbPath;
        private readonly IProcessRunner _processRunner;
        private const int DefaultTimeoutMs = 5000;

        public DeviceCommandExecutor(string adbPath, IProcessRunner processRunner = null)
        {
            _adbPath = adbPath ?? throw new ArgumentNullException(nameof(adbPath));
            _processRunner = processRunner ?? new ProcessRunner();
        }

        /// <summary>
        /// 执行命令，返回标准输出；失败时返回 null 或抛异常。
        /// </summary>
        public async Task<string> ExecuteAsync(string arguments, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
        {
            var result = await _processRunner.RunAsync(_adbPath, arguments?.Trim() ?? string.Empty, timeoutMs, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? result.StandardOutput : null;
        }
    }
}
