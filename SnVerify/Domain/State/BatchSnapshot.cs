/// <author>
/// AI Assistant
/// </author>

using System;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// 批次管理状态快照（不可变对象）
    /// </summary>
    public class BatchSnapshot
    {
        /// <summary>
        /// 当前批次 ID
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 批次名称
        /// </summary>
        public string BatchName { get; }

        /// <summary>
        /// 是否处于活动状态
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        /// 错误消息（如果有）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 批次开始时间
        /// </summary>
        public DateTime? StartTime { get; }

        /// <summary>
        /// 批次结束时间
        /// </summary>
        public DateTime? EndTime { get; }

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态（无活动批次）
        /// </summary>
        public static BatchSnapshot Idle()
        {
            return new BatchSnapshot(null, null, false, null, null, null, DateTime.Now);
        }

        /// <summary>
        /// 创建活动批次状态
        /// </summary>
        public static BatchSnapshot Active(string batchId, string batchName, DateTime startTime)
        {
            return new BatchSnapshot(batchId, batchName, true, null, startTime, null, DateTime.Now);
        }

        /// <summary>
        /// 创建已结束批次状态
        /// </summary>
        public static BatchSnapshot Ended(string batchId, string batchName, DateTime startTime, DateTime endTime)
        {
            return new BatchSnapshot(batchId, batchName, false, null, startTime, endTime, DateTime.Now);
        }

        /// <summary>
        /// 创建错误状态
        /// </summary>
        public static BatchSnapshot Error(string errorMessage, string batchId = null)
        {
            return new BatchSnapshot(batchId, null, false, errorMessage, null, null, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private BatchSnapshot(
            string batchId,
            string batchName,
            bool isActive,
            string errorMessage,
            DateTime? startTime,
            DateTime? endTime,
            DateTime timestamp)
        {
            BatchId = batchId;
            BatchName = batchName;
            IsActive = isActive;
            ErrorMessage = errorMessage;
            StartTime = startTime;
            EndTime = endTime;
            Timestamp = timestamp;
        }
    }
}
