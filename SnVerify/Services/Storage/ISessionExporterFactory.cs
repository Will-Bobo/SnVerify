/// <author>AI Assistant</author>
/// <remarks>Phase3 按 ProductCode 选择 Exporter；productCode 大小写不敏感。</remarks>

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 根据 ProductCode 返回对应 Exporter；null 或非 KM001 时返回 Legacy。
    /// </summary>
    public interface ISessionExporterFactory
    {
        /// <summary>
        /// 获取 Exporter；productCode 经 Trim 与忽略大小写比较，如 "KM001"/"km001" 均返回 KM001 Exporter。
        /// </summary>
        ISessionExporter GetExporter(string productCode);
    }
}
