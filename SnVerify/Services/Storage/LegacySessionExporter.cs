/// <author>AI Assistant</author>
/// <remarks>Phase3 导出策略：Legacy（SOLTAG25）— 委托现有 Storage.ExportBySessionAsync。</remarks>

using System.Threading.Tasks;
using SnVerify.Domain.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// Legacy 单 Session 导出：使用现有 ExportBySessionAsync，带 VerifyType Filter。
    /// </summary>
    public class LegacySessionExporter : ISessionExporter
    {
        private readonly IStorageService _storage;

        public LegacySessionExporter(IStorageService storage)
        {
            _storage = storage ?? throw new System.ArgumentNullException(nameof(storage));
        }

        /// <inheritdoc />
        public Task ExportAsync(ExportContext context)
        {
            if (context == null)
                throw new System.ArgumentNullException(nameof(context));
            var filter = context.Filter ?? ExportRecordFilter.All;
            return _storage.ExportBySessionAsync(context.SessionId, context.OutputDirectory, filter);
        }
    }
}
