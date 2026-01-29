/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。契约见 MES_Plugin_Gate_Design_Freeze.md §4.2。
/// </remarks>

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// Pre-Gate 返回结果：三态决策 + 可选原因（Reject 时建议填写）。
    /// </summary>
    public class MesPreCheckResult
    {
        /// <summary>允许 / 拒绝 / 降级放行</summary>
        public MesPreCheckDecision Decision { get; set; }

        /// <summary>拒绝或降级时的原因说明（可选）</summary>
        public string Reason { get; set; }
    }
}
