using System;

namespace SnVerify.Domain.Product
{
    public enum Yme009ConnectionStage
    {
        Temporary = 0,
        Formal = 1
    }

    /// <summary>
    /// 同一对象的两个阶段。正式阶段可以由单设备工位选定唯一 Transport 成立。
    /// 该选定不表示 CDC 与 ADB 已完成物理身份证明。
    /// 不保存可跨枚举复用的 COM 号或串口句柄。
    /// </summary>
    public sealed class Yme009DeviceConnection
    {
        private Yme009DeviceConnection(string generation, string identityKey)
        {
            Generation = generation ?? throw new ArgumentNullException(nameof(generation));
            IdentityKey = identityKey ?? throw new ArgumentNullException(nameof(identityKey));
            Stage = Yme009ConnectionStage.Temporary;
            TransportId = string.Empty;
            SerialForLog = string.Empty;
        }

        public string Generation { get; }

        public string IdentityKey { get; }

        public Yme009ConnectionStage Stage { get; private set; }

        public string TransportId { get; private set; }

        public string SerialForLog { get; private set; }

        public bool IsRevoked { get; private set; }

        public bool AllowsBusinessUse
        {
            get
            {
                return !IsRevoked
                    && Stage == Yme009ConnectionStage.Formal
                    && TransportId.Length > 0;
            }
        }

        public static Yme009DeviceConnection Temporary(string generation, string identityKey)
        {
            if (string.IsNullOrWhiteSpace(identityKey))
                throw new ArgumentException("临时上下文必须有 CDC 设备身份。", nameof(identityKey));

            return new Yme009DeviceConnection(generation, identityKey.Trim());
        }

        public bool TryPromote(Yme009AssociationResult association)
        {
            if (IsRevoked || Stage == Yme009ConnectionStage.Formal)
                return false;

            if (association == null || !association.IsProven || string.IsNullOrWhiteSpace(association.TransportId))
                return false;

            Stage = Yme009ConnectionStage.Formal;
            TransportId = association.TransportId;
            SerialForLog = association.SerialForLog ?? string.Empty;
            return true;
        }

        public bool TryAcceptUniqueStationTransport(string transportId, string serialForLog)
        {
            if (IsRevoked || Stage == Yme009ConnectionStage.Formal)
                return false;

            if (!Yme009AdbDeviceListParser.IsStationSelectable(transportId))
                return false;

            Stage = Yme009ConnectionStage.Formal;
            TransportId = transportId.Trim();
            SerialForLog = serialForLog ?? string.Empty;
            return true;
        }

        public void Revoke()
        {
            IsRevoked = true;
        }

        public string BuildDirectedArguments(string adbArguments)
        {
            if (!AllowsBusinessUse)
                throw new InvalidOperationException("正式连接上下文不可用，不能定向执行。");

            if (ContainsDeviceSelector(adbArguments))
                throw new InvalidOperationException("调用方不得再携带设备选择参数。");

            var rest = string.IsNullOrWhiteSpace(adbArguments) ? string.Empty : adbArguments.Trim();
            if (rest.Length == 0)
                return "-t " + TransportId;

            return "-t " + TransportId + " " + rest;
        }

        private static bool ContainsDeviceSelector(string adbArguments)
        {
            if (string.IsNullOrWhiteSpace(adbArguments))
                return false;

            var parts = adbArguments.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "-s" || parts[i] == "-t" || parts[i] == "-d" || parts[i] == "-e" || parts[i] == "--one-device")
                    return true;
            }

            return false;
        }
    }
}
