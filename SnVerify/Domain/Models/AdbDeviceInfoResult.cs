/// <author>
/// AI Assistant
/// </author>
using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// ADB 设备信息读取结果（临时调试接口，完全可删除）
    /// </summary>
    public class AdbDeviceInfoResult
    {
        /// <summary>
        /// 是否成功读取设备信息
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 设备 SN（成功时不为空）
        /// </summary>
        public string DeviceSn { get; }

        /// <summary>
        /// 设备版本号（成功且读取到时不为空）
        /// </summary>
        public string DeviceVersion { get; }

        /// <summary>
        /// 错误信息（失败时不为空）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static AdbDeviceInfoResult Success(string deviceSn, string deviceVersion)
        {
            if (string.IsNullOrWhiteSpace(deviceSn))
                throw new ArgumentException("deviceSn cannot be null or whitespace when success.", nameof(deviceSn));

            return new AdbDeviceInfoResult(true, deviceSn, deviceVersion, null);
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static AdbDeviceInfoResult Failure(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = "Unknown error";
            }
            return new AdbDeviceInfoResult(false, null, null, errorMessage);
        }

        private AdbDeviceInfoResult(bool isSuccess, string deviceSn, string deviceVersion, string errorMessage)
        {
            IsSuccess = isSuccess;
            DeviceSn = deviceSn;
            DeviceVersion = deviceVersion;
            ErrorMessage = errorMessage;
        }
    }
}

