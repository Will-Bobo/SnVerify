/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。契约见 MES_Plugin_Gate_Design_Freeze.md §4.2。
/// </remarks>

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// Pre-Gate 极简三态：允许 / 拒绝 / 降级放行。
    /// </summary>
    public enum MesPreCheckDecision
    {
        /// <summary>允许进入本站流程</summary>
        Allow = 0,

        /// <summary>明确禁止进入本站流程</summary>
        Reject = 1,

        /// <summary>MES 不可用，但允许继续（降级放行）</summary>
        DegradedAllow = 2
    }
}
