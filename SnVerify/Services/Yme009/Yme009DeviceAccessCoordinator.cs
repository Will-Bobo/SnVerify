using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Yme009
{
    public sealed class Yme009AccessResult
    {
        public Yme009AccessResult(
            Yme009AccessStop stop,
            bool atConfirmed,
            Yme009DeviceConnection connection,
            string failureDetail)
        {
            Stop = stop;
            AtConfirmed = atConfirmed;
            Connection = connection;
            FailureDetail = failureDetail ?? string.Empty;
        }

        public Yme009AccessStop Stop { get; }

        public bool AtConfirmed { get; }

        public Yme009DeviceConnection Connection { get; }

        public string FailureDetail { get; }

        public bool FormalContextDelivered
        {
            get { return Connection != null && Connection.AllowsBusinessUse; }
        }
    }

    public sealed class Yme009CloseAttempt
    {
        public Yme009CloseAttempt(Yme009CloseDisposition disposition, Yme009AtResult atResult, string detail)
        {
            Disposition = disposition;
            AtResult = atResult;
            Detail = detail ?? string.Empty;
        }

        public Yme009CloseDisposition Disposition { get; }

        public Yme009AtResult AtResult { get; }

        public string Detail { get; }

        public static Yme009CloseAttempt NotAttempted()
        {
            return new Yme009CloseAttempt(Yme009CloseDisposition.NotAttempted, Yme009AtResult.NotSent, string.Empty);
        }
    }

    public sealed class Yme009DeviceAccessCoordinator
    {
        private readonly IYme009CdcDiscovery _discovery;
        private readonly Yme009AtAdbSwitch _atSwitch;
        private readonly IYme009AdbDeviceQuery _adbQuery;
        private readonly ILoggingService _logging;
        private readonly Yme009AccessTiming _timing;
        private readonly IYme009Clock _clock;
        private readonly IYme009AsyncDelay _delay;
        private readonly Yme009RoundLogScope _scope;
        private Yme009DeviceConnection _connection;
        private bool _atConfirmed;
        private bool _observing;

        public Yme009DeviceAccessCoordinator(
            IYme009CdcDiscovery discovery,
            Yme009AtAdbSwitch atSwitch,
            IYme009AdbDeviceQuery adbQuery,
            ILoggingService logging,
            Yme009AccessTiming timing,
            IYme009Clock clock,
            IYme009AsyncDelay delay,
            Yme009RoundLogScope scope = null)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _atSwitch = atSwitch ?? throw new ArgumentNullException(nameof(atSwitch));
            _adbQuery = adbQuery ?? throw new ArgumentNullException(nameof(adbQuery));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _timing = timing ?? Yme009AccessTiming.Default;
            _clock = clock ?? new Yme009SystemClock();
            _delay = delay ?? new Yme009SystemDelay();
            _scope = scope;
        }

        internal Action<Yme009OperatorStage> StageChanged { get; set; }

        public async Task<Yme009AccessResult> AccessAsync(CancellationToken workToken)
        {
            _connection = null;
            _atConfirmed = false;
            _observing = true;
            try
            {
                var confirmed = await ObserveUntilConfirmedAsync(workToken).ConfigureAwait(false);
                if (confirmed == null)
                    return new Yme009AccessResult(Yme009AccessStop.WaitTimedOut, false, null, "CDC 门禁未通过");

                _connection = Yme009DeviceConnection.Temporary(Guid.NewGuid().ToString("N"), confirmed);
                var at = await _atSwitch.EnableAsync(confirmed, workToken).ConfigureAwait(false);
                if (at != Yme009AtResult.Succeeded)
                {
                    var stop = at == Yme009AtResult.NotSent
                        ? Yme009AccessStop.AtNotSent
                        : Yme009AccessStop.AtUncertain;
                    return new Yme009AccessResult(stop, false, _connection, at.ToString());
                }

                _atConfirmed = true;
                var ready = await WaitUntilCommunicableAsync(workToken).ConfigureAwait(false);
                if (ready.Kind == ReadyKind.TimedOut)
                    return new Yme009AccessResult(Yme009AccessStop.ReadyFailed, true, _connection, "ADB 未就绪");

                if (ready.Kind == ReadyKind.Multiple)
                {
                    var detail = "状态为 device 的数量=" + ready.DeviceCount + "，不选择";
                    Log("多设备拒绝。" + detail);
                    return new Yme009AccessResult(Yme009AccessStop.AssociationFailed, true, _connection, detail);
                }

                if (ready.Kind != ReadyKind.Unique
                    || !_connection.TryAcceptUniqueStationTransport(ready.Device.TransportId, ready.Device.Serial))
                {
                    Log("Transport ID 无效，不建立正式上下文。");
                    return new Yme009AccessResult(Yme009AccessStop.AssociationFailed, true, _connection, "Transport ID 无效");
                }

                Log("唯一 ADB 设备已选定，Transport ID=" + _connection.TransportId
                    + "。依据是单设备工位下唯一的 device，不是 CDC 与 ADB 的物理身份证明。");
                return new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, _connection, string.Empty);
            }
            catch (OperationCanceledException)
            {
                var stop = _atConfirmed ? Yme009AccessStop.StoppedAfterAt : Yme009AccessStop.StoppedBeforeAt;
                return new Yme009AccessResult(stop, _atConfirmed, _connection, "工作已停止");
            }
            catch (Exception ex)
            {
                _logging.LogError("YME009 设备接入出现未预期异常。", ex);
                return new Yme009AccessResult(Yme009AccessStop.Unexpected, _atConfirmed, _connection, ex.Message);
            }
            finally
            {
                _observing = false;
            }
        }

        public async Task<Yme009CloseAttempt> TryCloseAsync(CancellationToken cleanupToken)
        {
            var identityAvailable = _connection != null && !_connection.IsRevoked && _connection.IdentityKey.Length > 0;
            if (!Yme009AdbClosePolicy.ShouldAttemptClose(CurrentStopForClose(), _atConfirmed, identityAvailable))
            {
                Log("关闭策略未进入发送。AT已确认=" + _atConfirmed + "，身份可用=" + identityAvailable);
                return Yme009CloseAttempt.NotAttempted();
            }

            var formal = _connection.Stage == Yme009ConnectionStage.Formal;
            Log("进入关闭策略，选择" + (formal ? "正式" : "临时")
                + "上下文。关闭按当前唯一 CDC 发送，不比较 Enable 接口实例。");
            try
            {
                var at = await _atSwitch.DisableAsync(cleanupToken).ConfigureAwait(false);
                if (at == Yme009AtResult.Succeeded)
                {
                    Log("关闭结果=Confirmed。关闭命令已写入，并收到符合规则的成功响应。");
                    return new Yme009CloseAttempt(Yme009CloseDisposition.Sent, at, string.Empty);
                }

                if (at == Yme009AtResult.NotSent)
                {
                    Log("关闭结果=Skipped。没有发送关闭命令。");
                    return new Yme009CloseAttempt(Yme009CloseDisposition.Skipped, at, "关闭前未能确认当前 CDC");
                }

                Log("关闭结果=Uncertain。关闭命令已写入，但响应不能确认成功。");
                return new Yme009CloseAttempt(Yme009CloseDisposition.Failed, at, "关闭命令结果不确定");
            }
            catch (Exception ex)
            {
                _logging.LogError(Yme009AccessLog.Line(_scope, "关闭结果=Failed。关闭过程抛出异常，接入结果保持不变。"), ex);
                return new Yme009CloseAttempt(Yme009CloseDisposition.Failed, Yme009AtResult.Uncertain, ex.Message);
            }
        }

        public void Release()
        {
            var started = Stopwatch.StartNew();
            Log("资源清理开始");
            _observing = false;
            Log("CDC、Ready 和身份关联观察已停止");
            if (_connection == null)
            {
                Log("没有需要作废的临时或正式上下文");
            }
            else
            {
                var stage = _connection.Stage;
                var revoked = _connection.IsRevoked;
                _connection.Revoke();
                Log((stage == Yme009ConnectionStage.Formal ? "正式上下文" : "临时上下文")
                    + (revoked ? "在清理前已作废" : "已作废")
                    + "。串口句柄不在上下文中，由每次 AT 交换结束时释放。");
            }

            Log("资源清理结束，耗时=" + started.ElapsedMilliseconds + "ms");
        }

        public bool IsObserving
        {
            get { return _observing; }
        }

        private Yme009AccessStop CurrentStopForClose()
        {
            if (_connection != null && _connection.Stage == Yme009ConnectionStage.Formal && !_connection.IsRevoked)
                return Yme009AccessStop.AccessSucceeded;

            return _atConfirmed ? Yme009AccessStop.ReadyFailed : Yme009AccessStop.AtNotSent;
        }

        private async Task<string> ObserveUntilConfirmedAsync(CancellationToken workToken)
        {
            var gateStarted = Stopwatch.StartNew();
            var gate = new Yme009CdcUniquenessGate(_clock.UtcNow, _timing.CdcWindow);
            var scanIndex = 0;
            var previousIdentity = string.Empty;
            Log("CDC门禁开始，窗口=" + (int)_timing.CdcWindow.TotalSeconds + "s");
            while (true)
            {
                workToken.ThrowIfCancellationRequested();
                scanIndex++;
                var scanWatch = Stopwatch.StartNew();
                var scan = await DiscoverOrFailAsync(workToken).ConfigureAwait(false);
                scanWatch.Stop();
                var currentIdentity = SingleIdentity(scan);
                Log("CDC扫描序号=" + scanIndex
                    + "，耗时=" + scanWatch.ElapsedMilliseconds + "ms，"
                    + DescribeScan(scan)
                    + "，与上次身份一致=" + SameIdentity(previousIdentity, currentIdentity));
                previousIdentity = currentIdentity;

                var decision = gate.Apply(scan, _clock.UtcNow);
                if (decision.Action == Yme009GateAction.Confirmed)
                {
                    Log("CDC门禁通过。发送前复核通过，身份=" + Yme009AccessLog.Detail(decision.IdentityKey)
                        + "，总耗时=" + gateStarted.ElapsedMilliseconds + "ms");
                    return decision.IdentityKey;
                }

                if (decision.Action == Yme009GateAction.TimedOut)
                {
                    Log("CDC门禁失败。窗口到期仍未通过发送前复核，总耗时=" + gateStarted.ElapsedMilliseconds + "ms");
                    return null;
                }

                Log("CDC门禁继续等待。" + DescribeProgress(gate, decision)
                    + "，待确认身份=" + Yme009AccessLog.Detail(gate.PendingIdentity));
                var waiting = StageChanged;
                if (waiting != null)
                    waiting(Yme009OperatorStage.WaitingForDevice);
                if (!decision.RescanImmediately)
                    await _delay.DelayAsync(_timing.NonUniquePause, workToken).ConfigureAwait(false);
            }
        }

        private async Task<ReadyObservation> WaitUntilCommunicableAsync(CancellationToken workToken)
        {
            var startedAt = _clock.UtcNow;
            var watch = Stopwatch.StartNew();
            var deadline = startedAt + _timing.ReadyWindow;
            var queryCount = 0;
            var lastClass = "E";
            Log("ADB Ready开始，窗口=" + (int)_timing.ReadyWindow.TotalSeconds + "s，轮询=" + (int)_timing.ReadyPoll.TotalSeconds + "s");
            while (_clock.UtcNow <= deadline)
            {
                workToken.ThrowIfCancellationRequested();
                queryCount++;
                Yme009AdbQueryResult query;
                try
                {
                    query = await _adbQuery.QueryAsync(workToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logging.LogWarning(Yme009AccessLog.Line(_scope, "ADB Ready查询序号=" + queryCount + "，分类=E，查询异常：" + ex.GetType().Name + " " + ex.Message));
                    query = new Yme009AdbQueryResult(false, Array.Empty<Yme009AdbDevice>());
                    query.DiagnosticClass = "E";
                }

                var communicable = query.QuerySucceeded
                    ? CommunicableOnly(query.Devices)
                    : Array.Empty<Yme009AdbDevice>();
                lastClass = ClassifyReady(query, communicable.Count);
                Log("ADB Ready查询序号=" + queryCount + "，分类=" + lastClass
                    + "，设备数=" + query.Devices.Count
                    + "，device数量=" + communicable.Count
                    + "，状态=" + DescribeStates(query.Devices));
                if (query.QuerySucceeded && communicable.Count > 1)
                {
                    Log("ADB Ready结束，多设备拒绝，device数量=" + communicable.Count
                        + "。开始=" + startedAt.ToString("HH:mm:ss.fff")
                        + "，结束=" + _clock.UtcNow.ToString("HH:mm:ss.fff")
                        + "，耗时=" + watch.ElapsedMilliseconds + "ms，查询次数=" + queryCount);
                    return ReadyObservation.Multiple(communicable.Count);
                }

                if (query.QuerySucceeded && communicable.Count == 1)
                {
                    var device = communicable[0];
                    if (!Yme009AdbDeviceListParser.IsStationSelectable(device.TransportId))
                    {
                        Log("ADB Ready结束，Transport ID 无效。开始=" + startedAt.ToString("HH:mm:ss.fff")
                            + "，结束=" + _clock.UtcNow.ToString("HH:mm:ss.fff")
                            + "，耗时=" + watch.ElapsedMilliseconds + "ms，查询次数=" + queryCount);
                        return ReadyObservation.InvalidTransport();
                    }

                    Log("ADB Ready结束，唯一 ADB 设备选定，Transport ID=" + device.TransportId.Trim()
                        + "。开始=" + startedAt.ToString("HH:mm:ss.fff")
                        + "，结束=" + _clock.UtcNow.ToString("HH:mm:ss.fff")
                        + "，耗时=" + watch.ElapsedMilliseconds + "ms，查询次数=" + queryCount);
                    return ReadyObservation.Unique(device);
                }

                if (_clock.UtcNow >= deadline)
                    break;

                await _delay.DelayAsync(_timing.ReadyPoll, workToken).ConfigureAwait(false);
            }

            Log("ADB Ready结束，分类=" + lastClass
                + "，未选定唯一 ADB Transport。开始=" + startedAt.ToString("HH:mm:ss.fff")
                + "，结束=" + _clock.UtcNow.ToString("HH:mm:ss.fff")
                + "，耗时=" + watch.ElapsedMilliseconds + "ms，查询次数=" + queryCount);
            return ReadyObservation.TimedOut();
        }

        private enum ReadyKind
        {
            TimedOut = 0,
            Unique = 1,
            Multiple = 2,
            InvalidTransport = 3
        }

        private sealed class ReadyObservation
        {
            private ReadyObservation(ReadyKind kind, Yme009AdbDevice device, int deviceCount)
            {
                Kind = kind;
                Device = device;
                DeviceCount = deviceCount;
            }

            public ReadyKind Kind { get; }

            public Yme009AdbDevice Device { get; }

            public int DeviceCount { get; }

            public static ReadyObservation TimedOut()
            {
                return new ReadyObservation(ReadyKind.TimedOut, null, 0);
            }

            public static ReadyObservation Unique(Yme009AdbDevice device)
            {
                return new ReadyObservation(ReadyKind.Unique, device, 1);
            }

            public static ReadyObservation Multiple(int deviceCount)
            {
                return new ReadyObservation(ReadyKind.Multiple, null, deviceCount);
            }

            public static ReadyObservation InvalidTransport()
            {
                return new ReadyObservation(ReadyKind.InvalidTransport, null, 1);
            }
        }

        private static string ClassifyReady(Yme009AdbQueryResult query, int communicableCount)
        {
            if (query != null && !string.IsNullOrEmpty(query.DiagnosticClass))
                return query.DiagnosticClass;

            if (query == null || !query.QuerySucceeded)
                return "A";

            if (communicableCount > 1)
                return "多设备";

            if (communicableCount == 1)
                return "D";

            return query.Devices.Count == 0 ? "B" : "C";
        }

        private static string DescribeStates(IReadOnlyList<Yme009AdbDevice> devices)
        {
            if (devices == null || devices.Count == 0)
                return "(none)";

            var text = new System.Text.StringBuilder();
            for (var i = 0; i < devices.Count; i++)
            {
                if (i > 0)
                    text.Append(',');
                text.Append(string.IsNullOrEmpty(devices[i].State) ? "(empty)" : devices[i].State);
            }

            return text.ToString();
        }

        private static string SingleIdentity(Yme009CdcScan scan)
        {
            if (scan == null || scan.DiscoveryFailed || scan.Candidates.Count != 1)
                return string.Empty;

            return scan.Candidates[0].IdentityKey ?? string.Empty;
        }

        private static string SameIdentity(string previous, string current)
        {
            if (previous.Length == 0 || current.Length == 0)
                return "无法比较";

            return string.Equals(previous, current, StringComparison.Ordinal) ? "是" : "否";
        }

        private static string DescribeScan(Yme009CdcScan scan)
        {
            if (scan == null || scan.DiscoveryFailed)
                return "数量=0，发现失败";

            var text = "数量=" + scan.Candidates.Count;
            for (var i = 0; i < scan.Candidates.Count; i++)
            {
                text += "，身份=" + Yme009AccessLog.Detail(scan.Candidates[i].IdentityKey)
                    + "，COM=" + Yme009AccessLog.Detail(scan.Candidates[i].PortName);
            }

            return text;
        }

        private static string DescribeProgress(Yme009CdcUniquenessGate gate, Yme009GateDecision decision)
        {
            if (gate.IsAwaitingPrecheck)
                return "连续两次为同一身份，下一次是发送前复核";

            if (decision.RescanImmediately && gate.PendingIdentity.Length > 0)
                return "已记录第一次唯一，立即复扫";

            return "连续确认已清除";
        }

        private static IReadOnlyList<Yme009AdbDevice> CommunicableOnly(IReadOnlyList<Yme009AdbDevice> devices)
        {
            var list = new List<Yme009AdbDevice>();
            for (var i = 0; i < devices.Count; i++)
            {
                if (devices[i].IsCommunicable)
                    list.Add(devices[i]);
            }

            return list;
        }

        private async Task<Yme009CdcScan> DiscoverOrFailAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logging.LogWarning(Yme009AccessLog.Line(_scope, "CDC发现抛出异常：" + ex.GetType().Name + " " + ex.Message));
                return Yme009CdcScan.Failed();
            }
        }

        private void Log(string message)
        {
            _logging.LogInfo(Yme009AccessLog.Line(_scope, message));
        }
    }
}
