using System;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Models;

namespace SnVerify.Infrastructure.DeviceAccess.Parser
{
    /// <summary>
    /// KM001 聚合命令输出解析器。
    /// 协议约定：
    /// - 第1行：时间（忽略）
    /// - 第2行：charge,board,chipId,android,sn,wifiMac
    /// </summary>
    public class Km001McuVersionAggregateParser : IAggregateDeviceInfoParser
    {
        private static AggregateProtocolException CreateProtocolException(string message, string rawOutput)
        {
            return new AggregateProtocolException($"{message} | 原始输出: {rawOutput}");
        }

        public DeviceInfo Parse(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException("ADB 输出为空");
            }

            var normalized = output.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length < 2)
            {
                throw CreateProtocolException("聚合协议错误：输出至少两行（时间行 + 数据行）", output);
            }

            var dataLine = lines[1]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dataLine))
            {
                throw CreateProtocolException("聚合协议错误：第二行数据为空", output);
            }

            var cols = dataLine.Split(',');
            if (cols.Length < 6)
            {
                throw CreateProtocolException($"聚合协议错误：字段数量不足，期望=6，实际={cols.Length}", output);
            }

            string At(int index) => (cols[index] ?? string.Empty).Trim();

            return new DeviceInfo
            {
                ChargeBoardVersion = At(0),
                BoardVersion = At(1),
                ChipId = At(2),
                AndroidVersion = At(3),
                DeviceSn = At(4),
                WifiMac = At(5).ToUpperInvariant()
            };
        }
    }
}
