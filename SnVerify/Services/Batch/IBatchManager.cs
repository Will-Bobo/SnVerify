/// <author>
/// AI Assistant
/// </author>

using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Batch
{
    /// <summary>
    /// 批次管理服务接口，负责批次创建、开始、结束管理（Phase2 新增）
    /// </summary>
    public interface IBatchManager
    {
        /// <summary>
        /// 当前批次管理状态快照
        /// </summary>
        BatchSnapshot Snapshot { get; }

        /// <summary>
        /// 创建批次
        /// </summary>
        /// <param name="batchName">批次名称（可选，如果为 null 则使用默认时间命名）</param>
        /// <returns>创建的批次信息</returns>
        BatchInfo CreateBatch(string batchName = null);

        /// <summary>
        /// 开始批次
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        void StartBatch(string batchId);

        /// <summary>
        /// 结束当前活动批次
        /// </summary>
        void EndBatch();
    }
}
