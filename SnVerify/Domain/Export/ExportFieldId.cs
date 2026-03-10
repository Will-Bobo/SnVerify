/// <author>AI Assistant</author>
/// <remarks>导出字段语义 ID，仅表达列含义，不包含取值逻辑。版本字段保持通用语义（ExpectedVersion/ActualVersion）。</remarks>

namespace SnVerify.Domain.Export
{
    /// <summary>
    /// 导出列字段 ID，用于配置与解析层映射。
    /// </summary>
    public enum ExportFieldId
    {
        /// <summary>
        /// 行号（RowNumber）：PASS/FAIL Sheet 内部的序号列，从 1 开始按行递增。
        /// </summary>
        RowNumber,
        Id,
        StickerSn,
        DeviceSn,
        WifiMac,
        ChipId,
        ExpectedVersion,
        ActualVersion,
        ExpectedBoardVersion,
        ActualBoardVersion,
        ExpectedChargeBoardVersion,
        ActualChargeBoardVersion,
        Result,
        ErrorDetail,
        VerificationTime
    }
}
