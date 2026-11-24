using System;
using System.Collections.Generic;

namespace SnVerify.Domain.Product
{
    public sealed class Yme009CdcCandidate
    {
        public Yme009CdcCandidate(string identityKey, string portName)
        {
            IdentityKey = identityKey ?? string.Empty;
            PortName = portName ?? string.Empty;
        }

        public string IdentityKey { get; }

        public string PortName { get; }
    }

    public sealed class Yme009CdcScan
    {
        private Yme009CdcScan(bool discoveryFailed, IReadOnlyList<Yme009CdcCandidate> candidates)
        {
            DiscoveryFailed = discoveryFailed;
            Candidates = candidates ?? Array.Empty<Yme009CdcCandidate>();
        }

        public bool DiscoveryFailed { get; }

        public IReadOnlyList<Yme009CdcCandidate> Candidates { get; }

        public static Yme009CdcScan Failed()
        {
            return new Yme009CdcScan(true, Array.Empty<Yme009CdcCandidate>());
        }

        public static Yme009CdcScan Found(IReadOnlyList<Yme009CdcCandidate> candidates)
        {
            return new Yme009CdcScan(false, candidates ?? Array.Empty<Yme009CdcCandidate>());
        }
    }

    /// <summary>
    /// 只按 CDC ACM 兼容 ID 和 COM 口筛选。不使用参考工程的 VID_0E8D 或父设备名。
    /// 同一台设备的比较键是接口实例 ID，不是 COM 口。
    /// </summary>
    public static class Yme009CdcAcmFilter
    {
        public static bool TryMatch(string instanceId, string portName, IReadOnlyList<string> compatibleIds, out Yme009CdcCandidate candidate)
        {
            candidate = null;
            if (!IsCdcAcm(compatibleIds) || string.IsNullOrWhiteSpace(instanceId) || !IsComPort(portName))
                return false;

            candidate = new Yme009CdcCandidate(instanceId.Trim(), portName.Trim());
            return true;
        }

        public static bool IsCdcAcm(IReadOnlyList<string> compatibleIds)
        {
            if (compatibleIds == null)
                return false;

            for (var i = 0; i < compatibleIds.Count; i++)
            {
                var id = compatibleIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (ContainsToken(id, "Class_02")
                    && ContainsToken(id, "SubClass_02")
                    && ContainsToken(id, "Prot_01"))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsComPort(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
                return false;

            var name = portName.Trim();
            if (name.Length < 4 || name.IndexOf("COM", StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            for (var i = 3; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i]))
                    return false;
            }

            return true;
        }

        private static bool ContainsToken(string value, string token)
        {
            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
