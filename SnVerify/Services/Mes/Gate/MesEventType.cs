/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 4：MES 事件类型（仅通知用途，不得影响本站 PASS/FAIL）。
/// 契约边界：MES_Plugin_Gate_Design_Freeze.md §3.3 / §10.4。
/// </remarks>

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 事件类型（用于 UI 弱提示与日志，不用于业务裁决）。
    /// </summary>
    public enum MesEventType
    {
        /// <summary>
        /// 结果上报失败（Post-Report 失败）。不影响本站结果。
        /// </summary>
        ReportFailed = 0,

        /// <summary>
        /// MES 连接丢失（可选）。不影响本站结果。
        /// </summary>
        ConnectionLost = 1
    }
}

