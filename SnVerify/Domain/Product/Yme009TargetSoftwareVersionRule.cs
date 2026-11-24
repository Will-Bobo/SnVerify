using System.Collections.Generic;

namespace SnVerify.Domain.Product
{
    /// <summary>
    /// YME009 开始测试时的目标软件版本规则。
    /// 比较已保存快照时使用区分大小写的完全比较；快照在保存前已 Trim。
    /// </summary>
    public static class Yme009TargetSoftwareVersionRule
    {
        public static Yme009TargetSoftwareVersionDecision Evaluate(
            string requestedVersion,
            IReadOnlyList<string> existingVersions)
        {
            if (string.IsNullOrWhiteSpace(requestedVersion))
                return Yme009TargetSoftwareVersionDecision.RejectMissing();

            var requested = Yme009TextComparison.Normalize(requestedVersion);
            string established = null;
            if (existingVersions != null)
            {
                foreach (var raw in existingVersions)
                {
                    var value = Yme009TextComparison.Normalize(raw);
                    if (value.Length == 0)
                        continue;

                    if (established == null)
                    {
                        established = value;
                        continue;
                    }

                    if (!Yme009TextComparison.Equals(established, value))
                        return Yme009TargetSoftwareVersionDecision.RejectConflict(established);
                }
            }

            if (established == null)
                return Yme009TargetSoftwareVersionDecision.Allow(requested);

            if (!Yme009TextComparison.Equals(established, requested))
                return Yme009TargetSoftwareVersionDecision.RejectConflict(established);

            return Yme009TargetSoftwareVersionDecision.Allow(requested);
        }
    }

    public enum Yme009TargetSoftwareVersionRejection
    {
        None = 0,
        Missing = 1,
        Conflict = 2
    }

    public sealed class Yme009TargetSoftwareVersionDecision
    {
        private Yme009TargetSoftwareVersionDecision(
            bool isAllowed,
            Yme009TargetSoftwareVersionRejection rejection,
            string establishedVersion,
            string normalizedRequestedVersion)
        {
            IsAllowed = isAllowed;
            Rejection = rejection;
            EstablishedVersion = establishedVersion;
            NormalizedRequestedVersion = normalizedRequestedVersion;
        }

        public bool IsAllowed { get; }

        public Yme009TargetSoftwareVersionRejection Rejection { get; }

        public string EstablishedVersion { get; }

        public string NormalizedRequestedVersion { get; }

        public static Yme009TargetSoftwareVersionDecision Allow(string normalizedRequestedVersion)
        {
            return new Yme009TargetSoftwareVersionDecision(
                true,
                Yme009TargetSoftwareVersionRejection.None,
                null,
                normalizedRequestedVersion);
        }

        public static Yme009TargetSoftwareVersionDecision RejectMissing()
        {
            return new Yme009TargetSoftwareVersionDecision(
                false,
                Yme009TargetSoftwareVersionRejection.Missing,
                null,
                null);
        }

        public static Yme009TargetSoftwareVersionDecision RejectConflict(string establishedVersion)
        {
            return new Yme009TargetSoftwareVersionDecision(
                false,
                Yme009TargetSoftwareVersionRejection.Conflict,
                establishedVersion,
                null);
        }
    }
}
