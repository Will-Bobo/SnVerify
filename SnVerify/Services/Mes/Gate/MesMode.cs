/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。契约见 MES_Plugin_Gate_Design_Freeze.md §10.2。
/// Phase 2.5 不允许 Strict，仅分支预留。
/// </remarks>

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 开关模式。Phase 2.5 仅 Disabled/Enabled，不允许 Strict。
    /// </summary>
    public enum MesMode
    {
        /// <summary>完全不启用 MES，不调用 PreCheck/Post-Report</summary>
        Disabled = 0,

        /// <summary>启用 MES，失败不阻断；Post-Report 失败仅日志与 UI 提示</summary>
        Enabled = 1,

        /// <summary>Phase 3 预留：MES FAIL 阻断流程；PreCheck Reject 或 MES 异常时本条检验不继续</summary>
        Strict = 2,
    }
}
