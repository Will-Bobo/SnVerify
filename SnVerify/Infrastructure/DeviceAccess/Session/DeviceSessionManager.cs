/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：Session 级 Bootstrap、Shell warmup、设备数检查。</remarks>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Services.Adb;

namespace SnVerify.Infrastructure.DeviceAccess.Session
{
    /// <summary>
    /// 设备会话管理：环境级 Shell warmup（进程内一次），每检测批次执行 Bootstrap，设备数量检查。
    /// </summary>
    public class DeviceSessionManager
    {
        private readonly string _adbPath;
        private readonly IProcessRunner _processRunner;
        private readonly object _lock = new object();
        // SessionReady is environment-level barrier. It is not bound to device identity. Do not introduce device detection logic here.
#pragma warning disable CS0414 // 保留语义：环境会话已初始化，仅与 _warmupDone 同步，不用于控制流
        private bool _sessionReady;
#pragma warning restore CS0414
        /// <summary>Shell 通道预热是否已完成（进程生命周期内只执行一次）。</summary>
        private bool _warmupDone;
        private const int WarmupTimeoutMs = 2000;
        private const int BootstrapCommandTimeoutMs = 5000;
        private const int DevicesCommandTimeoutMs = 3000;
        /// <summary>Retry 策略下每条命令最多执行次数（含首次）。</summary>
        private const int BootstrapRetryMaxAttempts = 3;

        public DeviceSessionManager(string adbPath, IProcessRunner processRunner = null)
        {
            _adbPath = adbPath ?? throw new ArgumentNullException(nameof(adbPath));
            _processRunner = processRunner ?? new ProcessRunner();
        }

        /// <summary>
        /// 环境级 Warmup 仅执行一次；BootstrapCommandSpecs 每检测批次执行（不因 SessionReady 跳过）。
        /// </summary>
        public async Task EnsureSessionReadyAsync(DeviceAdbConfig config, CancellationToken cancellationToken = default)
        {
            bool needWarmup;
            lock (_lock)
            {
                needWarmup = !_warmupDone;
            }

            if (needWarmup)
            {
                await EnsureShellWarmedUpAsync(cancellationToken).ConfigureAwait(false);
                lock (_lock)
                {
                    _warmupDone = true;
                    _sessionReady = true;
                }
            }

            if (config?.BootstrapCommandSpecs != null && config.BootstrapCommandSpecs.Count > 0)
            {
                var totalTimeoutMs = BootstrapCommandTimeoutMs * config.BootstrapCommandSpecs.Count * BootstrapRetryMaxAttempts;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(totalTimeoutMs);
                    foreach (var spec in config.BootstrapCommandSpecs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await RunBootstrapSpecAsync(spec, cts.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// 执行单条 Bootstrap 命令规格：按超时策略与可接受退出码判断通过或抛异常。
        /// </summary>
        private async Task RunBootstrapSpecAsync(BootstrapCommandSpec spec, CancellationToken cancellationToken)
        {
            var cmd = (spec?.Command ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(cmd))
                return;

            var acceptableExitCodes = spec.AcceptableExitCodes;
            var timeoutBehavior = spec.TimeoutBehavior;

            for (var attempt = 1; attempt <= BootstrapRetryMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _processRunner.RunAsync(
                    _adbPath,
                    cmd,
                    BootstrapCommandTimeoutMs,
                    cancellationToken).ConfigureAwait(false);

                if (result.IsTimeout)
                {
                    switch (timeoutBehavior)
                    {
                        case BootstrapTimeoutBehavior.Fail:
                            throw new InvalidOperationException($"Bootstrap 命令超时: {cmd}");
                        case BootstrapTimeoutBehavior.Ignore:
                            return;
                        case BootstrapTimeoutBehavior.Retry:
                            if (attempt >= BootstrapRetryMaxAttempts)
                                throw new InvalidOperationException($"Bootstrap 命令超时（已重试）: {cmd}");
                            continue;
                        default:
                            throw new InvalidOperationException($"Bootstrap 命令超时: {cmd}");
                    }
                }

                if (result.IsSuccess)
                    return;
                if (acceptableExitCodes != null && acceptableExitCodes.Length > 0)
                {
                    for (var i = 0; i < acceptableExitCodes.Length; i++)
                    {
                        if (acceptableExitCodes[i] == result.ExitCode)
                            return;
                    }
                }
                throw new InvalidOperationException($"Bootstrap 命令失败: {cmd}, ExitCode={result.ExitCode}, {result.ErrorMessage}");
            }
        }

        /// <summary>
        /// Shell 通道预热（环境级，进程生命周期内由调用方保证只执行一次）。
        /// </summary>
        private async Task EnsureShellWarmedUpAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _processRunner.RunAsync(
                    _adbPath,
                    "shell exit",
                    WarmupTimeoutMs,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // warmup 失败不阻断，后续命令可能仍可用
            }
        }

        /// <summary>
        /// 检查是否存在多个设备；若存在则返回 true 并输出 deviceIds。
        /// </summary>
        public bool CheckMultipleDevices(out List<string> deviceIds)
        {
            deviceIds = new List<string>();
            try
            {
                var result = _processRunner.RunAsync(
                    _adbPath,
                    "devices",
                    DevicesCommandTimeoutMs,
                    CancellationToken.None).GetAwaiter().GetResult();
                if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
                    return false;
                var lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1 && !string.Equals(parts[0], "List of devices attached", StringComparison.OrdinalIgnoreCase))
                        deviceIds.Add(parts[0]);
                }
                return deviceIds.Count > 1;
            }
            catch
            {
                return false;
            }
        }
    }
}
