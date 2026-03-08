/// <author>AI Assistant</author>
/// <remarks>Phase3 按 ProductCode 导出策略：单 Session 导出抽象。</remarks>

using System.Threading.Tasks;
using SnVerify.Domain.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 按 Session 导出到指定目录；无状态，可通过 ExportContext 传入参数。
    /// </summary>
    public interface ISessionExporter
    {
        /// <summary>
        /// 执行导出；结果写入 context.OutputDirectory，异常由调用方捕获。
        /// </summary>
        Task ExportAsync(ExportContext context);
    }
}
