using System;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Models;

namespace SnVerify.Infrastructure.DeviceAccess.Parser
{
    /// <summary>
    /// KM008 聚合命令输出解析器。第二行 CSV：android, sn, wifiMac。
    /// </summary>
    public sealed class Km008AndroidVersionAggregateParser : IAggregateDeviceInfoParser
    {
        private static AggregateProtocolException Protocol(string message, string rawOutput)
        {
            return new AggregateProtocolException($"{message} | 原始输出: {rawOutput}");
        }

        public DeviceInfo Parse(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("ADB 输出为空");

            var normalized = output.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length < 2)
                throw Protocol("聚合协议错误：输出至少两行（时间行 + 数据行）", output);

            var dataLine = lines[1]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dataLine))
                throw Protocol("聚合协议错误：第二行数据为空", output);

            var cols = dataLine.Split(',');
            if (cols.Length < 3)
                throw Protocol($"聚合协议错误：KM008 字段数量不足，期望=3，实际={cols.Length}", output);

            string At(int index) => (cols[index] ?? string.Empty).Trim();

            var androidVersion = At(0);
            var deviceSn = At(1);
            var wifiMac = At(2);

            if (string.IsNullOrWhiteSpace(deviceSn))
                throw new AggregateProtocolException("Device SN is empty");
            if (string.IsNullOrWhiteSpace(androidVersion))
                throw new AggregateProtocolException("Android version is empty");

            return new DeviceInfo
            {
                AndroidVersion = androidVersion,
                DeviceSn = deviceSn,
                WifiMac = string.IsNullOrWhiteSpace(wifiMac) ? wifiMac : wifiMac.ToUpperInvariant(),
                ChipId = null,
                BoardVersion = null,
                ChargeBoardVersion = null
            };
        }
    }
}
