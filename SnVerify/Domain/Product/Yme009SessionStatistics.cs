using System.Collections.Generic;
using System.Globalization;
using SnVerify.Domain.Models;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// 按已保存记录的 Result 和主 FailReason 汇总当前会话统计。不访问数据库，不解释分项结论。
    /// </summary>
    public sealed class Yme009SessionStatistics
    {
        public static readonly Yme009SessionStatistics Empty = new Yme009SessionStatistics(0, 0, 0, string.Empty, 0);

        public Yme009SessionStatistics(int passCount, int businessFailCount, int readExceptionCount, string passRateText, int unresolvedCount = 0)
        {
            PassCount = passCount;
            BusinessFailCount = businessFailCount;
            ReadExceptionCount = readExceptionCount;
            PassRateText = passRateText ?? string.Empty;
            UnresolvedCount = unresolvedCount;
        }

        public int PassCount { get; }

        public int BusinessFailCount { get; }

        public int ReadExceptionCount { get; }

        public string PassRateText { get; }

        public int UnresolvedCount { get; }

        public static Yme009SessionStatistics Summarize(IEnumerable<TestRecord> records)
        {
            var passCount = 0;
            var businessFailCount = 0;
            var readExceptionCount = 0;
            var unresolvedCount = 0;
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record == null)
                        continue;

                    switch (Yme009ResultClassifier.Classify(record.Result, record.FailReason))
                    {
                        case Yme009StatisticBucket.Pass:
                            passCount++;
                            break;
                        case Yme009StatisticBucket.BusinessFail:
                            businessFailCount++;
                            break;
                        case Yme009StatisticBucket.ReadException:
                            readExceptionCount++;
                            break;
                        case Yme009StatisticBucket.Unresolved:
                            unresolvedCount++;
                            break;
                    }
                }
            }

            var denominator = passCount + businessFailCount;
            var passRateText = denominator == 0
                ? string.Empty
                : (passCount * 100d / denominator).ToString("0.00", CultureInfo.InvariantCulture) + "%";
            return new Yme009SessionStatistics(passCount, businessFailCount, readExceptionCount, passRateText, unresolvedCount);
        }
    }
}
