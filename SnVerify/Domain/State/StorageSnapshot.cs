/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// 存储服务状态快照（不可变对象）
    /// </summary>
    public class StorageSnapshot
    {
        /// <summary>
        /// 是否正在处理中
        /// </summary>
        public bool IsProcessing { get; }

        /// <summary>
        /// 最后一次保存的 SN
        /// </summary>
        public string LastSavedSN { get; }

        /// <summary>
        /// 当前批次 ID
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 错误消息（如果有）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 当前批次的总记录数
        /// </summary>
        public int RecordCount { get; }

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态（空闲）
        /// </summary>
        public static StorageSnapshot Idle(string batchId = null)
        {
            return new StorageSnapshot(false, null, batchId, null, 0, DateTime.Now);
        }

        /// <summary>
        /// 创建处理中状态
        /// </summary>
        public static StorageSnapshot Processing(string batchId = null)
        {
            return new StorageSnapshot(true, null, batchId, null, 0, DateTime.Now);
        }

        /// <summary>
        /// 创建保存成功状态
        /// </summary>
        public static StorageSnapshot Saved(string sn, string batchId, int recordCount)
        {
            return new StorageSnapshot(false, sn, batchId, null, recordCount, DateTime.Now);
        }

        /// <summary>
        /// 创建错误状态
        /// </summary>
        public static StorageSnapshot Error(string errorMessage, string batchId = null)
        {
            return new StorageSnapshot(false, null, batchId, errorMessage, 0, DateTime.Now);
        }

        /// <summary>
        /// 创建重复 SN 警告状态
        /// </summary>
        public static StorageSnapshot DuplicateSn(string sn, string batchId)
        {
            return new StorageSnapshot(false, sn, batchId, $"SN {sn} already exists in batch {batchId}", 0, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private StorageSnapshot(
            bool isProcessing,
            string lastSavedSN,
            string batchId,
            string errorMessage,
            int recordCount,
            DateTime timestamp)
        {
            IsProcessing = isProcessing;
            LastSavedSN = lastSavedSN;
            BatchId = batchId;
            ErrorMessage = errorMessage;
            RecordCount = recordCount;
            Timestamp = timestamp;
        }
    }
}
