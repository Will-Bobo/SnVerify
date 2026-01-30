 /// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。契约见 MES_Plugin_Gate_Design_Freeze.md §5。
/// 上报失败不得影响本站结果，仅日志与 UI 提示。
/// </remarks>

using System.Threading.Tasks;

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 结果上报：本站结果落库后异步调用，失败只记日志并触发 UI 提示，不反写结果、不阻断下一笔。
    /// </summary>
    public interface IMesResultReporter
    {
        /// <summary>
        /// 上报单条检验结果。失败时抛出或返回需由调用方捕获并仅做日志/UI 提示。
        /// </summary>
        /// <param name="context">本站结果上下文（只读，不得被本方法修改）</param>
        Task ReportTestResultAsync(TestResultContext context);
    }
}
