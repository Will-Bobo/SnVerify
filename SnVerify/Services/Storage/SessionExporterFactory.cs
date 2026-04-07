/// <author>AI Assistant</author>
/// <remarks>按 ProductCode 选择 Exporter；内部使用 string.Equals(..., OrdinalIgnoreCase)。</remarks>

using System;
using SnVerify.Domain.Product;
using SnVerify.Infrastructure.Export;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 根据 ProductCode 与 ProductProfile.Mode 返回 Legacy 或 Phase3 配置化 Exporter。
    /// </summary>
    public class SessionExporterFactory : ISessionExporterFactory
    {
        private readonly ISessionExporter _legacyExporter;
        private readonly ISessionExporter _phase3Exporter;
        private readonly IProductRegistry _productRegistry;

        public SessionExporterFactory(IStorageService storage, IProductExportRegistry exportRegistry, IExportValueResolver valueResolver, IProductRegistry productRegistry)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (exportRegistry == null) throw new ArgumentNullException(nameof(exportRegistry));
            if (valueResolver == null) throw new ArgumentNullException(nameof(valueResolver));
            if (productRegistry == null) throw new ArgumentNullException(nameof(productRegistry));
            _productRegistry = productRegistry;
            _legacyExporter = new LegacySessionExporter(storage);
            _phase3Exporter = new Km001SessionExporter(storage, exportRegistry, valueResolver, productRegistry);
        }

        /// <inheritdoc />
        public ISessionExporter GetExporter(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return _legacyExporter;
            var profile = _productRegistry.Get(productCode.Trim());
            if (profile != null && profile.Mode == VerificationMode.Phase3)
                return _phase3Exporter;
            return _legacyExporter;
        }
    }
}
