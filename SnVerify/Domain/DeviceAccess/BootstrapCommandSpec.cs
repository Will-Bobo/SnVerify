/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：单条 Bootstrap 命令规格（退出码宽容 + 超时策略）。Domain 层。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 单条 Bootstrap 命令规格：命令文本、可接受退出码、超时策略。
    /// </summary>
    public class BootstrapCommandSpec
    {
        /// <summary>要执行的命令（如 "shell ylzero"）。</summary>
        public string Command { get; set; }

        /// <summary>
        /// 可接受的退出码（可选）。执行后若 IsSuccess 或 ExitCode 在此列表中则视为通过；
        /// null 或空表示仅 IsSuccess 时通过。
        /// </summary>
        public int[] AcceptableExitCodes { get; set; }

        /// <summary>超时时的处理策略，默认 Fail。</summary>
        public BootstrapTimeoutBehavior TimeoutBehavior { get; set; } = BootstrapTimeoutBehavior.Fail;
    }
}
