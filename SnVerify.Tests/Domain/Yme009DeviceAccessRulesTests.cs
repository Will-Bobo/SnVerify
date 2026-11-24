using System;
using System.Collections.Generic;
using NUnit.Framework;
using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class Yme009DeviceAccessRulesTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Should_MatchCdcAcmWithoutUsingReferenceVidOrParentName_When_PortIsPresent()
        {
            var compatible = new[] { @"USB\Class_02&SubClass_02&Prot_01" };
            var matched = Yme009CdcAcmFilter.TryMatch(@"USB\INSTANCE-A", "COM4", compatible, out var candidate);

            Assert.That(matched, Is.True);
            Assert.That(candidate.IdentityKey, Is.EqualTo(@"USB\INSTANCE-A"));
            Assert.That(candidate.PortName, Is.EqualTo("COM4"));
            Assert.That(Yme009CdcAcmFilter.TryMatch(@"USB\VID_0E8D&PID_0003", "COM4", new[] { "USB\\VID_0E8D" }, out _), Is.False);
            Assert.That(Yme009CdcAcmFilter.TryMatch(@"USB\INSTANCE-A", "COM4", new[] { "M6x-66B" }, out _), Is.False);
            Assert.That(Yme009CdcAcmFilter.TryMatch(string.Empty, "COM4", compatible, out _), Is.False);
            Assert.That(Yme009CdcAcmFilter.TryMatch(@"USB\INSTANCE-A", "LPT1", compatible, out _), Is.False);
        }

        [Test]
        public void Should_RequireSameDeviceThreeTimes_When_GateConfirms()
        {
            var gate = new Yme009CdcUniquenessGate(Start, TimeSpan.FromSeconds(30));
            var same = One("A", "COM3");

            Assert.That(gate.Apply(same, Start).Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(same, Start).RescanImmediately, Is.True);
            var confirmed = gate.Apply(same, Start);
            Assert.That(confirmed.Action, Is.EqualTo(Yme009GateAction.Confirmed));
            Assert.That(confirmed.IdentityKey, Is.EqualTo("A"));
        }

        [Test]
        public void Should_RestartConfirmation_When_SecondDeviceIsDifferent()
        {
            var gate = new Yme009CdcUniquenessGate(Start, TimeSpan.FromSeconds(30));

            gate.Apply(One("A", "COM3"), Start);
            var changed = gate.Apply(One("B", "COM8"), Start);

            Assert.That(changed.Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(One("B", "COM8"), Start).Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(One("B", "COM8"), Start).Action, Is.EqualTo(Yme009GateAction.Confirmed));
        }

        [Test]
        public void Should_ReturnToObservation_When_PrecheckIsNoLongerTheSameDevice()
        {
            var gate = new Yme009CdcUniquenessGate(Start, TimeSpan.FromSeconds(30));
            gate.Apply(One("A", "COM3"), Start);
            gate.Apply(One("A", "COM3"), Start);

            var precheck = gate.Apply(One("B", "COM9"), Start.AddSeconds(1));

            Assert.That(precheck.Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(One("B", "COM9"), Start.AddSeconds(1)).Action, Is.Not.EqualTo(Yme009GateAction.Confirmed));
        }

        [Test]
        public void Should_ClearPendingConfirmation_When_CountIsNotOne()
        {
            var gate = new Yme009CdcUniquenessGate(Start, TimeSpan.FromSeconds(30));
            gate.Apply(One("A", "COM3"), Start);

            var many = Yme009CdcScan.Found(new[]
            {
                new Yme009CdcCandidate("A", "COM3"),
                new Yme009CdcCandidate("B", "COM4")
            });
            Assert.That(gate.Apply(many, Start).Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(Yme009CdcScan.Found(Array.Empty<Yme009CdcCandidate>()), Start).RescanImmediately, Is.False);
            Assert.That(gate.Apply(Yme009CdcScan.Failed(), Start).Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(One("A", "COM3"), Start).Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
        }

        [Test]
        public void Should_RejectSingleCandidateWithoutIdentity_When_ComparingDevices()
        {
            var gate = new Yme009CdcUniquenessGate(Start, TimeSpan.FromSeconds(30));
            var nameless = Yme009CdcScan.Found(new[] { new Yme009CdcCandidate("  ", "COM3") });

            Assert.That(gate.Apply(nameless, Start).Action, Is.EqualTo(Yme009GateAction.KeepWaiting));
            Assert.That(gate.Apply(nameless, Start).Action, Is.Not.EqualTo(Yme009GateAction.Confirmed));
        }

        [Test]
        public void Should_TimeOut_When_WindowElapsesBeforeConfirmation()
        {
            var gate = new Yme009CdcUniquenessGate(Start, TimeSpan.FromSeconds(30));

            var decision = gate.Apply(One("A", "COM3"), Start.AddSeconds(30));

            Assert.That(decision.Action, Is.EqualTo(Yme009GateAction.TimedOut));
        }

        [Test]
        public void Should_ClassifyAtSuccessOnlyAcrossSeparateLines_When_ResponseArrives()
        {
            Assert.That(Yme009AtResponseClassifier.Classify(false, "OK"), Is.EqualTo(Yme009AtResult.NotSent));
            Assert.That(
                Yme009AtResponseClassifier.Classify(true, "SIMO AT COMMAND RESULT:\r\nOK\r\n"),
                Is.EqualTo(Yme009AtResult.Succeeded));
            Assert.That(
                Yme009AtResponseClassifier.Classify(true, "SIMO AT COMMAND RESULT: OK"),
                Is.EqualTo(Yme009AtResult.Uncertain));
            Assert.That(Yme009AtResponseClassifier.Classify(true, string.Empty), Is.EqualTo(Yme009AtResult.Uncertain));
        }

        [Test]
        public void Should_TreatProcessListingAsNotReady_When_NoDeviceStateIsPresent()
        {
            var devices = Yme009AdbDeviceListParser.Parse(
                "List of devices attached\r\nabc offline transport_id:3\r\ndef device product:demo transport_id:9\r\n");

            Assert.That(devices, Has.Count.EqualTo(2));
            Assert.That(devices[1].TransportId, Is.EqualTo("9"));
            Assert.That(devices[1].IsCommunicable, Is.True);
            Assert.That(Yme009AdbDeviceListParser.HasCommunicableDevice(devices), Is.True);
            Assert.That(
                Yme009AdbDeviceListParser.HasCommunicableDevice(
                    Yme009AdbDeviceListParser.Parse("List of devices attached\r\n")),
                Is.False);
        }

        [Test]
        public void Should_PromoteOnlyProvenAssociation_When_BuildingFormalContext()
        {
            var connection = Yme009DeviceConnection.Temporary("gen-1", "CDC-A");

            Assert.That(connection.TryPromote(Yme009AssociationResult.Unproven()), Is.False);
            Assert.That(connection.AllowsBusinessUse, Is.False);
            Assert.That(connection.TryAcceptUniqueStationTransport(" ", "SER"), Is.False);
            Assert.That(connection.TryAcceptUniqueStationTransport("0", "SER"), Is.False);
            Assert.That(Yme009AdbDeviceListParser.IsStationSelectable("9"), Is.True);
            Assert.That(connection.TryPromote(Yme009AssociationResult.Proven("9", "SER")), Is.True);
            Assert.That(connection.BuildDirectedArguments("version"), Is.EqualTo("-t 9 version"));
            Assert.That(() => connection.BuildDirectedArguments("-s other version"), Throws.InvalidOperationException);

            connection.Revoke();
            Assert.That(connection.AllowsBusinessUse, Is.False);
            Assert.That(connection.TransportId, Is.EqualTo("9"));
        }

        [Test]
        public void Should_SkipClose_When_AtWasNotConfirmed()
        {
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.WaitTimedOut, false, true), Is.False);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.AtNotSent, false, true), Is.False);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.AtUncertain, false, true), Is.False);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.StoppedBeforeAt, false, true), Is.False);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.ReadyFailed, true, true), Is.True);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.AssociationFailed, true, true), Is.True);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.AccessSucceeded, true, true), Is.True);
            Assert.That(Yme009AdbClosePolicy.ShouldAttemptClose(Yme009AccessStop.ReadyFailed, true, false), Is.False);
            Assert.That(Yme009AdbClosePolicy.PreferFormalContext(true), Is.True);
            Assert.That(Yme009AdbClosePolicy.PreferFormalContext(false), Is.False);
        }

        private static Yme009CdcScan One(string identity, string port)
        {
            return Yme009CdcScan.Found(new List<Yme009CdcCandidate> { new Yme009CdcCandidate(identity, port) });
        }
    }
}
