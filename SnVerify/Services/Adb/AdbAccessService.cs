/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Adb
{
    /// <summary>
    /// ADB 访问服务实现，负责通过 ADB 命令读取设备 SN（Phase2 扩展）
    /// </summary>
    public class AdbAccessService : IAdbAccessService
    {
        private readonly string _adbPath;
        private readonly IProcessRunner _processRunner;
        private readonly object _snapshotLock = new object();
        private AdbSnapshot _snapshot;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;
        private const int TotalTimeoutMs = 10000;
        private const int DevicesCommandTimeoutMs = 3000;

        /// <summary>
        /// 当前 ADB 访问状态快照
        /// </summary>
        public AdbSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? AdbSnapshot.Idle();
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
        /// 初始化 ADB 访问服务
        /// </summary>
        /// <param name="adbPath">adb.exe 文件路径</param>
        /// <param name="processRunner">进程执行器（可选，默认使用 ProcessRunner）</param>
        public AdbAccessService(string adbPath, IProcessRunner processRunner = null)
        {
            _adbPath = adbPath ?? throw new ArgumentNullException(nameof(adbPath));
            _processRunner = processRunner ?? new ProcessRunner();
            _snapshot = AdbSnapshot.Idle();
        }

        /// <summary>
        /// 读取设备 SN
        /// </summary>
        public async Task<AdbSnReadResult> ReadDeviceSnAsync(CancellationToken cancellationToken = default)
        {
            // 使用 CancellationTokenSource 实现总超时控制
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TotalTimeoutMs);

                try
                {
                    // 重试执行完整流程
                    for (int attempt = 1; attempt <= MaxRetries; attempt++)
                    {
                        try
                        {
                            // Step 1: 执行 ylzero 命令
                            var ylzeroResult = await _processRunner.RunAsync(
                                _adbPath,
                                "shell ylzero",
                                TotalTimeoutMs / MaxRetries,
                                timeoutCts.Token);

                            if (!ylzeroResult.IsSuccess || ylzeroResult.IsTimeout)
                            {
                                if (ylzeroResult.IsTimeout)
                                {
                                    return AdbSnReadResult.Failure("ylzero command timeout", true);
                                }

                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                return AdbSnReadResult.Failure($"ylzero command failed: {ylzeroResult.ErrorMessage}");
                            }

                            // Step 2: 执行 SN 读取命令
                            var snResult = await _processRunner.RunAsync(
                                _adbPath,
                                "shell getprop sys.skyroam.osi.sn",
                                TotalTimeoutMs / MaxRetries,
                                timeoutCts.Token);

                            if (snResult.IsTimeout)
                            {
                                return AdbSnReadResult.Failure("SN read command timeout", true);
                            }

                            if (!snResult.IsSuccess)
                            {
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                return AdbSnReadResult.Failure($"SN read command failed: {snResult.ErrorMessage}");
                            }

                            // 验证 SN 有效性
                            var sn = snResult.StandardOutput?.Trim();
                            if (string.IsNullOrWhiteSpace(sn))
                            {
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                return AdbSnReadResult.Failure("SN is empty or whitespace");
                            }

                            return AdbSnReadResult.Success(sn);
                        }
                        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                        {
                            return AdbSnReadResult.Failure("Total operation timeout", true);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (attempt < MaxRetries)
                        {
                            await Task.Delay(RetryDelayMs, timeoutCts.Token);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            return AdbSnReadResult.Failure($"Unexpected error: {ex.Message}");
                        }
                    }

                    return AdbSnReadResult.Failure("Max retries exceeded");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// 获取指定设备的 SN（Phase2 新增）
        /// </summary>
        public async Task<string> GetDeviceSNAsync(string deviceId = null, string batchId = null, CancellationToken cancellationToken = default)
        {
            try
            {
                // 更新状态为处理中
                Snapshot = AdbSnapshot.Processing(batchId);

                // 如果没有指定设备 ID，先获取设备列表
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    var devices = await GetDeviceListAsync(cancellationToken);
                    if (devices == null || devices.Count == 0)
                    {
                        Snapshot = AdbSnapshot.Error("No devices connected", batchId);
                        return null;
                    }
                    if (devices.Count > 1)
                    {
                        Snapshot = AdbSnapshot.MultipleDevicesWarning(devices, batchId);
                        return null;
                    }
                    deviceId = devices[0];
                }

                // 使用 CancellationTokenSource 实现总超时控制
                CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TotalTimeoutMs);

                try
                {
                    // 重试执行完整流程
                    for (int attempt = 1; attempt <= MaxRetries; attempt++)
                    {
                        try
                        {
                            // Step 1: 执行 ylzero 命令（带设备 ID）
                            var ylzeroResult = await _processRunner.RunAsync(
                                _adbPath,
                                $"shell -s {deviceId} ylzero",
                                TotalTimeoutMs / MaxRetries,
                                timeoutCts.Token);

                            if (!ylzeroResult.IsSuccess || ylzeroResult.IsTimeout)
                            {
                                if (ylzeroResult.IsTimeout)
                                {
                                    Snapshot = AdbSnapshot.Error("ylzero command timeout", batchId);
                                    return null;
                                }

                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                Snapshot = AdbSnapshot.Error($"ylzero command failed: {ylzeroResult.ErrorMessage}", batchId);
                                return null;
                            }

                            // Step 2: 执行 SN 读取命令（带设备 ID）
                            var snResult = await _processRunner.RunAsync(
                                _adbPath,
                                $"shell -s {deviceId} getprop sys.skyroam.osi.sn",
                                TotalTimeoutMs / MaxRetries,
                                timeoutCts.Token);

                            if (snResult.IsTimeout)
                            {
                                Snapshot = AdbSnapshot.Error("SN read command timeout", batchId);
                                return null;
                            }

                            if (!snResult.IsSuccess)
                            {
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                Snapshot = AdbSnapshot.Error($"SN read command failed: {snResult.ErrorMessage}", batchId);
                                return null;
                            }

                            // 验证 SN 有效性
                            var sn = snResult.StandardOutput?.Trim();
                            if (string.IsNullOrWhiteSpace(sn))
                            {
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                Snapshot = AdbSnapshot.Error("SN is empty or whitespace", batchId);
                                return null;
                            }

                            // 成功获取 SN
                            Snapshot = AdbSnapshot.Success(sn, batchId);
                            return sn;
                        }
                        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                        {
                            Snapshot = AdbSnapshot.Error("Total operation timeout", batchId);
                            return null;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (attempt < MaxRetries)
                        {
                            await Task.Delay(RetryDelayMs, timeoutCts.Token);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Snapshot = AdbSnapshot.Error($"Unexpected error: {ex.Message}", batchId);
                            return null;
                        }
                    }

                    Snapshot = AdbSnapshot.Error("Max retries exceeded", batchId);
                    return null;
                }
                finally
                {
                    timeoutCts?.Dispose();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Snapshot = AdbSnapshot.Error("Operation cancelled", batchId);
                throw;
            }
            catch (Exception ex)
            {
                Snapshot = AdbSnapshot.Error($"Unexpected error: {ex.Message}", batchId);
                return null;
            }
        }

        /// <summary>
        /// 检查是否存在多个设备（Phase2 新增）
        /// </summary>
        public bool CheckMultipleDevices(out List<string> deviceIds)
        {
            deviceIds = new List<string>();

            try
            {
                // 使用同步方式调用，但内部仍然是异步的
                var devices = GetDeviceListAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (devices != null && devices.Count > 0)
                {
                    deviceIds = devices;
                    if (devices.Count > 1)
                    {
                        Snapshot = AdbSnapshot.MultipleDevicesWarning(devices, Snapshot?.BatchId);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Snapshot = AdbSnapshot.Error($"Failed to check devices: {ex.Message}", Snapshot?.BatchId);
                return false;
            }
        }

        /// <summary>
        /// 获取设备列表
        /// </summary>
        private async Task<List<string>> GetDeviceListAsync(CancellationToken cancellationToken = default)
        {
            var result = await _processRunner.RunAsync(
                _adbPath,
                "devices",
                DevicesCommandTimeoutMs,
                cancellationToken);

            if (!result.IsSuccess)
            {
                return null;
            }

            var devices = new List<string>();
            var lines = result.StandardOutput?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines == null)
            {
                return devices;
            }

            // 解析 adb devices 输出格式：
            // List of devices attached
            // device001    device
            // device002    device
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("List of devices"))
                {
                    continue;
                }

                // 匹配设备 ID（格式：deviceId    device 或 deviceId    unauthorized 等）
                var match = Regex.Match(trimmedLine, @"^(\S+)\s+\S+");
                if (match.Success)
                {
                    var deviceId = match.Groups[1].Value;
                    // 只添加状态为 "device" 的设备
                    if (trimmedLine.Contains("\tdevice") || trimmedLine.EndsWith("device"))
                    {
                        devices.Add(deviceId);
                    }
                }
            }

            return devices;
        }
    }
}
