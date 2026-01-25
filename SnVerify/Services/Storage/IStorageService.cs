/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System.Collections.Generic;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 存储服务接口，负责 SQLite 数据持久化和 Excel 导出（Phase2 扩展）
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// 当前存储服务状态快照
        /// </summary>
        StorageSnapshot Snapshot { get; }

        /// <summary>
        /// 初始化 SQLite 数据库和表结构
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 创建批次
        /// </summary>
        /// <param name="batch">批次信息</param>
        Task CreateBatchAsync(BatchInfo batch);

        /// <summary>
        /// 检查批次是否存在
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <returns>批次是否存在</returns>
        Task<bool> BatchExistsAsync(string batchId);

        /// <summary>
        /// 检查指定批次内 SN 是否重复
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <param name="sn">序列号</param>
        /// <returns>是否重复</returns>
        Task<bool> IsSnDuplicateAsync(string batchId, string sn);

        /// <summary>
        /// 保存 SN 校验结果（Phase2：更新 Snapshot）
        /// </summary>
        /// <param name="result">校验结果</param>
        Task SaveVerifyResultAsync(SnVerifyResult result);

        /// <summary>
        /// 获取指定批次的所有校验结果
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <returns>校验结果列表（只读）</returns>
        Task<IReadOnlyList<SnVerifyResult>> GetResultsByBatchAsync(string batchId);

        /// <summary>
        /// 导出批次结果到 Excel 文件（Phase2：支持 PASS/FAIL 分表）
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <param name="outputDirectory">输出目录</param>
        Task ExportBatchResultAsync(string batchId, string outputDirectory);

        void Dispose();
    }
}
