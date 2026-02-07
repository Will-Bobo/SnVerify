/// <author>AI Assistant</author>
/// <remarks>
/// A1 Domain 扩展：TestRecord 结构单元测试。
/// Record 类型由所属 Session.VerificationType 推断。
/// </remarks>

using System;
using NUnit.Framework;
using SnVerify.Domain.Models;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class TestRecordVerificationTypeTests
    {
        [Test]
        public void SnMatchStyleRecord_CanHaveNullExpectedVersionAndActualVersion()
        {
            var rec = new TestRecord
            {
                SessionId = 1,
                StickerSN = "STICK001",
                DeviceSN = "DEV001",
                Result = "PASS",
                VerifyTime = DateTime.Now,
                ExpectedVersion = null,
                ActualVersion = null
            };

            Assert.That(rec.ExpectedVersion, Is.Null);
            Assert.That(rec.ActualVersion, Is.Null);
            Assert.That(rec.StickerSN, Is.EqualTo("STICK001"));
            Assert.That(rec.DeviceSN, Is.EqualTo("DEV001"));
        }

        [Test]
        public void VersionMatchStyleRecord_CanHaveNullStickerSnAndDeviceSn()
        {
            var rec = new TestRecord
            {
                SessionId = 1,
                StickerSN = null,
                DeviceSN = null,
                Result = "PASS",
                VerifyTime = DateTime.Now,
                ExpectedVersion = "1.0.0",
                ActualVersion = "1.0.0"
            };

            Assert.That(rec.StickerSN, Is.Null);
            Assert.That(rec.DeviceSN, Is.Null);
            Assert.That(rec.ExpectedVersion, Is.EqualTo("1.0.0"));
            Assert.That(rec.ActualVersion, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void Record_CanSaveStickerSnDeviceSnExpectedVersionActualVersion()
        {
            var rec = new TestRecord
            {
                SessionId = 1,
                StickerSN = "S1",
                DeviceSN = "D1",
                ExpectedVersion = "1.0",
                ActualVersion = "1.0",
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            Assert.That(rec.StickerSN, Is.EqualTo("S1"));
            Assert.That(rec.DeviceSN, Is.EqualTo("D1"));
            Assert.That(rec.ExpectedVersion, Is.EqualTo("1.0"));
            Assert.That(rec.ActualVersion, Is.EqualTo("1.0"));
        }
    }
}
