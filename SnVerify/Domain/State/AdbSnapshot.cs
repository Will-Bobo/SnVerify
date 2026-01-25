/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;

namespace SnVerify.Domain.State
{
    /// <summary>
    /// ADB 访问状态快照（不可变对象）
    /// </summary>
    public class AdbSnapshot
    {
        /// <summary>
        /// 是否正在处理中
        /// </summary>
        public bool IsProcessing { get; }

        /// <summary>
        /// 最后一次读取的 SN
        /// </summary>
        public string LastSN { get; }

        /// <summary>
        /// 错误消息（如果有）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 检测到的设备 ID 列表
        /// </summary>
        public IReadOnlyList<string> DeviceIds { get; }

        /// <summary>
        /// 当前批次 ID
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 是否检测到多设备
        /// </summary>
        public bool HasMultipleDevices { get; }

        /// <summary>
        /// 状态更新时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 创建初始状态（空闲）
        /// </summary>
        public static AdbSnapshot Idle(string batchId = null)
        {
            return new AdbSnapshot(false, null, null, null, batchId, false, DateTime.Now);
        }

        /// <summary>
        /// 创建处理中状态
        /// </summary>
        public static AdbSnapshot Processing(string batchId = null)
        {
            return new AdbSnapshot(true, null, null, null, batchId, false, DateTime.Now);
        }

        /// <summary>
        /// 创建成功状态
        /// </summary>
        public static AdbSnapshot Success(string sn, string batchId = null)
        {
            return new AdbSnapshot(false, sn, null, null, batchId, false, DateTime.Now);
        }

        /// <summary>
        /// 创建错误状态
        /// </summary>
        public static AdbSnapshot Error(string errorMessage, string batchId = null)
        {
            return new AdbSnapshot(false, null, errorMessage, null, batchId, false, DateTime.Now);
        }

        /// <summary>
        /// 创建多设备警告状态
        /// </summary>
        public static AdbSnapshot MultipleDevicesWarning(IReadOnlyList<string> deviceIds, string batchId = null)
        {
            return new AdbSnapshot(false, null, "Multiple devices detected", deviceIds, batchId, true, DateTime.Now);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private AdbSnapshot(
            bool isProcessing,
            string lastSN,
            string errorMessage,
            IReadOnlyList<string> deviceIds,
            string batchId,
            bool hasMultipleDevices,
            DateTime timestamp)
        {
            IsProcessing = isProcessing;
            LastSN = lastSN;
            ErrorMessage = errorMessage;
            DeviceIds = deviceIds ?? (IReadOnlyList<string>)new List<string>();
            BatchId = batchId;
            HasMultipleDevices = hasMultipleDevices;
            Timestamp = timestamp;
        }
    }
}
