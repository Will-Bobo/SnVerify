using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Yme009
{
    /// <summary>
    /// 每次 AT 交换都重新发现 CDC，只打开当次 COM，交换结束释放串口。
    /// </summary>
    public sealed class Yme009AtAdbSwitch
    {
        private readonly IYme009CdcDiscovery _discovery;
        private readonly IYme009SerialPortFactory _ports;
        private readonly IYme009Clock _clock;
        private readonly IYme009AsyncDelay _delay;
        private readonly TimeSpan _readBudget;
        private readonly ILoggingService _logging;
        private readonly Yme009RoundLogScope _scope;

        public Yme009AtAdbSwitch(
            IYme009CdcDiscovery discovery,
            IYme009SerialPortFactory ports,
            IYme009Clock clock,
            IYme009AsyncDelay delay,
            TimeSpan readBudget,
            ILoggingService logging = null,
            Yme009RoundLogScope scope = null)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _ports = ports ?? throw new ArgumentNullException(nameof(ports));
            _clock = clock ?? new Yme009SystemClock();
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
            _readBudget = readBudget;
            _logging = logging;
            _scope = scope;
        }

        public Task<Yme009AtResult> EnableAsync(string identityKey, CancellationToken cancellationToken)
        {
            return ExchangeAsync(identityKey, Yme009AtCommands.EnableAdb, true, cancellationToken);
        }

        public Task<Yme009AtResult> DisableAsync(CancellationToken cancellationToken)
        {
            return ExchangeAsync(string.Empty, Yme009AtCommands.DisableAdb, false, cancellationToken);
        }

        private async Task<Yme009AtResult> ExchangeAsync(
            string identityKey,
            string command,
            bool matchEnableIdentity,
            CancellationToken cancellationToken)
        {
            var operation = string.Equals(command, Yme009AtCommands.DisableAdb, StringComparison.Ordinal)
                ? "Disable"
                : "Enable";
            var exchange = Stopwatch.StartNew();
            Log(matchEnableIdentity
                ? operation + " 开始，CDC身份=" + Yme009AccessLog.Detail(identityKey)
                : operation + " 开始，重新发现当前 CDC，不比较 Enable 接口实例");

            Yme009CdcScan scan;
            try
            {
                scan = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log(operation + " 重新发现异常：" + ex.GetType().Name + " " + ex.Message
                    + "。串口未打开，命令未发送，结果=NotSent，耗时=" + exchange.ElapsedMilliseconds + "ms");
                return Yme009AtResult.NotSent;
            }

            string portName;
            string selectReason;
            var selected = matchEnableIdentity
                ? TrySelectEnablePort(scan, identityKey, out portName, out selectReason)
                : TrySelectDisablePort(scan, out portName, out selectReason);
            if (!selected)
            {
                Log(operation + " " + selectReason + "。串口未打开，命令未发送，结果=NotSent，耗时="
                    + exchange.ElapsedMilliseconds + "ms");
                return Yme009AtResult.NotSent;
            }

            Log(matchEnableIdentity
                ? operation + " 唯一性通过，身份一致，COM=" + portName
                : operation + " 当前 CDC 唯一且 COM 有效，COM=" + portName);
            IYme009SerialPortSession session = null;
            var written = false;
            var writeElapsedMs = 0L;
            try
            {
                session = _ports.Open(portName);
                Log(operation + " 串口打开成功，COM=" + portName);
                var writeWatch = Stopwatch.StartNew();
                session.Write(command + "\r\n");
                writeWatch.Stop();
                writeElapsedMs = writeWatch.ElapsedMilliseconds;
                written = true;
                Log(operation + " AT命令已写入，写入耗时=" + writeElapsedMs + "ms");
                var readWatch = Stopwatch.StartNew();
                var response = await ReadAsync(session, cancellationToken).ConfigureAwait(false);
                readWatch.Stop();
                var result = Yme009AtResponseClassifier.Classify(true, response);
                Log(operation + " " + DescribeResponse(response, result)
                    + "，响应读取耗时=" + readWatch.ElapsedMilliseconds
                    + "ms，原始响应=" + Yme009AccessLog.Detail(response)
                    + "，最终结果=" + result
                    + "，交换耗时=" + exchange.ElapsedMilliseconds + "ms");
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var result = Yme009AtResponseClassifier.Classify(written, null);
                var stage = written
                    ? "命令已发送但随后异常"
                    : (session == null ? "串口打开失败，命令未发送" : "串口已打开但命令未发送");
                Log(operation + " " + stage + "：" + ex.GetType().Name + " " + ex.Message
                    + "，写入耗时=" + writeElapsedMs
                    + "ms，最终结果=" + result
                    + "，交换耗时=" + exchange.ElapsedMilliseconds + "ms");
                return result;
            }
            finally
            {
                if (session != null)
                {
                    session.Dispose();
                    Log(operation + " 串口已释放，COM=" + portName);
                }
            }
        }

        private static string DescribeResponse(string response, Yme009AtResult result)
        {
            if (result == Yme009AtResult.Succeeded)
                return "收到符合规则的成功响应";

            if (string.IsNullOrEmpty(response))
                return "命令已发送但响应超时";

            return "收到响应但格式不符合规则";
        }

        private async Task<string> ReadAsync(IYme009SerialPortSession session, CancellationToken cancellationToken)
        {
            var buffer = new StringBuilder();
            var deadline = _clock.UtcNow + _readBudget;
            while (_clock.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Append(session.ReadExisting());
                if (Yme009AtResponseClassifier.HasSplitSuccess(buffer.ToString()))
                    return buffer.ToString();

                var remaining = deadline - _clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                var slice = remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50);
                await _delay.DelayAsync(slice, cancellationToken).ConfigureAwait(false);
            }

            return buffer.ToString();
        }

        private static bool TrySelectDisablePort(Yme009CdcScan scan, out string portName, out string reason)
        {
            portName = null;
            if (!TryReadUniqueCandidate(scan, out var candidate, out reason))
                return false;

            if (!Yme009CdcAcmFilter.IsComPort(candidate.PortName))
            {
                reason = "COM无效，当前值=" + Yme009AccessLog.Detail(candidate.PortName);
                return false;
            }

            portName = candidate.PortName;
            reason = string.Empty;
            return true;
        }

        private static bool TrySelectEnablePort(
            Yme009CdcScan scan,
            string identityKey,
            out string portName,
            out string reason)
        {
            portName = null;
            if (!TryReadUniqueCandidate(scan, out var candidate, out reason))
                return false;

            if (!string.Equals(candidate.IdentityKey, identityKey, StringComparison.Ordinal))
            {
                reason = "身份不匹配，当前身份=" + Yme009AccessLog.Detail(candidate.IdentityKey);
                return false;
            }

            if (!Yme009CdcAcmFilter.IsComPort(candidate.PortName))
            {
                reason = "COM无效，当前值=" + Yme009AccessLog.Detail(candidate.PortName);
                return false;
            }

            portName = candidate.PortName;
            reason = string.Empty;
            return true;
        }

        private static bool TryReadUniqueCandidate(Yme009CdcScan scan, out Yme009CdcCandidate candidate, out string reason)
        {
            candidate = null;
            if (scan == null || scan.DiscoveryFailed)
            {
                reason = "重新发现失败";
                return false;
            }

            if (scan.Candidates.Count != 1)
            {
                reason = "当前不是唯一设备，数量=" + scan.Candidates.Count;
                return false;
            }

            candidate = scan.Candidates[0];
            reason = string.Empty;
            return true;
        }

        private void Log(string message)
        {
            if (_logging == null)
                return;

            _logging.LogInfo(Yme009AccessLog.Line(_scope, message));
        }
    }
}
