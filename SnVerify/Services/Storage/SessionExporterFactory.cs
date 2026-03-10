/// <author>AI Assistant</author>
/// <remarks>按 ProductCode 选择 Exporter；内部使用 string.Equals(..., OrdinalIgnoreCase)。</remarks>

using System;
using SnVerify.Infrastructure.Export;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 根据 ProductCode 返回 Legacy 或 KM001 Exporter；productCode 可为 null，大小写不敏感。
    /// </summary>
    public class SessionExporterFactory : ISessionExporterFactory
    {
        private readonly ISessionExporter _legacyExporter;
        private readonly ISessionExporter _km001Exporter;

        /// <summary>
        /// 使用存储服务与导出配置、值解析器创建工厂；KM001 使用 Km001SessionExporter。
        /// </summary>
        public SessionExporterFactory(IStorageService storage, IProductExportRegistry exportRegistry, IExportValueResolver valueResolver)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (exportRegistry == null) throw new ArgumentNullException(nameof(exportRegistry));
            if (valueResolver == null) throw new ArgumentNullException(nameof(valueResolver));
            _legacyExporter = new LegacySessionExporter(storage);
            _km001Exporter = new Km001SessionExporter(storage, exportRegistry, valueResolver);
        }

        /// <inheritdoc />
        public ISessionExporter GetExporter(string productCode)
        {
            if (string.Equals(productCode?.Trim(), "KM001", StringComparison.OrdinalIgnoreCase))
                return _km001Exporter;
            return _legacyExporter;
        }
    }
}
