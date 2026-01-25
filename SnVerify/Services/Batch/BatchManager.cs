/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Batch
{
    /// <summary>
    /// 批次管理服务实现，负责批次创建、开始、结束管理（Phase2 新增）
    /// </summary>
    public class BatchManager : IBatchManager
    {
        private readonly IStorageService _storageService;
        private readonly IFileLogger _logger;
        private readonly object _snapshotLock = new object();
        private BatchSnapshot _snapshot;

        /// <summary>
        /// 当前批次管理状态快照
        /// </summary>
        public BatchSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? BatchSnapshot.Idle();
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
        /// 初始化批次管理服务
        /// </summary>
        /// <param name="storageService">存储服务</param>
        /// <param name="logger">日志记录器（可选）</param>
        public BatchManager(IStorageService storageService, IFileLogger logger = null)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _logger = logger ?? new NullFileLogger();
            _snapshot = BatchSnapshot.Idle();
        }

        /// <summary>
        /// 创建批次
        /// </summary>
        public BatchInfo CreateBatch(string batchName = null)
        {
            try
            {
                // 生成批次 ID（如果未提供名称，使用时间格式）
                string batchId;
                if (string.IsNullOrWhiteSpace(batchName))
                {
                    var now = DateTime.Now;
                    batchId = $"batch_{now:yyyyMMdd_HHmmss}";
                }
                else
                {
                    batchId = batchName;
                }

                // 检查批次是否已存在
                var existsTask = _storageService.BatchExistsAsync(batchId);
                existsTask.Wait();
                if (existsTask.Result)
                {
                    Snapshot = BatchSnapshot.Error($"批次 {batchId} 已存在", batchId);
                    _logger?.LogWarning($"批次已存在: {batchId}");
                    throw new InvalidOperationException($"批次 {batchId} 已存在");
                }

                // 创建批次信息
                var batch = new BatchInfo
                {
                    BatchId = batchId,
                    StartTime = DateTime.Now,
                    Operator = null,
                    Remark = null
                };

                // 在 StorageService 中创建批次
                var createTask = _storageService.CreateBatchAsync(batch);
                createTask.Wait();

                _logger?.LogInfo($"批次创建成功: BatchId={batchId}");
                return batch;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Snapshot = BatchSnapshot.Error($"创建批次失败: {ex.Message}");
                _logger?.LogError($"创建批次失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 开始批次
        /// </summary>
        public void StartBatch(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
            {
                Snapshot = BatchSnapshot.Error("批次 ID 不能为空");
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            }

            try
            {
                // 检查是否有活动批次
                if (Snapshot.IsActive)
                {
                    Snapshot = BatchSnapshot.Error($"已有活动批次 {Snapshot.BatchId}，无法开始新批次", batchId);
                    _logger?.LogWarning($"已有活动批次 {Snapshot.BatchId}，无法开始新批次: {batchId}");
                    throw new InvalidOperationException($"已有活动批次 {Snapshot.BatchId}，无法开始新批次");
                }

                // 检查批次是否存在
                var existsTask = _storageService.BatchExistsAsync(batchId);
                existsTask.Wait();
                if (!existsTask.Result)
                {
                    Snapshot = BatchSnapshot.Error($"批次 {batchId} 不存在", batchId);
                    _logger?.LogWarning($"批次不存在: {batchId}");
                    throw new InvalidOperationException($"批次 {batchId} 不存在");
                }

                // 更新 Snapshot 为活动状态
                var startTime = DateTime.Now;
                Snapshot = BatchSnapshot.Active(batchId, batchId, startTime);
                _logger?.LogInfo($"批次开始: BatchId={batchId}");
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Snapshot = BatchSnapshot.Error($"开始批次失败: {ex.Message}", batchId);
                _logger?.LogError($"开始批次失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 结束当前活动批次
        /// </summary>
        public void EndBatch()
        {
            try
            {
                if (!Snapshot.IsActive)
                {
                    Snapshot = BatchSnapshot.Error("没有活动批次，无法结束");
                    _logger?.LogWarning("没有活动批次，无法结束");
                    throw new InvalidOperationException("没有活动批次，无法结束");
                }

                var batchId = Snapshot.BatchId;
                var batchName = Snapshot.BatchName;
                var startTime = Snapshot.StartTime ?? DateTime.Now;
                var endTime = DateTime.Now;

                // 更新 Snapshot 为已结束状态
                Snapshot = BatchSnapshot.Ended(batchId, batchName, startTime, endTime);
                _logger?.LogInfo($"批次结束: BatchId={batchId}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Snapshot = BatchSnapshot.Error($"结束批次失败: {ex.Message}", Snapshot?.BatchId);
                _logger?.LogError($"结束批次失败: {ex.Message}", ex);
                throw;
            }
        }
    }
}
