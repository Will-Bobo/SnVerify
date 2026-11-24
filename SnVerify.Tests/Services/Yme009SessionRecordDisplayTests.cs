using System;
using System.Collections.Generic;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;
using SnVerify.Services.Yme009;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009SessionRecordDisplayTests
    {
        [Test]
        public void Should_ProjectStoredFields_When_RecordIsSaved()
        {
            var row = Yme009SessionRecordDisplay.Create(new TestRecord
            {
                Id = 8,
                StickerSN = "12",
                DeviceSN = "msb2116a0vm58b6r",
                SnReadStatus = "Ok",
                ActualVersion = "KME009_V1",
                VersionReadStatus = "Ok",
                ExpectedVersion = "KME009_V1",
                Imei1 = "111",
                Imei1ReadStatus = "Ok",
                Imei2 = "222",
                Imei2ReadStatus = "Ok",
                WifiMac = "AA:BB",
                WifiMacReadStatus = "Ok",
                CitValue = "0",
                CitReadStatus = "Ok",
                AgingRebootValue = string.Empty,
                AgingRebootReadStatus = "Missing",
                AgingResetValue = string.Empty,
                AgingResetReadStatus = "Missing",
                SnJudgment = "Fail",
                VersionJudgment = "Pass",
                CitJudgment = "Fail",
                AgingRebootJudgment = "Fail",
                AgingResetJudgment = "Fail",
                Result = "FAIL",
                FailReason = Yme009FailReasonCodes.RequiredValueMissing,
                VerifyTime = new DateTime(2026, 11, 23, 21, 40, 0)
            });

            Assert.That(row.Id, Is.EqualTo(8));
            Assert.That(row.VerifyTime, Is.EqualTo("2026-11-23 21:40:00"));
            Assert.That(row.StickerSn, Is.EqualTo("12"));
            Assert.That(row.DeviceSn, Is.EqualTo("msb2116a0vm58b6r（Ok）"));
            Assert.That(row.ActualVersion, Is.EqualTo("KME009_V1（Ok）"));
            Assert.That(row.ExpectedVersion, Is.EqualTo("KME009_V1"));
            Assert.That(row.Imei1, Is.EqualTo("111（Ok）"));
            Assert.That(row.CitValue, Is.EqualTo("0（Ok）"));
            Assert.That(row.SnJudgment, Is.EqualTo("FAIL"));
            Assert.That(row.SnReason, Is.EqualTo(Yme009FailReasonCodes.ItemSnMismatch));
            Assert.That(row.VersionJudgment, Is.EqualTo("PASS"));
            Assert.That(row.VersionReason, Is.Empty);
            Assert.That(row.CitJudgment, Is.EqualTo("FAIL"));
            Assert.That(row.CitReason, Is.EqualTo(Yme009FailReasonCodes.ItemTestFailed));
            Assert.That(row.AgingRebootValue, Is.EqualTo("（空）（Missing）"));
            Assert.That(row.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemUntested));
            Assert.That(row.Result, Is.EqualTo("FAIL"));
            Assert.That(row.FailReason, Is.EqualTo(FailReasonTextResolver.Resolve(Yme009FailReasonCodes.RequiredValueMissing)));
        }

        [Test]
        public void Should_DistinguishNullFromEmptyString_When_ValueIsDisplayed()
        {
            var row = Yme009SessionRecordDisplay.Create(new TestRecord
            {
                Id = 3,
                DeviceSN = null,
                SnReadStatus = "TimedOut",
                SnJudgment = "Fail",
                CitValue = string.Empty,
                CitReadStatus = "Missing",
                CitJudgment = "Fail",
                AgingRebootValue = null,
                AgingRebootReadStatus = "CommandFailed",
                AgingRebootJudgment = "Fail",
                Result = "FAIL",
                VerifyTime = new DateTime(2026, 11, 23, 12, 0, 0)
            });

            Assert.That(row.DeviceSn, Is.EqualTo("未取得（TimedOut）"));
            Assert.That(row.SnReason, Is.EqualTo(Yme009FailReasonCodes.ItemReadFailed));
            Assert.That(row.CitValue, Is.EqualTo("（空）（Missing）"));
            Assert.That(row.CitReason, Is.EqualTo(Yme009FailReasonCodes.ItemUntested));
            Assert.That(row.AgingRebootValue, Is.EqualTo("未取得（CommandFailed）"));
            Assert.That(row.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemReadFailed));
        }

        [Test]
        public void Should_KeepSeparateRows_When_SameSnIsTestedAgain()
        {
            var rows = Yme009SessionRecordDisplay.Project(new List<TestRecord>
            {
                Row(2, "SN-1", new DateTime(2026, 11, 23, 12, 0, 2)),
                Row(1, "SN-1", new DateTime(2026, 11, 23, 12, 0, 1))
            });

            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Id, Is.EqualTo(1));
            Assert.That(rows[1].Id, Is.EqualTo(2));
            Assert.That(rows[0].StickerSn, Is.EqualTo("SN-1"));
            Assert.That(rows[1].StickerSn, Is.EqualTo("SN-1"));
        }

        [Test]
        public void Should_OrderById_When_VerifyTimeIsEqual()
        {
            var time = new DateTime(2026, 11, 23, 12, 0, 0);
            var rows = Yme009SessionRecordDisplay.Project(new List<TestRecord>
            {
                Row(9, "SN-B", time),
                Row(4, "SN-A", time)
            });

            Assert.That(rows[0].Id, Is.EqualTo(4));
            Assert.That(rows[1].Id, Is.EqualTo(9));
        }

        [Test]
        public void Should_RestoreItemReasons_When_StoredStatusMatchesConfirmedRules()
        {
            var row = Yme009SessionRecordDisplay.Create(new TestRecord
            {
                Id = 5,
                SnReadStatus = "Missing",
                SnJudgment = "Fail",
                VersionReadStatus = "Ok",
                VersionJudgment = "Fail",
                CitReadStatus = "ProtocolInvalid",
                CitValue = "2",
                CitJudgment = "Fail",
                AgingRebootReadStatus = "Disconnected",
                AgingRebootJudgment = "Fail",
                AgingResetReadStatus = "Ok",
                AgingResetValue = "1",
                AgingResetJudgment = "Fail",
                Result = "FAIL",
                VerifyTime = new DateTime(2026, 11, 23, 12, 0, 0)
            });

            Assert.That(row.SnReason, Is.EqualTo(Yme009FailReasonCodes.ItemRequiredMissing));
            Assert.That(row.VersionReason, Is.EqualTo(Yme009FailReasonCodes.ItemVersionMismatch));
            Assert.That(row.CitReason, Is.EqualTo(Yme009FailReasonCodes.ItemUnrecognized));
            Assert.That(row.AgingRebootReason, Is.EqualTo(Yme009FailReasonCodes.ItemReadFailed));
            Assert.That(row.AgingResetJudgment, Is.EqualTo("FAIL"));
            Assert.That(row.AgingResetReason, Is.Empty);
        }

        private static TestRecord Row(int id, string stickerSn, DateTime verifyTime)
        {
            return new TestRecord
            {
                Id = id,
                StickerSN = stickerSn,
                Result = "PASS",
                VerifyTime = verifyTime,
                SnJudgment = "Pass"
            };
        }
    }
}
