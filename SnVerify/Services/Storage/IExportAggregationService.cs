/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：导出聚合由 B 做。按 OrderId/ProjectId 聚合 Session 并逐 Session 调用阶段 1 的 ExportBySessionAsync。
/// </remarks>

using System.Threading.Tasks;
using SnVerify.Domain.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 导出聚合服务：按订单或按项目聚合 Session，逐 Session 调用按 Session 导出 API，暴露给 ViewModel。
    /// </summary>
    public interface IExportAggregationService
    {
        /// <summary>
        /// 按订单导出：查该订单下所有 Session，逐 Session 调用 ExportBySessionAsync。
        /// </summary>
        /// <param name="orderId">订单 ID</param>
        /// <param name="outputDirectory">输出目录</param>
        /// <param name="filter">导出记录过滤（可选，默认 All）</param>
        Task ExportByOrderIdAsync(string orderId, string outputDirectory, ExportRecordFilter filter = null);

        /// <summary>
        /// 按项目导出：查该项目下所有 Session，逐 Session 调用 ExportBySessionAsync。
        /// </summary>
        /// <param name="projectId">项目 ID</param>
        /// <param name="outputDirectory">输出目录</param>
        /// <param name="filter">导出记录过滤（可选，默认 All）</param>
        Task ExportByProjectIdAsync(string projectId, string outputDirectory, ExportRecordFilter filter = null);
    }
}
