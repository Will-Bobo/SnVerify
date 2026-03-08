/// <author>AI Assistant</author>
/// <remarks>Phase3 按 ProductCode 选择 Exporter；内部使用 string.Equals(..., OrdinalIgnoreCase)。</remarks>

using System;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 根据 ProductCode 返回 Legacy 或 KM001 Exporter；productCode 可为 null，大小写不敏感。
    /// </summary>
    public class SessionExporterFactory : ISessionExporterFactory
    {
        private readonly ISessionExporter _legacyExporter;
        private readonly ISessionExporter _km001Exporter;

        public SessionExporterFactory(IStorageService storage)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            _legacyExporter = new LegacySessionExporter(storage);
            _km001Exporter = new Phase3Km001SessionExporter(storage);
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
