namespace SnVerify.Domain.Product
{
    public sealed class Yme009BatchScope
    {
        public Yme009BatchScope(string productName, string orderName)
        {
            ProductName = productName ?? string.Empty;
            OrderName = orderName ?? string.Empty;
        }

        public string ProductName { get; }

        public string OrderName { get; }
    }

    public enum Yme009PassHistoryQueryDisposition
    {
        NotEvaluated = 0,
        NoHistoricalPass = 1,
        HistoricalPass = 2,
        QueryFailed = 3
    }

    /// <summary>
    /// 在五项判定完成之后选择主失败原因。不访问数据库。
    /// </summary>
    public static class Yme009PassHistoryDecision
    {
        public static Yme009InspectionDecision Apply(
            Yme009InspectionDecision judged,
            string scannedSn,
            Yme009PassHistoryQueryDisposition history)
        {
            if (judged == null)
                throw new System.ArgumentNullException(nameof(judged));
            if (judged.IsSessionParameterInvalid)
                return judged;

            if (IsConfirmedMismatch(judged, scannedSn))
                return Copy(judged, Yme009ResultClassifier.FailResult, Yme009FailReasonCodes.SnMismatch);

            if (!CanQueryHistory(judged.Attributes, scannedSn))
                return judged;

            if (history == Yme009PassHistoryQueryDisposition.HistoricalPass)
                return Copy(judged, Yme009ResultClassifier.FailResult, Yme009FailReasonCodes.SnDuplicate);

            if (history == Yme009PassHistoryQueryDisposition.QueryFailed)
                return Copy(judged, Yme009ResultClassifier.FailResult, Yme009FailReasonCodes.PassHistoryQueryFail);

            return judged;
        }

        public static bool CanQueryHistory(Yme009ParsedAttributes attributes, string scannedSn)
        {
            if (attributes == null)
                return false;
            return attributes.DeviceSn.Status == Yme009ReadStatus.Ok
                && Yme009TextComparison.Equals(attributes.DeviceSn.Text, scannedSn);
        }

        private static bool IsConfirmedMismatch(Yme009InspectionDecision judged, string scannedSn)
        {
            var deviceSn = judged.Attributes.DeviceSn;
            return deviceSn.Status == Yme009ReadStatus.Ok
                && !Yme009TextComparison.Equals(deviceSn.Text, scannedSn);
        }

        private static Yme009InspectionDecision Copy(Yme009InspectionDecision judged, string result, string failReason)
        {
            return Yme009InspectionDecision.Judged(
                judged.Attributes,
                result,
                failReason,
                judged.DeviceSn,
                judged.SoftwareVersion,
                judged.ProductionTest,
                judged.AgingReboot,
                judged.AgingReset,
                judged.DeviceSnReason,
                judged.SoftwareVersionReason,
                judged.ProductionTestReason,
                judged.AgingRebootReason,
                judged.AgingResetReason,
                judged.ExpectedIdentity);
        }
    }
}
