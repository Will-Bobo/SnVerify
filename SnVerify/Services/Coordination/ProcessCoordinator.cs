/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
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
        public ProcessCoordinator(
            string batchId,
            IStorageService storageService,
            IAdbAccessService adbAccessService)
        {
            _batchId = batchId ?? throw new ArgumentNullException(nameof(batchId));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
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

            try
            {
                // Step 1: 检查批次内 SN 是否重复
                var isDuplicate = await _storageService.IsSnDuplicateAsync(_batchId, sn);
                if (isDuplicate)
                {
                    await SaveResultAsync(sn, "FAIL", "DUPLICATE_SN");
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", "DUPLICATE_SN", _batchId));
                    return;
                }

                // Step 2: 通过 ADB 读取设备 SN
                var adbResult = await _adbAccessService.ReadDeviceSnAsync();
                if (!adbResult.IsSuccess)
                {
                    var result = adbResult.IsTimeout ? "TIMEOUT" : "FAIL";
                    var failReason = adbResult.IsTimeout ? "ADB_TIMEOUT" : adbResult.ErrorReason;
                    await SaveResultAsync(sn, result, failReason);
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, result, failReason, _batchId));
                    return;
                }

                var snAdb = adbResult.Sn;
                if (string.IsNullOrWhiteSpace(snAdb))
                {
                    await SaveResultAsync(sn, "FAIL", "ADB_SN_EMPTY");
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", "ADB_SN_EMPTY", _batchId));
                    return;
                }

                // Step 3: 校验 SN 一致性（不区分大小写）
                var snScanNormalized = sn.Trim().ToUpperInvariant();
                var snAdbNormalized = snAdb.Trim().ToUpperInvariant();

                if (snScanNormalized == snAdbNormalized)
                {
                    // PASS
                    await SaveResultAsync(sn, "PASS", null);
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "PASS", null, _batchId));
                }
                else
                {
                    // FAIL - SN 不一致
                    var failReason = $"MISMATCH: Scan={snScanNormalized}, ADB={snAdbNormalized}";
                    await SaveResultAsync(sn, "FAIL", failReason);
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", failReason, _batchId));
                }
            }
            catch (Exception ex)
            {
                // 异常处理
                await SaveResultAsync(sn, "FAIL", $"EXCEPTION: {ex.Message}");
                UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", $"EXCEPTION: {ex.Message}", _batchId));
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
        /// 保存校验结果到存储服务
        /// </summary>
        private async Task SaveResultAsync(string sn, string result, string failReason)
        {
            try
            {
                var verifyResult = new SnVerifyResult
                {
                    BatchId = _batchId,
                    SN = sn,
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
