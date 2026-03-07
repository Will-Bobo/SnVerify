/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：Bootstrap 命令超时策略。Domain 层。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// Bootstrap 命令超时时的处理策略。
    /// </summary>
    public enum BootstrapTimeoutBehavior
    {
        /// <summary>超时视为失败，抛异常（默认）。</summary>
        Fail,

        /// <summary>超时视为通过，继续下一条（Warmup 宽容：无输出但设备已 ready）。</summary>
        Ignore,

        /// <summary>超时后重试该条命令（次数上限由实现约定）。</summary>
        Retry
    }
}
