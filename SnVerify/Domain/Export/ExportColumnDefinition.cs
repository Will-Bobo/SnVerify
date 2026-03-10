/// <author>AI Assistant</author>
/// <remarks>导出列元数据，仅保留 HeaderResourceKey，列头文案由 Resources 管理。</remarks>

namespace SnVerify.Domain.Export
{
    /// <summary>
    /// 单列导出定义：字段 ID + 列头资源 Key。
    /// </summary>
    public sealed class ExportColumnDefinition
    {
        /// <summary>字段语义 ID。</summary>
        public ExportFieldId FieldId { get; }

        /// <summary>列头文案的资源 Key（如 Export_Km001_ChipId），用于从 Resources 读取。</summary>
        public string HeaderResourceKey { get; }

        /// <summary>
        /// 构造列定义。
        /// </summary>
        public ExportColumnDefinition(ExportFieldId fieldId, string headerResourceKey)
        {
            FieldId = fieldId;
            HeaderResourceKey = headerResourceKey ?? "";
        }
    }
}
