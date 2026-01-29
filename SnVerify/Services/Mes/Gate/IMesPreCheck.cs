/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。契约见 MES_Plugin_Gate_Design_Freeze.md §4.2。
/// </remarks>

using System.Threading.Tasks;

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 前置闸口：每笔 SN 前调用，回答「能不能开始」，不参与 PASS/FAIL 判断。
    /// </summary>
    public interface IMesPreCheck
    {
        /// <summary>
        /// 执行 Pre-Gate 检查。
        /// </summary>
        /// <param name="context">当前 Session/Order/StickerSN 等上下文</param>
        /// <returns>Allow / Reject / DegradedAllow</returns>
        Task<MesPreCheckResult> CheckAsync(MesContext context);
    }
}
