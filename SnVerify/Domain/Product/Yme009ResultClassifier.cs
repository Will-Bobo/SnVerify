namespace SnVerify.Domain.Product
{
    public enum Yme009StatisticBucket
    {
        Pass = 0,
        BusinessFail = 1,
        ReadException = 2,
        Unregistered = 3
    }

    /// <summary>
    /// 只根据 Result 和主 FailReason 分类。不扫描分项结论，避免把诊断用的业务 Fail 计入业务 FAIL。
    /// </summary>
    public static class Yme009ResultClassifier
    {
        public const string PassResult = "PASS";
        public const string FailResult = "FAIL";

        public static Yme009StatisticBucket Classify(string result, string failReason)
        {
            if (result == PassResult && string.IsNullOrEmpty(failReason))
                return Yme009StatisticBucket.Pass;

            if (result == FailResult && Yme009FailReasonCodes.IsBusiness(failReason))
                return Yme009StatisticBucket.BusinessFail;

            if (result == FailResult && Yme009FailReasonCodes.IsReadException(failReason))
                return Yme009StatisticBucket.ReadException;

            return Yme009StatisticBucket.Unregistered;
        }
    }
}
