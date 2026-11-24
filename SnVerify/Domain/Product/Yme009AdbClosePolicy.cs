namespace SnVerify.Domain.Product
{
    public enum Yme009AccessStop
    {
        WaitTimedOut = 0,
        AtNotSent = 1,
        AtUncertain = 2,
        ReadyFailed = 3,
        AssociationFailed = 4,
        AccessSucceeded = 5,
        StoppedBeforeAt = 6,
        StoppedAfterAt = 7,
        Unexpected = 8
    }

    public enum Yme009CloseDisposition
    {
        NotAttempted = 0,
        Sent = 1,
        Skipped = 2,
        Failed = 3
    }

    /// <summary>
    /// 只有 AT 已确认成功，并且仍有临时或正式身份时才尝试关闭。
    /// 等待超时、AT 未发送、AT 结果不确定不补发关闭。
    /// </summary>
    public static class Yme009AdbClosePolicy
    {
        public static bool ShouldAttemptClose(Yme009AccessStop stop, bool atConfirmed, bool identityAvailable)
        {
            if (!atConfirmed || !identityAvailable)
                return false;

            return stop != Yme009AccessStop.WaitTimedOut
                && stop != Yme009AccessStop.AtNotSent
                && stop != Yme009AccessStop.AtUncertain
                && stop != Yme009AccessStop.StoppedBeforeAt;
        }

        public static bool PreferFormalContext(bool formalAvailable)
        {
            return formalAvailable;
        }
    }
}
