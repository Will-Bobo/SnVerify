/// <author>AI Assistant</author>
/// <remarks>导出字段取值解析：ExportFieldId → TestRecord 字段映射，位于 Storage/Export 层。</remarks>

using SnVerify.Domain.Export;
using SnVerify.Domain.Models;

namespace SnVerify.Services.Storage.Export
{
    /// <summary>
    /// 将 ExportFieldId 解析为导出单元格字符串值。
    /// </summary>
    public interface IExportValueResolver
    {
        /// <summary>
        /// 根据字段 ID 从 TestRecord 取对应值并格式化为字符串。
        /// </summary>
        string Resolve(ExportFieldId fieldId, TestRecord record);
    }
}
