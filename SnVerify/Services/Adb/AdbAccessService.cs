/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool _adbShellWarmedUp = false;
        private readonly object _warmupLock = new object();
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
        /// ADB shell 通道预热，避免冷启动下首次 shell 命令 protocol fault / connection reset。
        /// 失败不阻断后续流程，仅记录日志。
        /// 仅执行一次，并发调用下保证只执行一次。
        /// </summary>
        private async Task EnsureAdbShellWarmedUpAsync(CancellationToken token)
        {
            if (_adbShellWarmedUp) return;

            lock (_warmupLock)
            {
                if (_adbShellWarmedUp) return;
                _adbShellWarmedUp = true;
            }

            try
            {
                Debug.WriteLine("[AdbAccessService] adb shell warm-up executed");
                await _processRunner.RunAsync(
                    _adbPath,
                    "shell exit",
                    2000, // 单独的 warm-up 超时（短）
                    token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdbAccessService] adb shell warm-up failed: {ex.Message}");
                // warm-up 失败不阻断后续流程
            }
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
                            // Step 0: ADB shell warm-up（冷启动下避免首次 shell 命令失败）
                            await EnsureAdbShellWarmedUpAsync(timeoutCts.Token);

                            // Step 1: 执行 ylzero 命令（可选步骤，某些机器可能报错但不影响 SN 读取）
                            // 完整命令: adb shell ylzero
                            // 注意：ylzero 失败（非超时）可以继续，但超时或异常需要返回错误
                            try
                            {
                                var ylzeroResult = await _processRunner.RunAsync(
                                    _adbPath,
                                    "shell ylzero",
                                    TotalTimeoutMs / MaxRetries,
                                    timeoutCts.Token);

                                // 记录 ADB 口令命令的执行结果（仅用于调试）
                                if (ylzeroResult.IsTimeout)
                                {
                                    // ADB 口令超时，返回错误
                                    if (attempt < MaxRetries)
                                    {
                                        await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                        continue;
                                    }
                                    return AdbSnReadResult.Failure("ADB 口令超时", true);
                                }
                                
                                if (!ylzeroResult.IsSuccess)
                                {
                                    // ExitCode == 127：命令不存在；debug 版机器执行此命令会出现该状态，产线为正式版不会出现。此时可继续 SN 读取。
                                    // ExitCode == 255：user 版本设备的特殊状态，可以继续 SN 读取。
                                    // ExitCode != 127 && ExitCode != 255：其他错误，需直接返回失败。
                                    if (ylzeroResult.ExitCode != 127 && ylzeroResult.ExitCode != 255)
                                    {
                                        // 非 127/255 错误，返回错误
                                        if (attempt < MaxRetries)
                                        {
                                            await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                            continue;
                                        }
                                        
                                        // 构建错误消息，包含 ErrorMessage 和 StandardError
                                        var errorMessage = $"ADB 口令失败: {ylzeroResult.ErrorMessage}";
                                        if (!string.IsNullOrEmpty(ylzeroResult.StandardError))
                                        {
                                            errorMessage += $"\nError: {ylzeroResult.StandardError}";
                                        }
                                        return AdbSnReadResult.Failure(errorMessage);
                                    }
                                    
                                    // ExitCode == 127 或 255，记录但继续执行 SN 读取命令
                                    var exitCodeDesc = ylzeroResult.ExitCode == 127 ? "命令不存在" : "user版本设备特殊状态";
                                    Debug.WriteLine($"[AdbAccessService] ADB 口令 {exitCodeDesc}（ExitCode={ylzeroResult.ExitCode}，不影响 SN 读取）: {ylzeroResult.ErrorMessage}");
                                    if (!string.IsNullOrEmpty(ylzeroResult.StandardError))
                                    {
                                        Debug.WriteLine($"[AdbAccessService] ADB 口令标准错误输出: {ylzeroResult.StandardError}");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"[AdbAccessService] ADB 口令执行成功");
                                }
                            }
                            catch (Exception ylzeroEx)
                            {
                                // ADB 口令执行异常，返回错误
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }
                                return AdbSnReadResult.Failure($"ADB 口令异常: {ylzeroEx.Message}");
                            }

                            // Step 2: 执行 SN 读取命令（关键步骤）
                            // 完整命令: adb shell getprop sys.skyroam.osi.sn
                            var snResult = await _processRunner.RunAsync(
                                _adbPath,
                                "shell getprop sys.skyroam.osi.sn",
                                TotalTimeoutMs / MaxRetries,
                                timeoutCts.Token);

                            if (snResult.IsTimeout)
                            {
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }
                                return AdbSnReadResult.Failure("设备SN 读取超时", true);
                            }

                            if (!snResult.IsSuccess)
                            {
                                if (attempt < MaxRetries)
                                {
                                    await Task.Delay(RetryDelayMs, timeoutCts.Token);
                                    continue;
                                }

                                // 构建错误消息，包含 ErrorMessage 和 StandardError
                                var errorMessage = $"设备SN 读取失败: {snResult.ErrorMessage}";
                                if (!string.IsNullOrEmpty(snResult.StandardError))
                                {
                                    errorMessage += $"\nError: {snResult.StandardError}";
                                }
                                return AdbSnReadResult.Failure(errorMessage);
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

                                return AdbSnReadResult.Failure("设备SN is empty or whitespace");
                            }

                            // SN 读取成功，返回结果
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
        /// 读取设备信息（SN + Version），仅用于 UI「设备信息」按钮的临时调试接口。
        /// 完全独立于现有 SN 读取 / 自检 / MES 流程，可整体删除。
        /// </summary>
        public async Task<AdbDeviceInfoResult> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Step 0: ADB shell warm-up（冷启动下避免首次 shell 命令失败）
                await EnsureAdbShellWarmedUpAsync(cancellationToken);

                // Step 1: 尝试执行 ylzero（失败允许，仅记录日志，不做重试）
                try
                {
                    var ylzeroResult = await _processRunner.RunAsync(
                        _adbPath,
                        "shell ylzero",
                        TotalTimeoutMs,
                        cancellationToken);

                    if (!ylzeroResult.IsSuccess)
                    {
                        Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync ylzero failed: {ylzeroResult.ErrorMessage}");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    // 取消也视为失败，但不阻断后续异常处理逻辑，由外层 catch 统一转换
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync ylzero cancelled: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync ylzero exception: {ex.Message}");
                    // 按约定：ylzero 失败允许，继续后续 SN 读取
                }

                // Step 2: 读取 SN（失败即整个调用失败，不做重试）
                ProcessExecutionResult snResult;
                try
                {
                    snResult = await _processRunner.RunAsync(
                        _adbPath,
                        "shell getprop sys.skyroam.osi.sn",
                        TotalTimeoutMs,
                        cancellationToken);
                }
                catch (OperationCanceledException ex)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync SN cancelled: {ex.Message}");
                    return AdbDeviceInfoResult.Failure("SN read cancelled");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync SN exception: {ex.Message}");
                    return AdbDeviceInfoResult.Failure("SN read exception: " + ex.Message);
                }

                if (!snResult.IsSuccess)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync SN read failed: {snResult.ErrorMessage}");
                    return AdbDeviceInfoResult.Failure("SN read failed: " + (snResult.ErrorMessage ?? string.Empty));
                }

                var sn = snResult.StandardOutput?.Trim();
                if (string.IsNullOrWhiteSpace(sn))
                {
                    Debug.WriteLine("[AdbAccessService] ReadDeviceInfoAsync SN is empty or whitespace");
                    return AdbDeviceInfoResult.Failure("SN is empty or whitespace");
                }

                // Step 3: 读取版本号（失败允许，仅记录日志，结果中 Version 置空）
                string version = null;
                try
                {
                    var versionResult = await _processRunner.RunAsync(
                        _adbPath,
                        "shell getprop ro.build.display.id",
                        TotalTimeoutMs,
                        cancellationToken);

                    if (versionResult.IsSuccess)
                    {
                        version = versionResult.StandardOutput?.Trim();
                    }
                    else
                    {
                        Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync version read failed: {versionResult.ErrorMessage}");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync version cancelled: {ex.Message}");
                    // 版本读取失败不影响整体成功，只记录日志
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync version exception: {ex.Message}");
                    // 版本读取失败不影响整体成功，只记录日志
                }

                if (string.IsNullOrWhiteSpace(version))
                {
                    version = null;
                }

                return AdbDeviceInfoResult.Success(sn, version);
            }
            catch (OperationCanceledException ex)
            {
                // 所有异常必须被捕获，不向上传播
                Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync cancelled: {ex.Message}");
                return AdbDeviceInfoResult.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                // 所有异常必须被捕获，不向上传播
                Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync unexpected error: {ex.Message}");
                return AdbDeviceInfoResult.Failure("Unexpected error: " + ex.Message);
            }
        }

        /// <summary>
        /// 按项目配置读取设备信息（DeviceInfo），用于 Phase3 SN 校验流程。
        /// 优先尝试一次 ADB 命令读取全部字段；若配置未提供聚合命令或执行失败，则退回到现有 SN/版本读取逻辑，其他字段置空。
        /// 默认总超时 10 秒，期间内部允许多次重试。
        /// </summary>
        public async Task<DeviceInfo> ReadDeviceInfoAsync(ProjectProfile profile)
        {
            // 使用总超时控制，防止外部调用被长时间阻塞。
            using (var timeoutCts = new CancellationTokenSource(TotalTimeoutMs))
            {
                var token = timeoutCts.Token;
                string deviceSn = null;
                string wifiMac = null;
                string chipId = null;
                string boardVersion = null;
                string chargeBoardVersion = null;
                string androidVersion = null;

                try
                {
                    // Step 1：若 ProjectProfile 提供聚合命令，则优先使用。
                    // 目前项目尚未定义具体格式，这里只保留挂载点，实际解析规则可在后续 Phase 中补充。
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.AggregateDeviceInfoCommand))
                    {
                        try
                        {
                            await EnsureAdbShellWarmedUpAsync(token).ConfigureAwait(false);

                            var aggregateResult = await _processRunner.RunAsync(
                                _adbPath,
                                profile.AggregateDeviceInfoCommand,
                                TotalTimeoutMs,
                                token).ConfigureAwait(false);

                            if (aggregateResult.IsSuccess && !string.IsNullOrWhiteSpace(aggregateResult.StandardOutput))
                            {
                                // TODO: 根据后续文档定义解析规则，目前仅占位。
                                Debug.WriteLine("[AdbAccessService] Aggregate device info command executed, but parsing is not yet implemented.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AdbAccessService] Aggregate device info command failed: {ex.Message}");
                            // 聚合命令失败不阻断流程，继续使用分字段读取。
                        }
                    }

                    // Step 2：使用现有 SN 读取逻辑，保证与 Phase 2.5 行为一致。
                    var snResult = await ReadDeviceSnAsync(token).ConfigureAwait(false);
                    if (snResult.IsSuccess)
                    {
                        deviceSn = snResult.Sn;
                    }

                    // Step 3：读取 Android 版本（与调试接口一致）。
                    try
                    {
                        await EnsureAdbShellWarmedUpAsync(token).ConfigureAwait(false);

                        var versionResult = await _processRunner.RunAsync(
                            _adbPath,
                            "shell getprop ro.build.display.id",
                            TotalTimeoutMs / MaxRetries,
                            token).ConfigureAwait(false);

                        if (versionResult.IsSuccess)
                        {
                            var v = versionResult.StandardOutput?.Trim();
                            androidVersion = string.IsNullOrWhiteSpace(v) ? null : v;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync(ProjectProfile) version read failed: {ex.Message}");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    Debug.WriteLine($"[AdbAccessService] ReadDeviceInfoAsync(ProjectProfile) cancelled: {ex.Message}");
                }

                // 其余字段（WifiMac / ChipId / BoardVersion / ChargeBoardVersion）暂未在硬件协议中约定，先返回 null。
                // 业务层在使用时必须做好 null 防御处理。
                return new DeviceInfo
                {
                    DeviceSn = deviceSn,
                    WifiMac = wifiMac,
                    ChipId = chipId,
                    BoardVersion = boardVersion,
                    ChargeBoardVersion = chargeBoardVersion,
                    AndroidVersion = androidVersion
                };
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
