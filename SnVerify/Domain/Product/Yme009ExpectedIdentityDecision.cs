using System;
using System.Collections.Generic;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 项目预期数据校验的独立结果。未启用、未执行和两个未决状态都不是 PASS 或 FAIL。
    /// </summary>
    public enum Yme009ExpectedIdentityDisposition
    {
        NotEnabled = 0,
        NotExecuted = 1,
        Passed = 2,
        Failed = 3,
        HistoricalPassUnresolved = 4,
        QueryUnresolved = 5
    }

    public sealed class Yme009ExpectedIdentityCheck
    {
        public const string NotEnabledText = "未启用";
        public const string NotExecutedText = "未执行";
        public const string PassText = "PASS";
        public const string FailText = "FAIL";
        public const string MissingDeviceDetail = "Excel 中不存在该设备";
        public const string UnavailableDetailPrefix = "无法取得有效值：";
        public const string MismatchDetailPrefix = "不匹配字段：";
        public const string NotEnabledDetail = "未启用";
        public const string NotExecutedDetail = "扫码 SN 与设备 SN 不一致，本轮未执行";
        public const string PassedDetail = "身份校验通过";
        public const string HistoricalPassUnresolvedDetail = "历史 PASS 已命中，身份结果未决";
        public const string QueryUnresolvedFallbackDetail = "查询异常";

        private Yme009ExpectedIdentityCheck(Yme009ExpectedIdentityDisposition disposition, string detail)
        {
            Disposition = disposition;
            Detail = detail ?? string.Empty;
        }

        public Yme009ExpectedIdentityDisposition Disposition { get; }

        public string Detail { get; }

        public string JudgmentText
        {
            get
            {
                switch (Disposition)
                {
                    case Yme009ExpectedIdentityDisposition.NotEnabled:
                        return NotEnabledText;
                    case Yme009ExpectedIdentityDisposition.NotExecuted:
                        return NotExecutedText;
                    case Yme009ExpectedIdentityDisposition.Passed:
                        return PassText;
                    case Yme009ExpectedIdentityDisposition.Failed:
                        return FailText;
                    default:
                        return string.Empty;
                }
            }
        }

        public string ReasonText
        {
            get { return Disposition == Yme009ExpectedIdentityDisposition.Failed ? Detail : string.Empty; }
        }

        public static Yme009ExpectedIdentityCheck NotEnabled()
        {
            return new Yme009ExpectedIdentityCheck(Yme009ExpectedIdentityDisposition.NotEnabled, NotEnabledDetail);
        }

        public static Yme009ExpectedIdentityCheck NotExecuted()
        {
            return new Yme009ExpectedIdentityCheck(Yme009ExpectedIdentityDisposition.NotExecuted, NotExecutedDetail);
        }

        public static Yme009ExpectedIdentityCheck Passed()
        {
            return new Yme009ExpectedIdentityCheck(Yme009ExpectedIdentityDisposition.Passed, PassedDetail);
        }

        public static Yme009ExpectedIdentityCheck Failed(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                throw new ArgumentException("身份校验失败必须保留说明。", nameof(detail));
            return new Yme009ExpectedIdentityCheck(Yme009ExpectedIdentityDisposition.Failed, detail.Trim());
        }

        /// <summary>
        /// OQ-016。历史 PASS 已命中且当前 Session 已启用预期数据时的内部未决状态。
        /// 它不是继续查询，也不是跳过查询后的未启用或未执行。
        /// </summary>
        public static Yme009ExpectedIdentityCheck UnresolvedHistoricalPass()
        {
            return new Yme009ExpectedIdentityCheck(
                Yme009ExpectedIdentityDisposition.HistoricalPassUnresolved,
                HistoricalPassUnresolvedDetail);
        }

        /// <summary>
        /// 预期数据查询异常的内部未决状态，不是身份 PASS 或 FAIL。
        /// </summary>
        public static Yme009ExpectedIdentityCheck UnresolvedQuery()
        {
            return UnresolvedQuery(QueryUnresolvedFallbackDetail);
        }

        public static Yme009ExpectedIdentityCheck UnresolvedQuery(string detail)
        {
            var text = string.IsNullOrWhiteSpace(detail) ? QueryUnresolvedFallbackDetail : detail.Trim();
            return new Yme009ExpectedIdentityCheck(Yme009ExpectedIdentityDisposition.QueryUnresolved, text);
        }
    }

    /// <summary>
    /// 根据已经取得的读取状态和预期记录确定身份结果。不访问数据库。
    /// </summary>
    public static class Yme009ExpectedIdentityGate
    {
        /// <summary>
        /// 当前 Session 已有预期数据集时使用。返回 null 表示四个实际字段有效，调用方可以按扫码 SN 查询设备行。
        /// 历史 PASS 命中时直接返回未决状态，不查询设备行，也不写成未启用、未执行、PASS 或 FAIL。
        /// </summary>
        public static Yme009ExpectedIdentityCheck TryResolveWithoutDeviceLookup(
            Yme009PassHistoryQueryDisposition history,
            string scannedSn,
            Yme009ParsedAttributes attributes)
        {
            if (attributes == null)
                throw new ArgumentNullException(nameof(attributes));

            if (history == Yme009PassHistoryQueryDisposition.HistoricalPass)
                return Yme009ExpectedIdentityCheck.UnresolvedHistoricalPass();

            if (!IsUsable(attributes.DeviceSn))
                return Yme009ExpectedIdentityCheck.Failed(UnavailableDetail(UnavailableFields(attributes)));

            if (!Yme009TextComparison.Equals(attributes.DeviceSn.Text, scannedSn))
                return Yme009ExpectedIdentityCheck.NotExecuted();

            var unavailable = UnavailableFields(attributes);
            if (unavailable.Count > 0)
                return Yme009ExpectedIdentityCheck.Failed(UnavailableDetail(unavailable));

            return null;
        }

        public static Yme009ExpectedIdentityCheck EvaluateLookup(
            Yme009ParsedAttributes attributes,
            Yme009ExpectedDeviceLookup lookup)
        {
            if (attributes == null)
                throw new ArgumentNullException(nameof(attributes));
            if (lookup == null)
                throw new ArgumentNullException(nameof(lookup));

            var unavailable = UnavailableFields(attributes);
            if (unavailable.Count > 0)
                return Yme009ExpectedIdentityCheck.Failed(UnavailableDetail(unavailable));

            if (!lookup.Found || lookup.Device == null)
                return Yme009ExpectedIdentityCheck.Failed(Yme009ExpectedIdentityCheck.MissingDeviceDetail);

            var expected = new Yme009ExpectedIdentity(
                lookup.Device.Sn,
                lookup.Device.WifiMac,
                lookup.Device.Imei1,
                lookup.Device.Imei2);
            var actual = new Yme009ExpectedIdentity(
                attributes.DeviceSn.Text,
                attributes.WifiMac.Text,
                attributes.Imei1.Text,
                attributes.Imei2.Text);
            var comparison = Yme009ExpectedIdentityComparison.Compare(expected, actual);
            if (comparison.Passed)
                return Yme009ExpectedIdentityCheck.Passed();

            return Yme009ExpectedIdentityCheck.Failed(DifferenceDetail(comparison));
        }

        private static bool IsUsable(Yme009ParsedField field)
        {
            return field != null
                && field.Status == Yme009ReadStatus.Ok
                && Yme009TextComparison.Normalize(field.Text).Length > 0;
        }

        private static List<string> UnavailableFields(Yme009ParsedAttributes attributes)
        {
            var names = new List<string>(4);
            AddIfUnavailable(names, attributes.DeviceSn, Yme009ExpectedFields.Sn);
            AddIfUnavailable(names, attributes.WifiMac, Yme009ExpectedFields.WifiMac);
            AddIfUnavailable(names, attributes.Imei1, Yme009ExpectedFields.Imei1);
            AddIfUnavailable(names, attributes.Imei2, Yme009ExpectedFields.Imei2);
            return names;
        }

        private static void AddIfUnavailable(List<string> names, Yme009ParsedField field, string name)
        {
            if (!IsUsable(field))
                names.Add(name);
        }

        private static string UnavailableDetail(List<string> names)
        {
            return Yme009ExpectedIdentityCheck.UnavailableDetailPrefix + string.Join("、", names);
        }

        private static string DifferenceDetail(Yme009ExpectedIdentityComparison comparison)
        {
            var unavailable = new List<string>();
            var mismatched = new List<string>();
            var differences = comparison.Differences;
            for (var i = 0; i < differences.Count; i++)
            {
                var difference = differences[i];
                if (difference.ActualValueMissing)
                    unavailable.Add(difference.FieldName);
                else
                    mismatched.Add(difference.FieldName);
            }

            var unavailableText = unavailable.Count == 0
                ? string.Empty
                : UnavailableDetail(unavailable);
            var mismatchedText = mismatched.Count == 0
                ? string.Empty
                : Yme009ExpectedIdentityCheck.MismatchDetailPrefix + string.Join("、", mismatched);
            if (unavailableText.Length == 0)
                return mismatchedText;
            if (mismatchedText.Length == 0)
                return unavailableText;
            return unavailableText + "；" + mismatchedText;
        }
    }

    /// <summary>
    /// 把身份结果附到已完成的五项和需求 A 判定上。不改写更高优先级的主失败原因，也不登记新的失败码。
    /// </summary>
    public static class Yme009ExpectedIdentityDecision
    {
        public static Yme009InspectionDecision Apply(
            Yme009InspectionDecision judged,
            Yme009ExpectedIdentityCheck check)
        {
            if (judged == null)
                throw new ArgumentNullException(nameof(judged));
            if (check == null)
                throw new ArgumentNullException(nameof(check));
            if (judged.IsSessionParameterInvalid)
                return judged;

            var result = judged.Result;
            if (check.Disposition == Yme009ExpectedIdentityDisposition.Failed
                && result == Yme009ResultClassifier.PassResult)
            {
                result = Yme009ResultClassifier.FailResult;
            }
            else if (check.Disposition == Yme009ExpectedIdentityDisposition.QueryUnresolved
                && result == Yme009ResultClassifier.PassResult)
            {
                result = Yme009ResultClassifier.UnresolvedResult;
            }

            return judged.WithExpectedIdentity(result, judged.FailReason, check);
        }
    }
}
