using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Services.Yme009;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009WorkstationProjectionTests
    {
        [Test]
        public void AccessStopped_DoesNotShowPassOrFail()
        {
            var result = new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.WaitTimedOut, false, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "ignored");
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(snapshot.Phase, Is.EqualTo(Yme009RoundPhase.AccessStopped));
            Assert.That(snapshot.HasOverallResult, Is.False);
            Assert.That(snapshot.OverallText, Is.Empty);
            Assert.That(snapshot.Headline, Is.EqualTo(Yme009WorkstationText.AccessFailed));
            Assert.That(snapshot.SaveStatusText, Is.EqualTo(Yme009WorkstationText.NotSavedRecord));
        }

        [Test]
        public void ReadyFailed_UsesNotReadyPrompt()
        {
            var result = new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.ReadyFailed, true, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "ignored");
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(snapshot.Headline, Is.EqualTo(Yme009WorkstationText.NotReady));
            Assert.That(snapshot.HasOverallResult, Is.False);
        }

        [Test]
        public void PassSaved_ShowsPassAndFullDeviceValues()
        {
            var decision = PassDecision("DEVICE-1", "VER-1", "111111111111111", "222222222222222", "AA:BB:CC:DD:EE:FF");
            var result = new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                new Yme009CloseAttempt(Yme009CloseDisposition.Failed, Yme009AtResult.Uncertain, "secret"),
                true,
                "ignored",
                true,
                8,
                decision);
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(snapshot.Phase, Is.EqualTo(Yme009RoundPhase.Completed));
            Assert.That(snapshot.OverallText, Is.EqualTo("PASS"));
            Assert.That(snapshot.HasOverallResult, Is.True);
            Assert.That(snapshot.PrimaryReason, Is.Empty);
            Assert.That(snapshot.SaveStatusText, Is.EqualTo(Yme009WorkstationText.Saved));
            Assert.That(snapshot.CloseNotice, Is.EqualTo(Yme009WorkstationText.CloseFailed));
            Assert.That(snapshot.ActualVersionText, Is.EqualTo("VER-1"));
            Assert.That(snapshot.Imei1Text, Is.EqualTo("111111111111111"));
            Assert.That(snapshot.Imei2Text, Is.EqualTo("222222222222222"));
            Assert.That(snapshot.WifiMacText, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
            Assert.That(snapshot.VersionJudgment, Is.EqualTo("PASS"));
            Assert.That(snapshot.Headline, Does.Not.Contain("8"));
        }

        [Test]
        public void PassNotSaved_KeepsPass()
        {
            var result = new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "ignored",
                false,
                0,
                PassDecision("D", "V", "1", "2", "M"));
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(snapshot.OverallText, Is.EqualTo("PASS"));
            Assert.That(snapshot.SaveStatusText, Is.EqualTo(Yme009WorkstationText.SaveFailed));
        }

        [Test]
        public void EmptyInformation_ShowsNotRead_AndReadFailurePrompt()
        {
            var decision = Yme009InspectionDecision.Judged(
                Attributes(Ok(Yme009FieldKind.DeviceSn, "D"), Ok(Yme009FieldKind.SoftwareVersion, "V"),
                    Empty(Yme009FieldKind.Imei1), Empty(Yme009FieldKind.Imei2), Empty(Yme009FieldKind.WifiMac),
                    Empty(Yme009FieldKind.ProductionTest), Empty(Yme009FieldKind.AgingReboot), Empty(Yme009FieldKind.AgingReset)),
                "FAIL",
                Yme009FailReasonCodes.RequiredValueMissing,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Fail,
                null,
                null,
                Yme009FailReasonCodes.ItemUntested,
                Yme009FailReasonCodes.ItemUntested,
                Yme009FailReasonCodes.ItemUntested);
            var result = new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "ignored",
                true,
                3,
                decision);
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(snapshot.OverallText, Is.EqualTo("FAIL"));
            Assert.That(snapshot.ProcessPrompt, Is.EqualTo(Yme009WorkstationText.ReadException));
            Assert.That(snapshot.PrimaryReason, Is.EqualTo(Yme009FailReasonCodes.RequiredValueMissingText));
            Assert.That(snapshot.Imei1Text, Is.EqualTo(Yme009WorkstationText.NotRead));
            Assert.That(snapshot.CitJudgment, Is.EqualTo("FAIL"));
            Assert.That(snapshot.CitReason, Is.EqualTo(Yme009FailReasonCodes.ItemUntested));
        }

        [Test]
        public void Running_DoesNotShowPassOrFail()
        {
            var snapshot = Yme009WorkstationSnapshot.Running("SN-9", Yme009WorkstationText.Connecting);

            Assert.That(snapshot.Phase, Is.EqualTo(Yme009RoundPhase.Running));
            Assert.That(snapshot.HasOverallResult, Is.False);
            Assert.That(snapshot.Headline, Is.EqualTo(Yme009WorkstationText.Connecting));
            Assert.That(snapshot.SnJudgment, Is.EqualTo(Yme009WorkstationText.WaitingItem));
            Assert.That(snapshot.ScannedSn, Is.EqualTo("SN-9"));
        }

        private static Yme009InspectionDecision PassDecision(string device, string version, string imei1, string imei2, string mac)
        {
            return Yme009InspectionDecision.Judged(
                Attributes(
                    Ok(Yme009FieldKind.DeviceSn, device),
                    Ok(Yme009FieldKind.SoftwareVersion, version),
                    Ok(Yme009FieldKind.Imei1, imei1),
                    Ok(Yme009FieldKind.Imei2, imei2),
                    Ok(Yme009FieldKind.WifiMac, mac),
                    Ok(Yme009FieldKind.ProductionTest, "1"),
                    Ok(Yme009FieldKind.AgingReboot, "1"),
                    Ok(Yme009FieldKind.AgingReset, "1")),
                "PASS",
                null,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                null,
                null,
                null,
                null,
                null);
        }

        private static Yme009ParsedField Ok(Yme009FieldKind kind, string text)
        {
            return new Yme009ParsedField(kind, Yme009ReadStatus.Ok, text);
        }

        private static Yme009ParsedField Empty(Yme009FieldKind kind)
        {
            return new Yme009ParsedField(kind, Yme009ReadStatus.Missing, string.Empty);
        }

        private static Yme009ParsedAttributes Attributes(
            Yme009ParsedField device,
            Yme009ParsedField version,
            Yme009ParsedField imei1,
            Yme009ParsedField imei2,
            Yme009ParsedField mac,
            Yme009ParsedField cit,
            Yme009ParsedField reboot,
            Yme009ParsedField reset)
        {
            return new Yme009ParsedAttributes(device, version, imei1, imei2, mac, cit, reboot, reset);
        }
    }
}
