using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Yme009
{
    public sealed class Yme009RoundResult
    {
        public Yme009RoundResult(
            Yme009AccessResult access,
            Yme009CloseAttempt close,
            bool cleanedUp,
            string operatorText,
            bool recordSaved = false,
            int recordId = 0,
            Yme009InspectionDecision decision = null)
        {
            Access = access;
            Close = close ?? Yme009CloseAttempt.NotAttempted();
            CleanedUp = cleanedUp;
            OperatorText = operatorText ?? string.Empty;
            RecordSaved = recordSaved;
            RecordId = recordId;
            Decision = decision;
        }

        public Yme009AccessResult Access { get; }

        public Yme009CloseAttempt Close { get; }

        public bool CleanedUp { get; }

        public string OperatorText { get; }

        public bool RecordSaved { get; }

        public int RecordId { get; }

        public Yme009InspectionDecision Decision { get; }
    }

    public interface IYme009RoundCoordinator
    {
        void UseInspection(Yme009InspectionRequest request);

        void UseProgress(IProgress<Yme009OperatorStage> progress);

        Task<Yme009RoundResult> RunAsync(string scannedSn, CancellationToken cancellationToken);
    }

    /// <summary>
    /// YME009 专属轮次。设备接入成功后读取八项属性、复用综合判定并保存新记录，然后关闭 ADB 并清理。
    /// </summary>
    public sealed class Yme009RoundCoordinator : IYme009RoundCoordinator
    {
        private readonly Yme009DeviceAccessCoordinator _access;
        private readonly ILoggingService _logging;
        private readonly Yme009RoundLogScope _scope;
        private readonly IYme009PropertyReader _reader;
        private readonly IStorageService _storage;
        private Yme009InspectionRequest _inspection;
        private IProgress<Yme009OperatorStage> _progress;

        public Yme009RoundCoordinator(
            Yme009DeviceAccessCoordinator access,
            ILoggingService logging,
            Yme009RoundLogScope scope = null,
            IYme009PropertyReader reader = null,
            IStorageService storage = null)
        {
            _access = access ?? throw new ArgumentNullException(nameof(access));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _scope = scope;
            _reader = reader;
            _storage = storage;
        }

        public void UseInspection(Yme009InspectionRequest request)
        {
            _inspection = request;
        }

        public void UseProgress(IProgress<Yme009OperatorStage> progress)
        {
            _progress = progress;
        }

        public async Task<Yme009RoundResult> RunAsync(string scannedSn, CancellationToken cancellationToken)
        {
            var inspection = _inspection;
            _inspection = null;
            if (_scope != null)
                _scope.Begin(scannedSn);

            var roundWatch = Stopwatch.StartNew();
            _logging.LogInfo(Yme009AccessLog.Line(_scope, "设备接入开始"));
            _access.StageChanged = stage => _progress?.Report(stage);
            try
            {
            using (var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Report(Yme009OperatorStage.Connecting);
                var access = await _access.AccessAsync(work.Token).ConfigureAwait(false);
                var formalContextDelivered = access.FormalContextDelivered;
                var outcome = new InspectionOutcome();
                if (inspection != null && formalContextDelivered)
                {
                    try
                    {
                        outcome = await InspectAsync(scannedSn, inspection, access, work.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logging.LogError(Yme009AccessLog.Line(_scope, "检测未完成，不写入检测记录。SN=" + (scannedSn ?? string.Empty)), ex);
                        outcome = new InspectionOutcome { Note = "检测未完成，记录未保存" };
                    }
                }

                var close = Yme009CloseAttempt.NotAttempted();
                try
                {
                    var identityAvailable = access.Connection != null
                        && !access.Connection.IsRevoked
                        && access.Connection.IdentityKey.Length > 0;
                    var preferFormal = access.Connection != null
                        && Yme009AdbClosePolicy.PreferFormalContext(access.Connection.Stage == Yme009ConnectionStage.Formal);
                    if (Yme009AdbClosePolicy.ShouldAttemptClose(access.Stop, access.AtConfirmed, identityAvailable))
                    {
                        _logging.LogInfo(preferFormal
                            ? "YME009 使用正式连接上下文尽力关闭 ADB。"
                            : "YME009 使用临时设备身份上下文尽力关闭 ADB。");
                        close = await _access.TryCloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        _logging.LogInfo(Yme009AccessLog.Line(
                            _scope,
                            "关闭策略未进入发送。停止=" + access.Stop + "，AT已确认=" + access.AtConfirmed + "，身份可用=" + identityAvailable));
                    }
                }
                catch (Exception ex)
                {
                    _logging.LogError(Yme009AccessLog.Line(_scope, "关闭结果=Failed。关闭尝试抛出异常，接入结果保持不变。"), ex);
                    close = new Yme009CloseAttempt(Yme009CloseDisposition.Failed, Yme009AtResult.Uncertain, ex.Message);
                }
                finally
                {
                    _access.Release();
                }

                var text = DescribeRound(access, formalContextDelivered, inspection, scannedSn, outcome);
                _logging.LogInfo("YME009 设备接入结束: SN=" + (scannedSn ?? string.Empty) + "，结果=" + text.Replace(Environment.NewLine, " | "));
                _logging.LogInfo(Yme009AccessLog.Line(_scope, "本轮结束，结果=" + text.Replace(Environment.NewLine, " | ") + "，耗时=" + roundWatch.ElapsedMilliseconds + "ms"));
                return new Yme009RoundResult(access, close, true, text, outcome.Saved, outcome.RecordId, outcome.Decision);
            }
            }
            finally
            {
                _access.StageChanged = null;
                _progress = null;
            }
        }

        private void Report(Yme009OperatorStage stage)
        {
            var progress = _progress;
            if (progress != null)
                progress.Report(stage);
        }

        private async Task<InspectionOutcome> InspectAsync(
            string scannedSn,
            Yme009InspectionRequest inspection,
            Yme009AccessResult access,
            CancellationToken cancellationToken)
        {
            if (inspection.SessionId <= 0)
            {
                _logging.LogError(Yme009AccessLog.Line(_scope, "会话无效，未调用保存。SN=" + (scannedSn ?? string.Empty)), null);
                return new InspectionOutcome { Note = "会话无效，记录未保存" };
            }

            if (string.IsNullOrWhiteSpace(inspection.TargetSoftwareVersion))
            {
                _logging.LogError(Yme009AccessLog.Line(_scope, "目标软件版本不能为空，未读取属性，未调用保存。SN=" + (scannedSn ?? string.Empty)), null);
                return new InspectionOutcome { Note = "目标软件版本不能为空，记录未保存" };
            }

            if (_reader == null || _storage == null)
            {
                _logging.LogError(Yme009AccessLog.Line(_scope, "检测依赖未接线，未调用保存。SN=" + (scannedSn ?? string.Empty)), null);
                return new InspectionOutcome { Note = "检测未完成，记录未保存" };
            }

            Report(Yme009OperatorStage.Reading);
            var raw = await _reader.ReadAsync(access.Connection, cancellationToken).ConfigureAwait(false);
            var decision = Yme009InspectionJudge.Judge(
                scannedSn,
                inspection.TargetSoftwareVersion,
                Yme009AttributeParser.Parse(raw));
            if (decision.IsSessionParameterInvalid)
                return new InspectionOutcome { Note = "目标软件版本不能为空，记录未保存" };

            var record = Yme009TestRecordMapper.Create(
                inspection.SessionId,
                scannedSn,
                inspection.TargetSoftwareVersion.Trim(),
                decision,
                DateTime.Now);
            try
            {
                await _storage.SaveTestRecordAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logging.LogError(
                    Yme009AccessLog.Line(_scope, "记录保存失败。SessionId=" + inspection.SessionId + "，SN=" + (scannedSn ?? string.Empty) + "，判定=" + decision.Result + "，FailReason=" + (decision.FailReason ?? string.Empty)),
                    ex);
                return new InspectionOutcome { Decision = decision, Note = "记录未保存" };
            }

            if (record.Id <= 0)
            {
                _logging.LogError(Yme009AccessLog.Line(_scope, "保存返回后记录主键无效，按未保存处理。SessionId=" + inspection.SessionId + "，SN=" + (scannedSn ?? string.Empty)), null);
                return new InspectionOutcome { Decision = decision, Note = "记录未保存" };
            }

            _logging.LogInfo(Yme009AccessLog.Line(
                _scope,
                "记录已保存。SessionId=" + inspection.SessionId + "，TestRecordId=" + record.Id + "，SN=" + (scannedSn ?? string.Empty) + "，判定=" + decision.Result));
            return new InspectionOutcome { Decision = decision, Saved = true, RecordId = record.Id };
        }

        private static string DescribeRound(
            Yme009AccessResult access,
            bool formalContextDelivered,
            Yme009InspectionRequest inspection,
            string scannedSn,
            InspectionOutcome outcome)
        {
            var accessText = Describe(access.Stop);
            if (access.Stop == Yme009AccessStop.AssociationFailed && access.FailureDetail.Length > 0)
                accessText = accessText + "：" + access.FailureDetail;
            if (inspection == null)
                return accessText;
            if (!formalContextDelivered)
                return accessText + "，未保存检测记录";
            return Yme009InspectionSummary.Describe(
                scannedSn,
                inspection.TargetSoftwareVersion,
                outcome.Decision,
                outcome.Saved,
                outcome.RecordId,
                outcome.Note);
        }

        private static string Describe(Yme009AccessStop stop)
        {
            switch (stop)
            {
                case Yme009AccessStop.WaitTimedOut:
                    return "30 秒内未确认唯一设备";
                case Yme009AccessStop.AtNotSent:
                    return "AT 未发送";
                case Yme009AccessStop.AtUncertain:
                    return "AT 结果不确定";
                case Yme009AccessStop.ReadyFailed:
                    return "ADB 未就绪";
                case Yme009AccessStop.AssociationFailed:
                    return "未能按单设备规则选定唯一 ADB";
                case Yme009AccessStop.AccessSucceeded:
                    return "设备接入完成";
                case Yme009AccessStop.StoppedBeforeAt:
                    return "接入已停止";
                case Yme009AccessStop.StoppedAfterAt:
                    return "接入已停止";
                default:
                    return "接入异常";
            }
        }

        private sealed class InspectionOutcome
        {
            public Yme009InspectionDecision Decision { get; set; }

            public bool Saved { get; set; }

            public int RecordId { get; set; }

            public string Note { get; set; }
        }
    }
}
