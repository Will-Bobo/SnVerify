/// <author>
/// AI Assistant
/// </author>

using System;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// 校验流程状态快照（不可变对象）
    /// </summary>
    public class VerificationSnapshot
    {
        /// <summary>
        /// 当前正在处理的 SN（扫码输入）
        /// </summary>
        public string CurrentSn { get; }

        /// <summary>
        /// 设备SN（从ADB读取）
        /// </summary>
        public string DeviceSN { get; }

        /// <summary>
        /// 是否正在处理中
        /// </summary>
        public bool IsProcessing { get; }

        /// <summary>
        /// 最后一次校验结果（PASS / FAIL / TIMEOUT）
        /// </summary>
        public string LastResult { get; }

        /// <summary>
        /// 失败原因（如果失败）
        /// </summary>
        public string FailReason { get; }

        /// <summary>
        /// 当前批次 ID（Phase 2.5 退场后由 SessionId 替代，此处保留兼容）
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 当前会话 ID（Phase 2.5 Session 为入口时使用，与 BatchId 同源）
        /// </summary>
        public string SessionId => BatchId;

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态（空闲）
        /// </summary>
        public static VerificationSnapshot Idle(string batchId = null)
        {
            return new VerificationSnapshot(null, null, false, null, null, batchId, DateTime.Now);
        }

        /// <summary>
        /// 创建处理中状态
        /// </summary>
        public static VerificationSnapshot Processing(string currentSn, string batchId = null)
        {
            return new VerificationSnapshot(currentSn, null, true, null, null, batchId, DateTime.Now);
        }

        /// <summary>
        /// 创建完成状态
        /// </summary>
        public static VerificationSnapshot Completed(string currentSn, string result, string failReason = null, string batchId = null, string deviceSN = null)
        {
            return new VerificationSnapshot(currentSn, deviceSN, false, result, failReason, batchId, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private VerificationSnapshot(string currentSn, string deviceSN, bool isProcessing, string lastResult, string failReason, string batchId, DateTime timestamp)
        {
            CurrentSn = currentSn;
            DeviceSN = deviceSN;
            IsProcessing = isProcessing;
            LastResult = lastResult;
            FailReason = failReason;
            BatchId = batchId;
            Timestamp = timestamp;
        }
    }
}
