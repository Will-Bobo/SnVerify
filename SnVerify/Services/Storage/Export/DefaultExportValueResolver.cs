/// <author>AI Assistant</author>
/// <remarks>默认导出字段取值：ExportFieldId → TestRecord 映射，VerificationTime 使用 KM001 格式。</remarks>

using System;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Services.Rules;

namespace SnVerify.Services.Storage.Export
{
    /// <summary>
    /// 默认导出值解析器，集中维护 ExportFieldId 与 TestRecord 的映射。
    /// </summary>
    public sealed class DefaultExportValueResolver : IExportValueResolver
    {
        private const string VerificationTimeFormat = "yyyy年M月d日 HH:mm:ss";

        /// <inheritdoc />
        public string Resolve(ExportFieldId fieldId, TestRecord record)
        {
            if (record == null)
                return "";

            switch (fieldId)
            {
                case ExportFieldId.Id:
                    return record.Id.ToString();
                case ExportFieldId.StickerSn:
                    return record.StickerSN ?? "";
                case ExportFieldId.DeviceSn:
                    return record.DeviceSN ?? "";
                case ExportFieldId.WifiMac:
                    return record.WifiMac ?? "";
                case ExportFieldId.ChipId:
                    return record.ChipId ?? "";
                case ExportFieldId.ExpectedVersion:
                    return record.ExpectedVersion ?? "";
                case ExportFieldId.ActualVersion:
                    return record.ActualVersion ?? "";
                case ExportFieldId.ExpectedBoardVersion:
                    return record.ExpectedBoardVersion ?? "";
                case ExportFieldId.ActualBoardVersion:
                    return record.BoardVersion ?? "";
                case ExportFieldId.ExpectedChargeBoardVersion:
                    return record.ExpectedChargeBoardVersion ?? "";
                case ExportFieldId.ActualChargeBoardVersion:
                    return record.ChargeBoardVersion ?? "";
                case ExportFieldId.Result:
                    return record.Result ?? "";
                case ExportFieldId.ErrorDetail:
                    return FailReasonTextResolver.Resolve(record.FailReason);
                case ExportFieldId.VerificationTime:
                    return record.VerifyTime != default
                        ? record.VerifyTime.ToString(VerificationTimeFormat)
                        : "";
                default:
                    return "";
            }
        }
    }
}
