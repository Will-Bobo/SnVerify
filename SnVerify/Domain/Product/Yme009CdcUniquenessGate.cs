using System;

namespace SnVerify.Domain.Product
{
    public enum Yme009GateAction
    {
        KeepWaiting = 0,
        Confirmed = 1,
        TimedOut = 2
    }

    public sealed class Yme009GateDecision
    {
        public Yme009GateDecision(Yme009GateAction action, string identityKey, bool rescanImmediately)
        {
            Action = action;
            IdentityKey = identityKey;
            RescanImmediately = rescanImmediately;
        }

        public Yme009GateAction Action { get; }

        public string IdentityKey { get; }

        public bool RescanImmediately { get; }
    }

    /// <summary>
    /// 30 秒窗口内的 CDC 唯一性门禁。连续两次必须是同一台设备，发送前还要再复核一次。
    /// </summary>
    public sealed class Yme009CdcUniquenessGate
    {
        private readonly DateTime _startedUtc;
        private readonly TimeSpan _window;
        private string _pendingIdentity;
        private bool _awaitingPrecheck;

        public Yme009CdcUniquenessGate(DateTime startedUtc, TimeSpan window)
        {
            _startedUtc = startedUtc;
            _window = window;
        }

        public string PendingIdentity
        {
            get { return _pendingIdentity ?? string.Empty; }
        }

        public bool IsAwaitingPrecheck
        {
            get { return _awaitingPrecheck; }
        }

        public Yme009GateDecision Apply(Yme009CdcScan scan, DateTime utcNow)
        {
            if (_awaitingPrecheck)
                return ApplyPrecheck(scan, utcNow);

            if (utcNow - _startedUtc >= _window)
                return Timeout();

            if (!TryGetSingleIdentity(scan, out var identity))
            {
                _pendingIdentity = null;
                return Wait(false);
            }

            if (!string.Equals(_pendingIdentity, identity, StringComparison.Ordinal))
            {
                _pendingIdentity = identity;
                return Wait(true);
            }

            _awaitingPrecheck = true;
            return Wait(true);
        }

        private Yme009GateDecision ApplyPrecheck(Yme009CdcScan scan, DateTime utcNow)
        {
            if (TryGetSingleIdentity(scan, out var identity)
                && string.Equals(identity, _pendingIdentity, StringComparison.Ordinal))
            {
                _awaitingPrecheck = false;
                return new Yme009GateDecision(Yme009GateAction.Confirmed, identity, false);
            }

            _awaitingPrecheck = false;
            if (utcNow - _startedUtc >= _window)
            {
                _pendingIdentity = null;
                return Timeout();
            }

            if (TryGetSingleIdentity(scan, out identity))
            {
                _pendingIdentity = identity;
                return Wait(true);
            }

            _pendingIdentity = null;
            return Wait(false);
        }

        private static bool TryGetSingleIdentity(Yme009CdcScan scan, out string identity)
        {
            identity = null;
            if (scan == null || scan.DiscoveryFailed || scan.Candidates.Count != 1)
                return false;

            var key = scan.Candidates[0].IdentityKey;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            identity = key;
            return true;
        }

        private static Yme009GateDecision Wait(bool rescanImmediately)
        {
            return new Yme009GateDecision(Yme009GateAction.KeepWaiting, null, rescanImmediately);
        }

        private static Yme009GateDecision Timeout()
        {
            return new Yme009GateDecision(Yme009GateAction.TimedOut, null, false);
        }
    }
}
