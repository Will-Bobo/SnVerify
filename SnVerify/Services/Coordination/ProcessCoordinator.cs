/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 流程编排服务实现，负责协调各个 Service 完成 SN 校验流程
    /// </summary>
    public class ProcessCoordinator : IProcessCoordinator
    {
        private readonly string _batchId;
        private readonly IStorageService _storageService;
        private readonly IAdbAccessService _adbAccessService;
        private readonly ILoggingService _loggingService;
        private readonly object _lockObject = new object();
        private VerificationSnapshot _snapshot;

        /// <summary>
        /// 当前流程状态快照
        /// </summary>
        public VerificationSnapshot Snapshot
        {
            get
            {
                lock (_lockObject)
                {
                    return _snapshot ?? VerificationSnapshot.Idle();
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _snapshot = value;
                }
            }
        }

        /// <summary>
        /// 状态快照变化事件
        /// </summary>
        public event EventHandler<VerificationSnapshot> SnapshotChanged;

        /// <summary>
        /// 初始化流程编排服务
        /// </summary>
        /// <param name="batchId">当前批次 ID</param>
        /// <param name="storageService">存储服务</param>
        /// <param name="adbAccessService">ADB 访问服务</param>
        /// <param name="loggingService">日志服务（可选）</param>
        public ProcessCoordinator(
            string batchId,
            IStorageService storageService,
            IAdbAccessService adbAccessService,
            ILoggingService loggingService = null)
        {
            _batchId = batchId ?? throw new ArgumentNullException(nameof(batchId));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
            _loggingService = loggingService;
            _snapshot = VerificationSnapshot.Idle(_batchId);
        }

        /// <summary>
        /// 启动校验流程（原子化执行）
        /// </summary>
        public async Task StartVerificationAsync(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            // 原子锁定检查
            bool shouldProcess = false;
            lock (_lockObject)
            {
                if (!_snapshot.IsProcessing)
                {
                    shouldProcess = true;
                    UpdateSnapshot(VerificationSnapshot.Processing(sn, _batchId));
                }
            }

            if (!shouldProcess)
            {
                // 正在处理中，忽略本次请求
                return;
            }

            // 记录检验开始
            var verifyStartTime = DateTime.Now;
            _loggingService?.LogInfo($"检验开始，扫码枪SN: {sn}");

            try
            {
                // Step 1: 通过 ADB 读取设备 SN
                var adbResult = await _adbAccessService.ReadDeviceSnAsync();
                if (!adbResult.IsSuccess)
                {
                    var result = adbResult.IsTimeout ? "TIMEOUT" : "FAIL";
                    var failReason = adbResult.IsTimeout ? "ADB读取设备超时" : adbResult.ErrorReason;
                    await SaveOrUpdateFailResultAsync(sn, result, failReason, null);
                    _loggingService?.LogInfo($"检验结果 [{result}] , [扫码枪SN: {sn}, 设备SN: N/A] , 错误结果: {failReason}");
                    _loggingService?.LogInfo("检验结束");
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, result, failReason, _batchId, null));
                    return;
                }

                var deviceSN = adbResult.Sn;
                if (string.IsNullOrWhiteSpace(deviceSN))
                {
                    const string failReason = "ADB读取设备SN为空";
                    await SaveOrUpdateFailResultAsync(sn, "FAIL", failReason, null);
                    _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {sn}, 设备SN: N/A] , 错误结果: {failReason}");
                    _loggingService?.LogInfo("检验结束");
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", failReason, _batchId, null));
                    return;
                }

                // Step 2: 决策树校验逻辑（基于 SN_Sticker_Device_Relation_Rules.md）
                var stickerSN = sn.Trim();
                var deviceSNNormalized = deviceSN.Trim();

                // 规则 1：绑定一致，且无历史 PASS 绑定 → PASS
                if (stickerSN == deviceSNNormalized)
                {
                    // 优先检查绑定关系（规则2优先于规则1）
                    var bindingExists = await _storageService.IsBindingInPassHistoryAsync(stickerSN, deviceSNNormalized);
                    if (bindingExists)
                    {
                        // 规则 2：绑定一致，但存在历史 PASS 绑定 → FAIL（已出站）
                        const string failReason = "设备SN已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _batchId, deviceSNNormalized));
                        return;
                    }

                    // 检查是否在历史 PASS 中（用于规则1判断）
                    var stickerExists = await _storageService.IsStickerSnInPassHistoryAsync(stickerSN);
                    var deviceExists = await _storageService.IsDeviceSnInPassHistoryAsync(deviceSNNormalized);

                    if (!stickerExists && !deviceExists)
                    {
                        // 规则 1：PASS
                        await SaveResultAsync(stickerSN, "PASS", null, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [PASS] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 成功结果");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "PASS", null, _batchId, deviceSNNormalized));
                        return;
                    }
                    else
                    {
                        // 规则 2：绑定一致，但存在历史 PASS 绑定 → FAIL（已出站）
                        const string failReason = "设备SN已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _batchId, deviceSNNormalized));
                        return;
                    }
                }
                else
                {
                    // 绑定不一致：StickerSN != DeviceSN
                    const string mismatchReason = "设备SN 与 条形码SN [不匹配]";

                    // 规则 3：StickerSN 已存在于历史 PASS 绑定中 → FAIL（贴纸重复）
                    var stickerExists = await _storageService.IsStickerSnInPassHistoryAsync(stickerSN);
                    if (stickerExists)
                    {
                        var failReason = $"{mismatchReason}，并且 条形码SN 已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _batchId, deviceSNNormalized));
                        return;
                    }

                    // 规则 4：DeviceSN 已存在于历史 PASS 绑定中 → FAIL（设备已出站）
                    var deviceExists = await _storageService.IsDeviceSnInPassHistoryAsync(deviceSNNormalized);
                    if (deviceExists)
                    {
                        var failReason = $"{mismatchReason}，并且 设备SN 已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _batchId, deviceSNNormalized));
                        return;
                    }

                    // 规则 5：绑定不一致，且双方均无历史 PASS 绑定 → FAIL（包装不一致）
                    var failReason5 = mismatchReason;
                    await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason5, deviceSNNormalized);
                    _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason5}");
                    _loggingService?.LogInfo("检验结束");
                    UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason5, _batchId, deviceSNNormalized));
                    return;
                }
            }
            catch (Exception ex)
            {
                // 异常处理
                await SaveOrUpdateFailResultAsync(sn, "FAIL", $"EXCEPTION: {ex.Message}", null);
                _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {sn}, 设备SN: N/A] , 错误结果: EXCEPTION: {ex.Message}");
                _loggingService?.LogInfo("检验结束");
                UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", $"EXCEPTION: {ex.Message}", _batchId, null));
            }
        }

        /// <summary>
        /// 重置流程状态，允许下一次扫描
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                UpdateSnapshot(VerificationSnapshot.Idle(_batchId));
            }
        }

        /// <summary>
        /// 保存或更新 FAIL 结果：如果存在 FAIL 记录则更新，否则创建新记录
        /// </summary>
        private async Task SaveOrUpdateFailResultAsync(string sn, string result, string failReason, string deviceSN)
        {
            try
            {
                // 检查是否存在 FAIL 记录
                var existingFailResult = await _storageService.GetFailResultBySnAsync(_batchId, sn);
                
                if (existingFailResult != null)
                {
                    // 存在 FAIL 记录，更新它
                    existingFailResult.Result = result;
                    existingFailResult.FailReason = failReason;
                    existingFailResult.DeviceSN = deviceSN;
                    existingFailResult.VerifyTime = DateTime.Now;
                    await _storageService.UpdateVerifyResultAsync(existingFailResult);
                }
                else
                {
                    // 不存在 FAIL 记录，创建新记录
                    var verifyResult = new SnVerifyResult
                    {
                        BatchId = _batchId,
                        SN = sn,
                        DeviceSN = deviceSN,
                        Result = result,
                        FailReason = failReason,
                        VerifyTime = DateTime.Now
                    };
                    await _storageService.SaveVerifyResultAsync(verifyResult);
                }
            }
            catch
            {
                // 保存/更新失败不影响流程，记录到日志或忽略
                // 根据需求，可以在这里添加日志记录
            }
        }

        /// <summary>
        /// 保存校验结果到存储服务
        /// </summary>
        private async Task SaveResultAsync(string sn, string result, string failReason, string deviceSN)
        {
            try
            {
                var verifyResult = new SnVerifyResult
                {
                    BatchId = _batchId,
                    SN = sn,
                    DeviceSN = deviceSN,
                    Result = result,
                    FailReason = failReason,
                    VerifyTime = DateTime.Now
                };

                await _storageService.SaveVerifyResultAsync(verifyResult);
            }
            catch
            {
                // 保存失败不影响流程，记录到日志或忽略
                // 根据需求，可以在这里添加日志记录
            }
        }

        /// <summary>
        /// 更新快照并触发事件
        /// </summary>
        private void UpdateSnapshot(VerificationSnapshot newSnapshot)
        {
            lock (_lockObject)
            {
                _snapshot = newSnapshot;
            }

            // 触发事件（在锁外，避免死锁）
            OnSnapshotChanged(newSnapshot);
        }

        /// <summary>
        /// 触发快照变化事件
        /// </summary>
        protected virtual void OnSnapshotChanged(VerificationSnapshot snapshot)
        {
            var handler = SnapshotChanged;
            if (handler != null)
            {
                handler(this, snapshot);
            }
        }
    }
}
