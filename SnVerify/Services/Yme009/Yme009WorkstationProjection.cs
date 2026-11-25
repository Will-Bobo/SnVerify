using SnVerify.Domain.Product;

namespace SnVerify.Services.Yme009
{
    public enum Yme009RoundPhase
    {
        Idle = 0,
        Running = 1,
        Completed = 2,
        AccessStopped = 3,
        RoundFault = 4
    }

    public enum Yme009OperatorStage
    {
        Connecting = 0,
        WaitingForDevice = 1,
        Reading = 2
    }

    public enum Yme009HeadlineKind
    {
        Neutral = 0,
        Running = 1,
        Pass = 2,
        Fail = 3,
        Importing = 4,
        ImportComplete = 5,
        ReadyToTest = 6,
        Unresolved = 7,
        Exception = 8
    }

    /// <summary>
    /// 把已经算好的轮次结果抄成工位展示。不重新判定。
    /// </summary>
    public sealed class Yme009WorkstationSnapshot
    {
        public Yme009RoundPhase Phase { get; private set; }

        public string ProcessPrompt { get; private set; }

        public string Headline { get; private set; }

        public Yme009HeadlineKind HeadlineKind { get; private set; }

        public bool HasOverallResult { get; private set; }

        public string OverallText { get; private set; }

        public string PrimaryReason { get; private set; }

        public string SaveStatusText { get; private set; }

        public string CloseNotice { get; private set; }

        public string ScannedSn { get; private set; }

        public string DeviceSnText { get; private set; }

        public string ActualVersionText { get; private set; }

        public string SnJudgment { get; private set; }

        public string SnReason { get; private set; }

        public string SnRaw { get; private set; }

        public string VersionJudgment { get; private set; }

        public string VersionReason { get; private set; }

        public string CitJudgment { get; private set; }

        public string CitReason { get; private set; }

        public string CitRaw { get; private set; }

        public string RebootJudgment { get; private set; }

        public string RebootReason { get; private set; }

        public string RebootRaw { get; private set; }

        public string ResetJudgment { get; private set; }

        public string ResetReason { get; private set; }

        public string ResetRaw { get; private set; }

        public string ExpectedIdentityJudgment { get; private set; }

        public string ExpectedIdentityReason { get; private set; }

        public string Imei1Text { get; private set; }

        public string Imei2Text { get; private set; }

        public string WifiMacText { get; private set; }

        public static Yme009WorkstationSnapshot IdleWaiting()
        {
            var snapshot = Blank(Yme009RoundPhase.Idle, Yme009WorkstationText.WaitingForScan);
            snapshot.Headline = Yme009WorkstationText.WaitingForScan;
            return snapshot;
        }

        public static Yme009WorkstationSnapshot IdleCleared()
        {
            return Blank(Yme009RoundPhase.Idle, string.Empty);
        }

        public static Yme009WorkstationSnapshot Fault(string scannedSn)
        {
            var snapshot = Blank(Yme009RoundPhase.RoundFault, Yme009WorkstationText.RoundIncomplete);
            snapshot.ScannedSn = scannedSn ?? string.Empty;
            snapshot.Headline = snapshot.ProcessPrompt;
            snapshot.HeadlineKind = Yme009HeadlineKind.Exception;
            return snapshot;
        }

        public static Yme009WorkstationSnapshot Running(string scannedSn, string prompt)
        {
            var snapshot = Blank(Yme009RoundPhase.Running, prompt ?? Yme009WorkstationText.Connecting);
            snapshot.ScannedSn = scannedSn ?? string.Empty;
            snapshot.Headline = snapshot.ProcessPrompt;
            snapshot.HeadlineKind = Yme009HeadlineKind.Running;
            return snapshot;
        }

        public static Yme009WorkstationSnapshot FromResult(
            Yme009RoundResult result,
            string scannedSn)
        {
            var decision = result == null ? null : result.Decision;
            if (decision != null && decision.IsSessionParameterInvalid)
                decision = null;

            var formal = decision != null
                && (decision.Result == Yme009ResultClassifier.PassResult
                    || decision.Result == Yme009ResultClassifier.FailResult
                    || decision.Result == Yme009ResultClassifier.UnresolvedResult);
            Yme009RoundPhase phase;
            if (formal)
                phase = Yme009RoundPhase.Completed;
            else if (result == null || result.Access == null || result.Access.Stop != Yme009AccessStop.AccessSucceeded)
                phase = Yme009RoundPhase.AccessStopped;
            else
                phase = Yme009RoundPhase.RoundFault;

            var prompt = string.Empty;
            if (phase == Yme009RoundPhase.AccessStopped)
                prompt = AccessPrompt(result == null || result.Access == null
                    ? Yme009AccessStop.Unexpected
                    : result.Access.Stop);
            else if (phase == Yme009RoundPhase.RoundFault)
                prompt = Yme009WorkstationText.RoundIncomplete;
            else if (decision.Bucket == Yme009StatisticBucket.ReadException)
                prompt = Yme009WorkstationText.ReadException;

            var snapshot = Blank(phase, prompt);
            snapshot.ScannedSn = scannedSn ?? string.Empty;
            if (!formal)
            {
                snapshot.Headline = prompt;
                snapshot.HeadlineKind = Yme009HeadlineKind.Exception;
                return snapshot;
            }

            var pass = decision.Result == Yme009ResultClassifier.PassResult;
            var unresolved = decision.Result == Yme009ResultClassifier.UnresolvedResult;
            snapshot.HasOverallResult = true;
            if (unresolved)
            {
                snapshot.OverallText = Yme009ResultClassifier.UnresolvedResult;
                snapshot.HeadlineKind = Yme009HeadlineKind.Unresolved;
            }
            else if (pass)
            {
                snapshot.OverallText = Yme009ResultClassifier.PassResult;
                snapshot.HeadlineKind = Yme009HeadlineKind.Pass;
            }
            else
            {
                snapshot.OverallText = Yme009ResultClassifier.FailResult;
                snapshot.HeadlineKind = Yme009HeadlineKind.Fail;
            }

            snapshot.Headline = snapshot.OverallText;
            if (decision.Bucket == Yme009StatisticBucket.ReadException)
            {
                snapshot.HeadlineKind = Yme009HeadlineKind.Exception;
                snapshot.Headline = Yme009WorkstationText.ReadException;
            }

            snapshot.PrimaryReason = pass || unresolved ? string.Empty : FailReasonTextResolverSafe(decision.FailReason);
            snapshot.SaveStatusText = result.RecordSaved && result.RecordId > 0
                ? Yme009WorkstationText.Saved
                : Yme009WorkstationText.RecordNotSaved;
            snapshot.CloseNotice = result.Close != null && result.Close.Disposition == Yme009CloseDisposition.Failed
                ? Yme009WorkstationText.CloseFailed
                : string.Empty;
            FillJudged(snapshot, decision);
            return snapshot;
        }

        public static string StagePrompt(Yme009OperatorStage stage)
        {
            if (stage == Yme009OperatorStage.WaitingForDevice)
                return Yme009WorkstationText.WaitingForDevice;
            if (stage == Yme009OperatorStage.Reading)
                return Yme009WorkstationText.Reading;
            return Yme009WorkstationText.Connecting;
        }

        public static string AccessPrompt(Yme009AccessStop stop)
        {
            if (stop == Yme009AccessStop.ReadyFailed)
                return Yme009WorkstationText.NotReady;
            return Yme009WorkstationText.AccessFailed;
        }

        private static Yme009WorkstationSnapshot Blank(Yme009RoundPhase phase, string prompt)
        {
            return new Yme009WorkstationSnapshot
            {
                Phase = phase,
                ProcessPrompt = prompt ?? string.Empty,
                Headline = string.Empty,
                HeadlineKind = Yme009HeadlineKind.Neutral,
                HasOverallResult = false,
                OverallText = string.Empty,
                PrimaryReason = string.Empty,
                SaveStatusText = Yme009WorkstationText.NotSavedRecord,
                CloseNotice = string.Empty,
                ScannedSn = string.Empty,
                DeviceSnText = Yme009WorkstationText.NotRead,
                ActualVersionText = Yme009WorkstationText.NotRead,
                SnJudgment = Yme009WorkstationText.WaitingItem,
                SnReason = string.Empty,
                SnRaw = string.Empty,
                VersionJudgment = Yme009WorkstationText.WaitingItem,
                VersionReason = string.Empty,
                CitJudgment = Yme009WorkstationText.WaitingItem,
                CitReason = string.Empty,
                CitRaw = string.Empty,
                RebootJudgment = Yme009WorkstationText.WaitingItem,
                RebootReason = string.Empty,
                RebootRaw = string.Empty,
                ResetJudgment = Yme009WorkstationText.WaitingItem,
                ResetReason = string.Empty,
                ResetRaw = string.Empty,
                ExpectedIdentityJudgment = Yme009WorkstationText.WaitingItem,
                ExpectedIdentityReason = string.Empty,
                Imei1Text = Yme009WorkstationText.NotRead,
                Imei2Text = Yme009WorkstationText.NotRead,
                WifiMacText = Yme009WorkstationText.NotRead
            };
        }

        private static void FillJudged(Yme009WorkstationSnapshot snapshot, Yme009InspectionDecision decision)
        {
            var attributes = decision.Attributes;
            snapshot.DeviceSnText = Show(attributes == null ? null : attributes.DeviceSn);
            snapshot.ActualVersionText = Show(attributes == null ? null : attributes.SoftwareVersion);
            snapshot.Imei1Text = Show(attributes == null ? null : attributes.Imei1);
            snapshot.Imei2Text = Show(attributes == null ? null : attributes.Imei2);
            snapshot.WifiMacText = Show(attributes == null ? null : attributes.WifiMac);
            snapshot.SnJudgment = Item(decision.DeviceSn);
            snapshot.SnReason = Reason(decision.DeviceSn, decision.DeviceSnReason);
            snapshot.SnRaw = Show(attributes == null ? null : attributes.DeviceSn);
            snapshot.VersionJudgment = Item(decision.SoftwareVersion);
            snapshot.VersionReason = Reason(decision.SoftwareVersion, decision.SoftwareVersionReason);
            snapshot.CitJudgment = Item(decision.ProductionTest);
            snapshot.CitReason = Reason(decision.ProductionTest, decision.ProductionTestReason);
            snapshot.CitRaw = Show(attributes == null ? null : attributes.ProductionTest);
            snapshot.RebootJudgment = Item(decision.AgingReboot);
            snapshot.RebootReason = Reason(decision.AgingReboot, decision.AgingRebootReason);
            snapshot.RebootRaw = Show(attributes == null ? null : attributes.AgingReboot);
            snapshot.ResetJudgment = Item(decision.AgingReset);
            snapshot.ResetReason = Reason(decision.AgingReset, decision.AgingResetReason);
            snapshot.ResetRaw = Show(attributes == null ? null : attributes.AgingReset);
            var identity = decision.ExpectedIdentity;
            snapshot.ExpectedIdentityJudgment = identity == null ? string.Empty : identity.JudgmentText;
            snapshot.ExpectedIdentityReason = identity == null ? string.Empty : identity.ReasonText;
        }

        private static string Item(Yme009ItemJudgment judgment)
        {
            return judgment == Yme009ItemJudgment.Pass ? "PASS" : "FAIL";
        }

        private static string Reason(Yme009ItemJudgment judgment, string reason)
        {
            if (judgment != Yme009ItemJudgment.Fail)
                return string.Empty;
            return reason ?? string.Empty;
        }

        private static string Show(Yme009ParsedField field)
        {
            if (field == null || string.IsNullOrEmpty(field.Text))
                return Yme009WorkstationText.NotRead;
            return field.Text;
        }

        private static string FailReasonTextResolverSafe(string failReason)
        {
            return SnVerify.Services.Rules.FailReasonTextResolver.Resolve(failReason);
        }
    }

    public static class Yme009WorkstationText
    {
        public const string WaitingForScan = "等待扫码";
        public const string PassDetail = "测试通过";
        public const string UnresolvedDetail = "身份校验未完成";
        public const string Connecting = "正在接入设备";
        public const string WaitingForDevice = "正在等待设备接入";
        public const string Reading = "正在读取检测数据";
        public const string BusyScan = "当前设备正在检测，请等待本轮检测完成。";
        public const string AccessFailed = "设备接入失败，请检查设备连接";
        public const string NotReady = "设备连接未就绪，请检查设备";
        public const string ReadException = "检测数据读取异常";
        public const string RoundIncomplete = "检测未完成，记录未保存";
        public const string NotRead = "未读取";
        public const string NotSavedRecord = "未保存记录";
        public const string Saved = "已保存";
        public const string SaveFailed = "保存失败";
        public const string RecordNotSaved = "记录未保存";
        public const string CloseFailed = "收尾异常，本次检测结果不变";
        public const string WaitingItem = "等待";
    }

    public sealed class Yme009StatusPresentation
    {
        public const string NeutralColor = "#808080";
        public const string RunningColor = "#0D47A1";
        public const string ImportCompleteColor = "#00695C";
        public const string PassColor = "#1B5E20";
        public const string FailColor = "#B71C1C";
        public const string AttentionColor = "#E65100";
        private const int DetailLimit = 36;

        public string Title { get; private set; }
        public string Detail { get; private set; }
        public string Background { get; private set; }
        public string ScanSideText { get; private set; }

        public static Yme009StatusPresentation Map(
            Yme009HeadlineKind kind,
            string headline,
            string processPrompt,
            string primaryReason,
            string identityReason,
            string suppliedDetail)
        {
            var display = new Yme009StatusPresentation
            {
                Background = NeutralColor,
                Title = string.Empty,
                Detail = string.Empty,
                ScanSideText = string.Empty
            };
            switch (kind)
            {
                case Yme009HeadlineKind.Pass:
                    display.Title = Yme009ResultClassifier.PassResult;
                    display.Detail = Yme009WorkstationText.PassDetail;
                    display.Background = PassColor;
                    break;
                case Yme009HeadlineKind.Fail:
                    display.Title = Yme009ResultClassifier.FailResult;
                    display.Detail = Limit(primaryReason);
                    display.Background = FailColor;
                    break;
                case Yme009HeadlineKind.Running:
                    display.Title = "检验中...";
                    display.Detail = FirstText(processPrompt, headline);
                    display.Background = RunningColor;
                    display.ScanSideText = display.Detail;
                    break;
                case Yme009HeadlineKind.Importing:
                    display.Title = Yme009ExpectedOperatorText.ImportingTitle;
                    display.Detail = Yme009ExpectedOperatorText.ImportingDetail;
                    display.Background = RunningColor;
                    break;
                case Yme009HeadlineKind.ImportComplete:
                    display.Title = Yme009ExpectedOperatorText.ImportDoneTitle;
                    display.Detail = suppliedDetail ?? string.Empty;
                    display.Background = ImportCompleteColor;
                    break;
                case Yme009HeadlineKind.ReadyToTest:
                    display.Title = Yme009ExpectedOperatorText.ReadyTitle;
                    display.Detail = "WAITING";
                    display.Background = NeutralColor;
                    break;
                case Yme009HeadlineKind.Unresolved:
                    display.Title = Yme009ResultClassifier.UnresolvedResult;
                    display.Detail = Limit(FirstText(primaryReason, identityReason, Yme009WorkstationText.UnresolvedDetail));
                    display.Background = AttentionColor;
                    break;
                case Yme009HeadlineKind.Exception:
                    ApplyException(display, headline, processPrompt, primaryReason);
                    break;
                default:
                    display.Title = Yme009WorkstationText.WaitingForScan;
                    display.Detail = "WAITING";
                    display.Background = NeutralColor;
                    break;
            }

            return display;
        }

        private static void ApplyException(
            Yme009StatusPresentation display,
            string headline,
            string processPrompt,
            string primaryReason)
        {
            var source = FirstText(headline, processPrompt);
            string title;
            string detail;
            SplitPrompt(source, out title, out detail);
            if (!string.IsNullOrWhiteSpace(primaryReason) && primaryReason != title)
                detail = primaryReason;
            display.Title = title;
            display.Detail = Limit(detail);
            display.Background = AttentionColor;
        }

        private static void SplitPrompt(string source, out string title, out string detail)
        {
            title = source ?? string.Empty;
            detail = string.Empty;
            var index = title.IndexOf('，');
            if (index <= 0 || index >= title.Length - 1)
                return;
            detail = title.Substring(index + 1);
            title = title.Substring(0, index);
        }

        private static string FirstText(params string[] values)
        {
            if (values == null)
                return string.Empty;
            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i];
            }

            return string.Empty;
        }

        private static string Limit(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= DetailLimit)
                return value ?? string.Empty;
            return value.Substring(0, DetailLimit) + "…";
        }
    }
}
