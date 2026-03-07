/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：按 ProductProfile.AdbConfig 读取设备信息。Aggregate 与 Field 二选一。</remarks>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.DeviceAccess;
using SnVerify.Infrastructure.DeviceAccess.Session;
using SnVerify.Infrastructure.DeviceAccess.Command;
using SnVerify.Infrastructure.DeviceAccess.Parser;

namespace SnVerify.Infrastructure.DeviceAccess.Service
{
    /// <summary>
    /// 设备访问服务实现：Session 级 Bootstrap，Aggregate 或 Field 命令二选一，ParserFactory 解析。
    /// </summary>
    public class AdbDeviceService : IDeviceAccessService
    {
        private readonly DeviceSessionManager _sessionManager;
        private readonly DeviceCommandExecutor _commandExecutor;
        private readonly IParserFactory _parserFactory;
        private const int TotalTimeoutMs = 10000;
        private const int CommandTimeoutMs = 5000;

        public AdbDeviceService(
            DeviceSessionManager sessionManager,
            DeviceCommandExecutor commandExecutor,
            IParserFactory parserFactory)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
            _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        }

        /// <inheritdoc />
        public async Task<DeviceInfo> ReadDeviceInfoAsync(ProductProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            var config = profile.AdbConfig;
            if (config == null)
                throw new InvalidOperationException("ADB 命令未配置");

            bool useAggregate = config.AggregateCommand != null
                && !string.IsNullOrWhiteSpace(config.AggregateCommand.Command)
                && !string.IsNullOrWhiteSpace(config.AggregateCommand.ParserKey);
            bool useFields = config.Commands != null && config.Commands.Count > 0;

            if (useAggregate && useFields)
                throw new InvalidOperationException("Aggregate 与 Field 命令不可同时配置，请仅配置其一");

            if (!useAggregate && !useFields)
                throw new InvalidOperationException("ADB 命令未配置");

            using (var cts = new CancellationTokenSource(TotalTimeoutMs))
            {
                await _sessionManager.EnsureSessionReadyAsync(config, cts.Token).ConfigureAwait(false);

                if (useAggregate)
                    return await ExecuteAggregateAsync(config.AggregateCommand, cts.Token).ConfigureAwait(false);
                return await ExecuteFieldCommandsAsync(config.Commands, cts.Token).ConfigureAwait(false);
            }
        }

        private async Task<DeviceInfo> ExecuteAggregateAsync(AggregateDeviceInfoCommand aggregate, CancellationToken token)
        {
            var output = await _commandExecutor.ExecuteAsync(aggregate.Command, CommandTimeoutMs, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                return new DeviceInfo();

            var parser = _parserFactory.GetAggregate(aggregate.ParserKey);
            return parser.Parse(output) ?? new DeviceInfo();
        }

        private async Task<DeviceInfo> ExecuteFieldCommandsAsync(System.Collections.Generic.List<DeviceInfoCommand> commands, CancellationToken token)
        {
            var info = new DeviceInfo();
            foreach (var cmd in commands)
            {
                var output = await _commandExecutor.ExecuteAsync(cmd.Command, CommandTimeoutMs, token).ConfigureAwait(false);
                var parser = _parserFactory.Get(cmd.ParserKey);
                var value = parser.Parse(output ?? string.Empty);

                switch (cmd.Field)
                {
                    case DeviceInfoField.DeviceSn:
                        info.DeviceSn = value;
                        break;
                    case DeviceInfoField.ChipId:
                        info.ChipId = value;
                        break;
                    case DeviceInfoField.WifiMac:
                        info.WifiMac = value;
                        break;
                    case DeviceInfoField.AndroidVersion:
                        info.AndroidVersion = value;
                        break;
                    case DeviceInfoField.BoardVersion:
                        info.BoardVersion = value;
                        break;
                    case DeviceInfoField.ChargeBoardVersion:
                        info.ChargeBoardVersion = value;
                        break;
                }
            }
            return info;
        }
    }
}
