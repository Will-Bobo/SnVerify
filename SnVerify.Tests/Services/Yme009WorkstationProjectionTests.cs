using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Services.Yme009;
using SnVerify.Tests.Domain;

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
            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Exception));
            Assert.That(snapshot.SaveStatusText, Is.EqualTo(Yme009WorkstationText.NotSavedRecord));
            var display = Present(snapshot);
            Assert.That(display.Title, Is.EqualTo("设备接入失败"));
            Assert.That(display.Detail, Is.EqualTo("请检查设备连接"));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.AttentionColor));
            Assert.That(display.Title, Is.Not.EqualTo(Yme009WorkstationText.WaitingForScan));
            Assert.That(display.ScanSideText, Is.Empty);
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
            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Exception));
            Assert.That(snapshot.HasOverallResult, Is.False);
            var display = Present(snapshot);
            Assert.That(display.Title, Is.EqualTo("设备连接未就绪"));
            Assert.That(display.Detail, Is.EqualTo("请检查设备"));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.AttentionColor));
            Assert.That(display.ScanSideText, Is.Empty);
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
            Assert.That(snapshot.SaveStatusText, Is.EqualTo(Yme009WorkstationText.RecordNotSaved));
        }

        [Test]
        public void Should_ProjectIdentityStatesWithoutTreatingNeutralResultsAsFail()
        {
            var pass = Yme009AttributeSamples.Decide(new Yme009RawBuilder().Build());
            var snapshot = SnapshotOf(Yme009ExpectedIdentityDecision.Apply(pass, Yme009ExpectedIdentityCheck.NotEnabled()));
            Assert.That(snapshot.ExpectedIdentityJudgment, Is.EqualTo("未启用"));
            Assert.That(snapshot.ExpectedIdentityReason, Is.Empty);
            Assert.That(snapshot.OverallText, Is.EqualTo("PASS"));

            snapshot = SnapshotOf(Yme009ExpectedIdentityDecision.Apply(pass, Yme009ExpectedIdentityCheck.NotExecuted()));
            Assert.That(snapshot.ExpectedIdentityJudgment, Is.EqualTo("未执行"));
            Assert.That(snapshot.OverallText, Is.EqualTo("PASS"));

            snapshot = SnapshotOf(Yme009ExpectedIdentityDecision.Apply(
                pass,
                Yme009ExpectedIdentityCheck.Failed("不匹配字段：IMEI1")));
            Assert.That(snapshot.ExpectedIdentityJudgment, Is.EqualTo("FAIL"));
            Assert.That(snapshot.ExpectedIdentityReason, Is.EqualTo("不匹配字段：IMEI1"));
            Assert.That(snapshot.OverallText, Is.EqualTo("FAIL"));
            Assert.That(snapshot.PrimaryReason, Is.Empty);

            var duplicate = Yme009PassHistoryDecision.Apply(pass, "SN1", Yme009PassHistoryQueryDisposition.HistoricalPass);
            snapshot = SnapshotOf(Yme009ExpectedIdentityDecision.Apply(duplicate, Yme009ExpectedIdentityCheck.UnresolvedHistoricalPass()));
            Assert.That(snapshot.ExpectedIdentityJudgment, Is.Empty);
            Assert.That(snapshot.ExpectedIdentityReason, Is.Empty);
            Assert.That(snapshot.PrimaryReason, Is.EqualTo("设备SN已经检验"));

            snapshot = SnapshotOf(Yme009ExpectedIdentityDecision.Apply(pass, Yme009ExpectedIdentityCheck.UnresolvedQuery()));
            Assert.That(snapshot.ExpectedIdentityJudgment, Is.Empty);
            Assert.That(snapshot.Phase, Is.EqualTo(Yme009RoundPhase.Completed));
            Assert.That(snapshot.HasOverallResult, Is.True);
            Assert.That(snapshot.OverallText, Is.EqualTo(Yme009ResultClassifier.UnresolvedResult));
            Assert.That(snapshot.Headline, Is.EqualTo(Yme009ResultClassifier.UnresolvedResult));
            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Unresolved));
            Assert.That(snapshot.PrimaryReason, Is.Empty);
            Assert.That(snapshot.ProcessPrompt, Is.Not.EqualTo(Yme009WorkstationText.RoundIncomplete));
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
            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Exception));
            Assert.That(snapshot.Headline, Is.EqualTo(Yme009WorkstationText.ReadException));
            Assert.That(snapshot.ProcessPrompt, Is.EqualTo(Yme009WorkstationText.ReadException));
            var readDisplay = Present(snapshot);
            Assert.That(readDisplay.Title, Is.EqualTo(Yme009WorkstationText.ReadException));
            Assert.That(readDisplay.Detail, Is.EqualTo(Yme009FailReasonCodes.RequiredValueMissingText));
            Assert.That(readDisplay.Background, Is.EqualTo(Yme009StatusPresentation.AttentionColor));
            Assert.That(readDisplay.ScanSideText, Is.Empty);
            Assert.That(snapshot.PrimaryReason, Is.EqualTo(Yme009FailReasonCodes.RequiredValueMissingText));
            Assert.That(snapshot.Imei1Text, Is.EqualTo(Yme009WorkstationText.NotRead));
            Assert.That(snapshot.CitJudgment, Is.EqualTo("FAIL"));
            Assert.That(snapshot.CitReason, Is.EqualTo(Yme009FailReasonCodes.ItemUntested));
        }

        [Test]
        public void Should_ShowNeutralWaiting_When_NoRoundHasStarted()
        {
            var snapshot = Yme009WorkstationSnapshot.IdleWaiting();
            var display = Present(snapshot);

            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Neutral));
            Assert.That(display.Title, Is.EqualTo(Yme009WorkstationText.WaitingForScan));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.NeutralColor));
            Assert.That(display.ScanSideText, Is.Empty);
        }

        [Test]
        public void Should_ShowRunningStage_When_RoundIsRunning()
        {
            var snapshot = Yme009WorkstationSnapshot.Running("SN-9", Yme009WorkstationText.Reading);
            var display = Present(snapshot);

            Assert.That(display.Title, Is.EqualTo("检验中..."));
            Assert.That(display.Detail, Is.EqualTo(Yme009WorkstationText.Reading));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.RunningColor));
            Assert.That(display.ScanSideText, Is.EqualTo(display.Detail));
            Assert.That(display.ScanSideText, Is.Not.EqualTo(Yme009WorkstationText.WaitingForScan));
        }

        [Test]
        public void Should_ShowPassInGreen_When_ResultIsPass()
        {
            var display = Present(SnapshotOf(PassDecision("D", "V", "1", "2", "M")));

            Assert.That(display.Title, Is.EqualTo("PASS"));
            Assert.That(display.Detail, Is.EqualTo(Yme009WorkstationText.PassDetail));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.PassColor));
            Assert.That(display.ScanSideText, Is.Empty);
        }

        [Test]
        public void Should_ShowFailReasonInRed_When_ResultIsBusinessFail()
        {
            var decision = Yme009InspectionDecision.Judged(
                Attributes(
                    Ok(Yme009FieldKind.DeviceSn, "D"),
                    Ok(Yme009FieldKind.SoftwareVersion, "V"),
                    Ok(Yme009FieldKind.Imei1, "1"),
                    Ok(Yme009FieldKind.Imei2, "2"),
                    Ok(Yme009FieldKind.WifiMac, "M"),
                    Ok(Yme009FieldKind.ProductionTest, "0"),
                    Ok(Yme009FieldKind.AgingReboot, "1"),
                    Ok(Yme009FieldKind.AgingReset, "1")),
                "FAIL",
                Yme009FailReasonCodes.CitFail,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Fail,
                Yme009ItemJudgment.Pass,
                Yme009ItemJudgment.Pass,
                null,
                null,
                Yme009FailReasonCodes.ItemTestFailed,
                null,
                null);
            var snapshot = SnapshotOf(decision);
            var display = Present(snapshot);

            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Fail));
            Assert.That(display.Title, Is.EqualTo("FAIL"));
            Assert.That(display.Detail, Is.EqualTo(Yme009FailReasonCodes.CitFailText));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.FailColor));
            Assert.That(display.ScanSideText, Is.Empty);
        }

        [Test]
        public void Should_TruncateLongFailReason_When_PrimaryReasonExceedsTheCard()
        {
            var reason = new string('错', 40);
            var display = Yme009StatusPresentation.Map(
                Yme009HeadlineKind.Fail,
                "FAIL",
                string.Empty,
                reason,
                string.Empty,
                null);

            Assert.That(display.Detail, Is.EqualTo(new string('错', 36) + "…"));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.FailColor));
        }

        [Test]
        public void Should_ShowUnresolvedInOrange_When_IdentityIsUnresolved()
        {
            var snapshot = SnapshotOf(Yme009ExpectedIdentityDecision.Apply(
                PassDecision("D", "V", "1", "2", "M"),
                Yme009ExpectedIdentityCheck.UnresolvedQuery()));
            var display = Present(snapshot);

            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Unresolved));
            Assert.That(display.Title, Is.EqualTo(Yme009ResultClassifier.UnresolvedResult));
            Assert.That(display.Detail, Is.EqualTo(Yme009WorkstationText.UnresolvedDetail));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.AttentionColor));
            Assert.That(display.Background, Is.Not.EqualTo(Yme009StatusPresentation.NeutralColor));
            Assert.That(display.ScanSideText, Is.Empty);
        }

        [Test]
        public void Should_ShowRoundIncomplete_When_RoundFaults()
        {
            var snapshot = Yme009WorkstationSnapshot.Fault("SN-1");
            var display = Present(snapshot);

            Assert.That(snapshot.HeadlineKind, Is.EqualTo(Yme009HeadlineKind.Exception));
            Assert.That(display.Title, Is.EqualTo("检测未完成"));
            Assert.That(display.Detail, Is.EqualTo("记录未保存"));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.AttentionColor));
            Assert.That(display.Title, Is.Not.EqualTo(Yme009WorkstationText.WaitingForScan));
            Assert.That(display.ScanSideText, Is.Empty);
        }

        [Test]
        public void Should_KeepImportCompleteDistinctFromPass()
        {
            var display = Yme009StatusPresentation.Map(
                Yme009HeadlineKind.ImportComplete,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "已导入 3 条");

            Assert.That(display.Title, Is.EqualTo(Yme009ExpectedOperatorText.ImportDoneTitle));
            Assert.That(display.Detail, Is.EqualTo("已导入 3 条"));
            Assert.That(display.Background, Is.EqualTo(Yme009StatusPresentation.ImportCompleteColor));
            Assert.That(display.Background, Is.Not.EqualTo(Yme009StatusPresentation.PassColor));
        }

        private static Yme009StatusPresentation Present(Yme009WorkstationSnapshot snapshot)
        {
            return Yme009StatusPresentation.Map(
                snapshot.HeadlineKind,
                snapshot.Headline,
                snapshot.ProcessPrompt,
                snapshot.PrimaryReason,
                snapshot.ExpectedIdentityReason,
                null);
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

        private static Yme009WorkstationSnapshot SnapshotOf(Yme009InspectionDecision decision)
        {
            var result = new Yme009RoundResult(
                new Yme009AccessResult(Yme009AccessStop.AccessSucceeded, true, null, string.Empty),
                Yme009CloseAttempt.NotAttempted(),
                true,
                "ignored",
                true,
                1,
                decision);
            return Yme009WorkstationSnapshot.FromResult(result, "SN1");
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
