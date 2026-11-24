using System;
using System.Collections.Generic;

namespace SnVerify.Domain.Product
{
    public sealed class Yme009AdbDevice
    {
        public Yme009AdbDevice(string serial, string state, string transportId)
        {
            Serial = serial ?? string.Empty;
            State = state ?? string.Empty;
            TransportId = transportId ?? string.Empty;
        }

        public string Serial { get; }

        public string State { get; }

        public string TransportId { get; }

        public bool IsCommunicable
        {
            get { return string.Equals(State, "device", StringComparison.Ordinal); }
        }
    }

    public static class Yme009AdbDeviceListParser
    {
        public static IReadOnlyList<Yme009AdbDevice> Parse(string output)
        {
            var devices = new List<Yme009AdbDevice>();
            if (string.IsNullOrWhiteSpace(output))
                return devices;

            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.Ordinal))
                    continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                devices.Add(new Yme009AdbDevice(parts[0], parts[1], FindTransportId(parts)));
            }

            return devices;
        }

        public static bool IsStationSelectable(string transportId)
        {
            if (string.IsNullOrWhiteSpace(transportId))
                return false;

            var text = transportId.Trim();
            if (text.Length == 0 || text.Length > 9 || text[0] == '0')
                return false;

            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i]))
                    return false;
            }

            return true;
        }

        public static bool HasCommunicableDevice(IReadOnlyList<Yme009AdbDevice> devices)
        {
            if (devices == null)
                return false;

            for (var i = 0; i < devices.Count; i++)
            {
                if (devices[i].IsCommunicable)
                    return true;
            }

            return false;
        }

        private static string FindTransportId(string[] parts)
        {
            for (var i = 2; i < parts.Length; i++)
            {
                const string prefix = "transport_id:";
                if (parts[i].StartsWith(prefix, StringComparison.Ordinal) && parts[i].Length > prefix.Length)
                    return parts[i].Substring(prefix.Length);
            }

            return string.Empty;
        }
    }

    public sealed class Yme009AssociationResult
    {
        private Yme009AssociationResult(bool isProven, string transportId, string serialForLog)
        {
            IsProven = isProven;
            TransportId = transportId ?? string.Empty;
            SerialForLog = serialForLog ?? string.Empty;
        }

        public bool IsProven { get; }

        public string TransportId { get; }

        public string SerialForLog { get; }

        public static Yme009AssociationResult Unproven()
        {
            return new Yme009AssociationResult(false, string.Empty, string.Empty);
        }

        /// <summary>
        /// 只表示既有模型中的物理身份关联已证明。单设备工位选定 Transport 不调用它。
        /// </summary>
        public static Yme009AssociationResult Proven(string transportId, string serialForLog)
        {
            if (string.IsNullOrWhiteSpace(transportId))
                throw new ArgumentException("已证明的关联必须带 Transport ID。", nameof(transportId));

            return new Yme009AssociationResult(true, transportId.Trim(), serialForLog);
        }
    }
}
