/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// MES 接口状态快照（不可变对象）
    /// </summary>
    public class MESSnapshot
    {
        /// <summary>
        /// 是否正在处理中
        /// </summary>
        public bool IsProcessing { get; }

        /// <summary>
        /// 最后一次上传结果状态（SUCCESS/FAIL）
        /// </summary>
        public string LastResultStatus { get; }

        /// <summary>
        /// 错误消息（如果有）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 当前批次 ID
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 缓存的结果数量
        /// </summary>
        public int CachedCount { get; }

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态（空闲）
        /// </summary>
        public static MESSnapshot Idle(string batchId = null)
        {
            return new MESSnapshot(false, null, null, batchId, 0, DateTime.Now);
        }

        /// <summary>
        /// 创建处理中状态
        /// </summary>
        public static MESSnapshot Processing(string batchId = null)
        {
            return new MESSnapshot(true, null, null, batchId, 0, DateTime.Now);
        }

        /// <summary>
        /// 创建成功状态
        /// </summary>
        public static MESSnapshot Success(string batchId = null)
        {
            return new MESSnapshot(false, "SUCCESS", null, batchId, 0, DateTime.Now);
        }

        /// <summary>
        /// 创建失败状态
        /// </summary>
        public static MESSnapshot Failed(string errorMessage, string batchId = null, int cachedCount = 0)
        {
            return new MESSnapshot(false, "FAIL", errorMessage, batchId, cachedCount, DateTime.Now);
        }

        /// <summary>
        /// 创建缓存状态
        /// </summary>
        public static MESSnapshot Cached(string batchId, int cachedCount)
        {
            return new MESSnapshot(false, "CACHED", $"已缓存 {cachedCount} 条结果", batchId, cachedCount, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private MESSnapshot(
            bool isProcessing,
            string lastResultStatus,
            string errorMessage,
            string batchId,
            int cachedCount,
            DateTime timestamp)
        {
            IsProcessing = isProcessing;
            LastResultStatus = lastResultStatus;
            ErrorMessage = errorMessage;
            BatchId = batchId;
            CachedCount = cachedCount;
            Timestamp = timestamp;
        }
    }
}
