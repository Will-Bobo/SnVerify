/// <author>
/// AI Assistant
/// </author>

using System;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// 扫码输入状态快照（不可变对象）
    /// </summary>
    public class ScanSnapshot
    {
        /// <summary>
        /// 是否正在处理中
        /// </summary>
        public bool IsProcessing { get; }

        /// <summary>
        /// 最后一次扫描的 SN
        /// </summary>
        public string LastScanSN { get; }

        /// <summary>
        /// 错误消息（如果有）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 当前批次 ID
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态（空闲）
        /// </summary>
        public static ScanSnapshot Idle(string batchId = null)
        {
            return new ScanSnapshot(false, null, null, batchId, DateTime.Now);
        }

        /// <summary>
        /// 创建处理中状态
        /// </summary>
        public static ScanSnapshot Processing(string sn, string batchId = null)
        {
            return new ScanSnapshot(true, sn, null, batchId, DateTime.Now);
        }

        /// <summary>
        /// 创建错误状态
        /// </summary>
        public static ScanSnapshot Error(string sn, string errorMessage, string batchId = null)
        {
            return new ScanSnapshot(false, sn, errorMessage, batchId, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private ScanSnapshot(bool isProcessing, string lastScanSN, string errorMessage, string batchId, DateTime timestamp)
        {
            IsProcessing = isProcessing;
            LastScanSN = lastScanSN;
            ErrorMessage = errorMessage;
            BatchId = batchId;
            Timestamp = timestamp;
        }
    }
}
