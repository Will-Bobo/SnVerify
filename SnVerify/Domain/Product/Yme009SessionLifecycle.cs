namespace SnVerify.Domain.Product
{
    /// <summary>
    /// YME009 检测任务的持久化状态。只使用这两个明确值。
    /// 历史 Session 的 NULL 不是进行中，也不是已结束。
    /// </summary>
    public static class Yme009SessionStatus
    {
        public const string Running = "Running";

        public const string Completed = "Completed";
    }

    /// <summary>
    /// 一条可恢复的 YME009 进行中任务。身份以内部 SessionId 和 SessionName 为准。
    /// </summary>
    public sealed class Yme009RunningSession
    {
        public Yme009RunningSession(
            int sessionId,
            string sessionName,
            string orderName,
            string projectName,
            string productCode,
            string expectedFileName,
            int recordCount)
        {
            SessionId = sessionId;
            SessionName = sessionName ?? string.Empty;
            OrderName = orderName ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            ProductCode = productCode ?? string.Empty;
            ExpectedFileName = expectedFileName ?? string.Empty;
            RecordCount = recordCount;
        }

        public int SessionId { get; }

        public string SessionName { get; }

        public string OrderName { get; }

        public string ProjectName { get; }

        public string ProductCode { get; }

        public string ExpectedFileName { get; }

        public int RecordCount { get; }
    }
}
