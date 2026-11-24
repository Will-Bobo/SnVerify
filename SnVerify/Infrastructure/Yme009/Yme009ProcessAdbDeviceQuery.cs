using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;
using SnVerify.Services.Yme009;

namespace SnVerify.Infrastructure.Yme009
{
    public sealed class Yme009ProcessAdbDeviceQuery : IYme009AdbDeviceQuery
    {
        private readonly string _adbPath;
        private readonly IProcessRunner _processRunner;
        private readonly ILoggingService _logging;
        private readonly Yme009RoundLogScope _scope;
        private int _queryIndex;
        private string _countedRound = "-";

        public Yme009ProcessAdbDeviceQuery(
            string adbPath,
            IProcessRunner processRunner,
            ILoggingService logging = null,
            Yme009RoundLogScope scope = null)
        {
            _adbPath = adbPath ?? throw new ArgumentNullException(nameof(adbPath));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _logging = logging;
            _scope = scope;
        }

        public async Task<Yme009AdbQueryResult> QueryAsync(CancellationToken cancellationToken)
        {
            var index = NextIndex();
            var watch = Stopwatch.StartNew();
            ProcessExecutionResult result;
            try
            {
                result = await _processRunner.RunAsync(_adbPath, "devices -l", 15000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                watch.Stop();
                Log(index, "E", false, -1, true, string.Empty, ex.GetType().Name + " " + ex.Message, watch.ElapsedMilliseconds, Array.Empty<Yme009AdbDevice>());
                var failed = new Yme009AdbQueryResult(false, Array.Empty<Yme009AdbDevice>());
                failed.DiagnosticClass = "E";
                return failed;
            }

            watch.Stop();
            if (result == null || result.IsTimeout)
            {
                Log(index, "E", false, result == null ? -1 : result.ExitCode, true, result == null ? string.Empty : result.StandardOutput, result == null ? "进程结果为空" : result.StandardError, watch.ElapsedMilliseconds, Array.Empty<Yme009AdbDevice>());
                var timedOut = new Yme009AdbQueryResult(false, Array.Empty<Yme009AdbDevice>());
                timedOut.DiagnosticClass = "E";
                return timedOut;
            }

            if (!result.IsSuccess || result.ExitCode != 0)
            {
                Log(index, "A", false, result.ExitCode, false, result.StandardOutput, FirstNonEmpty(result.StandardError, result.ErrorMessage), watch.ElapsedMilliseconds, Array.Empty<Yme009AdbDevice>());
                var failed = new Yme009AdbQueryResult(false, Array.Empty<Yme009AdbDevice>());
                failed.DiagnosticClass = "A";
                return failed;
            }

            var devices = Yme009AdbDeviceListParser.Parse(result.StandardOutput);
            var klass = devices.Count == 0
                ? "B"
                : (Yme009AdbDeviceListParser.HasCommunicableDevice(devices) ? "D" : "C");
            Log(index, klass, true, result.ExitCode, false, result.StandardOutput, result.StandardError, watch.ElapsedMilliseconds, devices);
            var succeeded = new Yme009AdbQueryResult(true, devices);
            succeeded.DiagnosticClass = klass;
            return succeeded;
        }

        private int NextIndex()
        {
            var roundId = _scope == null ? "-" : _scope.RoundId;
            if (!string.Equals(_countedRound, roundId, StringComparison.Ordinal))
            {
                _countedRound = roundId;
                _queryIndex = 0;
            }

            _queryIndex++;
            return _queryIndex;
        }

        private void Log(
            int index,
            string klass,
            bool executed,
            int exitCode,
            bool timedOut,
            string stdout,
            string stderr,
            long elapsedMs,
            System.Collections.Generic.IReadOnlyList<Yme009AdbDevice> devices)
        {
            if (_logging == null)
                return;

            var foundDevice = false;
            var states = new System.Text.StringBuilder();
            for (var i = 0; i < devices.Count; i++)
            {
                if (i > 0)
                    states.Append(',');
                states.Append(string.IsNullOrEmpty(devices[i].State) ? "(empty)" : devices[i].State);
                if (devices[i].IsCommunicable)
                    foundDevice = true;
            }

            _logging.LogInfo(Yme009AccessLog.Line(
                _scope,
                "adb devices -l 序号=" + index
                + "，分类=" + klass
                + "，执行成功=" + executed
                + "，退出码=" + exitCode
                + "，超时=" + timedOut
                + "，耗时=" + elapsedMs + "ms"
                + "，解析数量=" + devices.Count
                + "，状态=" + (devices.Count == 0 ? "(none)" : states.ToString())
                + "，找到device=" + foundDevice
                + "，stdout=" + Yme009AccessLog.Detail(stdout)
                + "，stderr=" + Yme009AccessLog.Detail(stderr)));
        }

        private static string FirstNonEmpty(string first, string second)
        {
            if (!string.IsNullOrEmpty(first))
                return first;

            return second ?? string.Empty;
        }
    }
}
