using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Yme009
{
    /// <summary>
    /// 通过正式连接上下文读取八项属性。超时沿用现有 YME009 adb devices 查询的 15 秒进程预算，不是已确认的协议时限。
    /// </summary>
    public sealed class Yme009ProcessPropertyReader : IYme009PropertyReader
    {
        public const int CommandTimeoutMilliseconds = 15000;

        private readonly string _adbPath;
        private readonly IProcessRunner _runner;
        private readonly IYme009AdbDeviceQuery _devices;
        private readonly ILoggingService _logging;
        private readonly Yme009RoundLogScope _scope;

        public Yme009ProcessPropertyReader(
            string adbPath,
            IProcessRunner runner,
            IYme009AdbDeviceQuery devices,
            ILoggingService logging,
            Yme009RoundLogScope scope = null)
        {
            _adbPath = string.IsNullOrWhiteSpace(adbPath) ? "adb" : adbPath;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _scope = scope;
        }

        public async Task<Yme009RawAttributeSet> ReadAsync(Yme009DeviceConnection connection, CancellationToken cancellationToken)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (!connection.AllowsBusinessUse)
                throw new InvalidOperationException("正式连接上下文不可用，不能读取业务属性。");

            var definitions = Yme009AttributeCatalog.All;
            var fields = new Yme009FieldRaw[definitions.Count];
            var disconnected = false;
            for (var i = 0; i < definitions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (disconnected || !connection.AllowsBusinessUse)
                {
                    fields[i] = Yme009FieldRaw.Disconnected();
                    continue;
                }

                var definition = definitions[i];
                var arguments = connection.BuildDirectedArguments("shell getprop " + definition.PropertyKey);
                var result = await _runner.RunAsync(_adbPath, arguments, CommandTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                if (result != null && result.IsSuccess && result.ExitCode == 0 && !result.IsTimeout)
                {
                    fields[i] = Yme009FieldRaw.Succeeded(Yme009AttributeParser.StripTrailingLineEndings(result.StandardOutput));
                }
                else if (result != null && result.IsTimeout)
                {
                    fields[i] = Yme009FieldRaw.TimedOut();
                }
                else if (ReportsTransportMissing(result) || await TransportAbsentAsync(connection, cancellationToken).ConfigureAwait(false))
                {
                    fields[i] = Yme009FieldRaw.Disconnected();
                    disconnected = true;
                }
                else
                {
                    fields[i] = Yme009FieldRaw.CommandFailed();
                }

                _logging.LogInfo(Yme009AccessLog.Line(
                    _scope,
                    "属性读取 " + definition.PropertyKey + "，状态=" + fields[i].FailureStatus + "，命令成功=" + fields[i].CommandSucceeded));
            }

            return new Yme009RawAttributeSet(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7]);
        }

        private async Task<bool> TransportAbsentAsync(Yme009DeviceConnection connection, CancellationToken cancellationToken)
        {
            var query = await _devices.QueryAsync(cancellationToken).ConfigureAwait(false);
            if (query == null || !query.QuerySucceeded)
                return false;

            var devices = query.Devices;
            for (var i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].TransportId.Trim(), connection.TransportId, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static bool ReportsTransportMissing(ProcessExecutionResult result)
        {
            if (result == null)
                return false;
            return ContainsNotFound(result.ErrorMessage)
                || ContainsNotFound(result.StandardError)
                || ContainsNotFound(result.StandardOutput);
        }

        private static bool ContainsNotFound(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
