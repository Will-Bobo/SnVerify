/// <author>AI Assistant</author>
/// <remarks>
/// Phase3 按 ProductCode 导出策略：单次 Session 导出的上下文（command/context 模式）。
/// 未来可扩展 ZipName、ProjectName、OrderId、ExportTime 等。
/// </remarks>

namespace SnVerify.Domain.Export
{
    /// <summary>
    /// 单次 Session 导出的调用参数；Exporter 无状态，通过 context 传入参数。
    /// </summary>
    public class ExportContext
    {
        /// <summary>会话内部 Id（TestSession.Id）。</summary>
        public int SessionId { get; set; }

        /// <summary>会话业务名（如 OrderName_yyyyMMdd_HHmmss），用于 Summary 等展示；可选。</summary>
        public string SessionName { get; set; }

        /// <summary>导出输出目录（xlsx/txt 写入此目录）。</summary>
        public string OutputDirectory { get; set; }

        /// <summary>记录过滤（仅 Legacy 使用）；null 时使用 ExportRecordFilter.All。</summary>
        public ExportRecordFilter Filter { get; set; }
    }
}
