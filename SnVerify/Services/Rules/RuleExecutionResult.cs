/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step3：规则链执行结果。
/// 仅承载流程事实（PASS/FAIL 与 FailReason、读取到的 DeviceInfo 等），不包含业务计算。
/// </remarks>

using SnVerify.Domain.Models;

namespace SnVerify.Services.Rules
{
    /// <summary>
    /// 规则链执行结果（不可变数据承载）。
    /// </summary>
    public class RuleExecutionResult
    {
        /// <summary>
        /// 最终结果：PASS / FAIL。
        /// </summary>
        public string Result { get; }

        /// <summary>
        /// 失败原因代码（Result=FAIL 时必须有值）。
        /// </summary>
        public string FailReason { get; }

        /// <summary>
        /// 本次执行读取到的设备信息（可能为 null）。
        /// </summary>
        public DeviceInfo DeviceInfo { get; }

        /// <summary>
        /// 设备 SN（来自 DeviceInfo；供 ProcessCoordinator 在 deviceSN fallback 时使用）。
        /// </summary>
        public string DeviceSn => DeviceInfo?.DeviceSn;

        private RuleExecutionResult(string result, string failReason, DeviceInfo deviceInfo)
        {
            Result = result;
            FailReason = failReason;
            DeviceInfo = deviceInfo;
        }

        /// <summary>
        /// 创建 PASS 结果。
        /// </summary>
        public static RuleExecutionResult Pass(DeviceInfo deviceInfo) => new RuleExecutionResult("PASS", null, deviceInfo);

        /// <summary>
        /// 创建 FAIL 结果。
        /// </summary>
        public static RuleExecutionResult Fail(string failReason, DeviceInfo deviceInfo = null) => new RuleExecutionResult("FAIL", failReason, deviceInfo);
    }
}

