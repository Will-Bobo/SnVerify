using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Storage;
using SnVerify.Services.Yme009;
using SnVerify.Tests.Domain;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009TestRecordStorageTests
    {
        private StorageService _storage;
        private string _dbPath;
        private int _sessionId;

        [SetUp]
        public async Task SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009_" + Guid.NewGuid().ToString("N") + ".db");
            _storage = new StorageService(_dbPath);
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P", ProductCode = "YME009", CreatedAt = DateTime.Now });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "ORD", CreatedAt = DateTime.Now });
            _sessionId = await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "ORD_20261123_120000", StartTime = DateTime.Now });
        }

        [TearDown]
        public void TearDown()
        {
            _storage?.Dispose();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Test]
        public async Task Should_SavePassFailAndReadExceptionAsSeparateRows_When_SameSnIsTestedAgain()
        {
            var started = new DateTime(2026, 11, 23, 12, 0, 0);
            var pass = Record("SN-1", PassRaw("SN-1"), started);
            var fail = Record("SN-1", new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded("SN-1")).ProductionTest(Yme009FieldRaw.Succeeded("0")).Build(), started.AddSeconds(1));
            var read = Record("SN-1", new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.TimedOut()).Build(), started.AddSeconds(2));

            await _storage.SaveTestRecordAsync(pass);
            await _storage.SaveTestRecordAsync(fail);
            await _storage.SaveTestRecordAsync(read);

            var rows = await _storage.GetTestRecordsBySessionAsync(_sessionId);
            Assert.That(rows.Select(item => item.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(rows[0].Id, Is.EqualTo(pass.Id));
            Assert.That(rows[0].Result, Is.EqualTo("PASS"));
            Assert.That(rows[0].DeviceSN, Is.EqualTo("SN-1"));
            Assert.That(rows[0].Imei1, Is.EqualTo("IMEI1"));
            Assert.That(rows[0].CitJudgment, Is.EqualTo("Pass"));
            Assert.That(rows[0].ChipId, Is.Null);
            Assert.That(rows[1].Result, Is.EqualTo("FAIL"));
            Assert.That(rows[1].FailReason, Is.EqualTo(Yme009FailReasonCodes.CitFail));
            Assert.That(rows[1].CitValue, Is.EqualTo("0"));
            Assert.That(rows[2].FailReason, Is.EqualTo(Yme009FailReasonCodes.ReadFail));
            Assert.That(rows[2].DeviceSN, Is.Null);
            Assert.That(rows[2].SnReadStatus, Is.EqualTo("TimedOut"));
            Assert.That(rows[2].SnJudgment, Is.EqualTo("Fail"));
            Assert.That(rows[0].DeviceSN, Is.EqualTo("SN-1"));
        }

        [Test]
        public async Task Should_SaveEmptyStringAndMissing_When_RequiredBinarySucceedsWithNoValue()
        {
            var record = Record(
                "SN-1",
                new Yme009RawBuilder()
                    .DeviceSn(Yme009FieldRaw.Succeeded("SN-1"))
                    .ProductionTest(Yme009FieldRaw.Succeeded(string.Empty))
                    .Build(),
                new DateTime(2026, 11, 23, 12, 0, 3));

            await _storage.SaveTestRecordAsync(record);
            var rows = await _storage.GetTestRecordsBySessionAsync(_sessionId);

            Assert.That(rows[0].Result, Is.EqualTo("FAIL"));
            Assert.That(rows[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.RequiredValueMissing));
            Assert.That(rows[0].CitValue, Is.EqualTo(string.Empty));
            Assert.That(rows[0].CitReadStatus, Is.EqualTo("Missing"));
            Assert.That(rows[0].CitJudgment, Is.EqualTo("Fail"));
            Assert.That(rows[0].SnJudgment, Is.EqualTo("Pass"));
        }

        [Test]
        public async Task Should_SaveNullRawValue_When_RequiredCommandFails()
        {
            var record = Record(
                "SN-1",
                new Yme009RawBuilder()
                    .DeviceSn(Yme009FieldRaw.Succeeded("SN-1"))
                    .ProductionTest(Yme009FieldRaw.CommandFailed())
                    .Build(),
                new DateTime(2026, 11, 23, 12, 0, 4));

            await _storage.SaveTestRecordAsync(record);
            var rows = await _storage.GetTestRecordsBySessionAsync(_sessionId);

            Assert.That(rows[0].Result, Is.EqualTo("FAIL"));
            Assert.That(rows[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.ReadFail));
            Assert.That(rows[0].CitValue, Is.Null);
            Assert.That(rows[0].CitReadStatus, Is.EqualTo("CommandFailed"));
            Assert.That(rows[0].CitJudgment, Is.EqualTo("Fail"));
        }

        [Test]
        public async Task Should_LeaveYme009ColumnsNull_When_RecordBelongsToAnotherProduct()
        {
            var record = new TestRecord
            {
                SessionId = _sessionId,
                StickerSN = "KM-SN",
                DeviceSN = "KM-SN",
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            await _storage.SaveTestRecordAsync(record);
            var rows = await _storage.GetTestRecordsBySessionAsync(_sessionId);

            Assert.That(record.Id, Is.GreaterThan(0));
            Assert.That(rows[0].DeviceSN, Is.EqualTo("KM-SN"));
            Assert.That(rows[0].Imei1, Is.Null);
            Assert.That(rows[0].CitValue, Is.Null);
            Assert.That(rows[0].SnReadStatus, Is.Null);
            Assert.That(rows[0].SnJudgment, Is.Null);
        }

        [Test]
        public async Task Should_NotCreateRecordOrAssignId_When_SaveIsRejected()
        {
            var record = new TestRecord { SessionId = _sessionId, StickerSN = "SN-1", Result = " ", VerifyTime = DateTime.Now };

            Assert.That(async () => await _storage.SaveTestRecordAsync(record), Throws.ArgumentException);
            Assert.That(record.Id, Is.EqualTo(0));
            Assert.That(await _storage.GetTestRecordsBySessionAsync(_sessionId), Is.Empty);
        }

        private TestRecord Record(string scannedSn, Yme009RawAttributeSet raw, DateTime verifyTime)
        {
            var decision = Yme009InspectionJudge.Judge(scannedSn, "SW-1", Yme009AttributeParser.Parse(raw));
            return Yme009TestRecordMapper.Create(_sessionId, scannedSn, "SW-1", decision, verifyTime);
        }

        private static Yme009RawAttributeSet PassRaw(string serial)
        {
            return new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded(serial)).Build();
        }
    }
}
