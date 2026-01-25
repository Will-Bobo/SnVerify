/// <author>
/// AI Assistant
/// </author>

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// ADB SN 读取结果（不可变对象）
    /// </summary>
    public class AdbSnReadResult
    {
        /// <summary>
        /// 是否成功读取 SN
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 读取到的 SN（成功时不为空）
        /// </summary>
        public string Sn { get; }

        /// <summary>
        /// 错误原因（失败时不为空）
        /// </summary>
        public string ErrorReason { get; }

        /// <summary>
        /// 是否因超时失败
        /// </summary>
        public bool IsTimeout { get; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        /// <param name="sn">读取到的 SN</param>
        public static AdbSnReadResult Success(string sn)
        {
            return new AdbSnReadResult(true, sn, null, false);
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        /// <param name="errorReason">错误原因</param>
        /// <param name="isTimeout">是否超时</param>
        public static AdbSnReadResult Failure(string errorReason, bool isTimeout = false)
        {
            return new AdbSnReadResult(false, null, errorReason, isTimeout);
        }

        /// <summary>
        /// 私有构造函数，确保不可变性
        /// </summary>
        private AdbSnReadResult(bool isSuccess, string sn, string errorReason, bool isTimeout)
        {
            IsSuccess = isSuccess;
            Sn = sn;
            ErrorReason = errorReason;
            IsTimeout = isTimeout;
        }
    }
}
