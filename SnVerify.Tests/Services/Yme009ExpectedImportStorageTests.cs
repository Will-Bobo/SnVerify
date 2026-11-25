using System;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.ExpectedData;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009ExpectedImportStorageTests
    {
        private StorageService _storage;
        private string _dbPath;
        private string _directory;
        private int _orderId;

        [SetUp]
        public async Task SetUp()
        {
            var token = Guid.NewGuid().ToString("N");
            _dbPath = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Expected_" + token + ".db");
            _directory = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Expected_" + token);
            Directory.CreateDirectory(_directory);
            _storage = new StorageService(_dbPath);
            await _storage.InitializeAsync();
            var productId = await _storage.CreateProductAsync(new Product
            {
                ProductName = "P",
                ProductCode = "YME009",
                CreatedAt = DateTime.Now
            });
            _orderId = await _storage.CreateOrderAsync(new Order
            {
                ProductId = productId,
                OrderName = "ORD",
                CreatedAt = DateTime.Now
            });
        }

        [TearDown]
        public void TearDown()
        {
            _storage?.Dispose();
            _storage = null;
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [Test]
        public async Task Should_CommitSessionDataSetAndDevices_When_FingerprintMatches()
        {
            var path = CreateWorkbook(Row("AbC"), Row("SN-2"));
            var precheck = Yme009ExpectedWorkbook.Read(path, null);

            var result = await ImportAsync(path, precheck.Sha256, "BATCH-1");

            Assert.That(result.Committed, Is.True);
            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(result.SessionId, Is.GreaterThan(0));
            Assert.That(result.RecordCount, Is.EqualTo(2));
            Assert.That(result.Sha256, Is.EqualTo(precheck.Sha256));

            var sessions = await _storage.GetSessionsByOrderIdAsync(_orderId);
            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(sessions[0].Id, Is.EqualTo(result.SessionId));
            Assert.That(sessions[0].Status, Is.Null);
            Assert.That(Count("SELECT COUNT(*) FROM TestRecord"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
            Assert.That(Scalar("SELECT FileName FROM Yme009ExpectedDataSet"), Is.EqualTo(Path.GetFileName(path)));
            Assert.That(Scalar("SELECT Sha256 FROM Yme009ExpectedDataSet"), Is.EqualTo(precheck.Sha256));

            var spaced = await _storage.FindYme009ExpectedDeviceAsync(result.SessionId, "  AbC  ");
            Assert.That(spaced.Found, Is.True);
            Assert.That(spaced.Device.Sn, Is.EqualTo("AbC"));
            Assert.That(spaced.Device.WifiMac, Is.EqualTo("48:C8:62:16:BF:1F"));
            Assert.That(spaced.Device.Imei1, Is.EqualTo("111111111111111"));
            Assert.That(spaced.Device.Imei2, Is.EqualTo("222222222222222"));
        }

        [Test]
        public async Task Should_Miss_When_SnCaseDiffersOrSessionDoesNotContainSn()
        {
            var path = CreateWorkbook(Row("AbC"));
            var hash = Yme009ExpectedWorkbook.Read(path, null).Sha256;
            var result = await ImportAsync(path, hash, "BATCH-CASE");

            var differentCase = await _storage.FindYme009ExpectedDeviceAsync(result.SessionId, "abc");
            var missing = await _storage.FindYme009ExpectedDeviceAsync(result.SessionId, "SN-2");
            var blank = await _storage.FindYme009ExpectedDeviceAsync(result.SessionId, "   ");

            Assert.That(differentCase.Found, Is.False);
            Assert.That(differentCase.Device, Is.Null);
            Assert.That(missing.Found, Is.False);
            Assert.That(blank.Found, Is.False);
        }

        [Test]
        public async Task Should_KeepSessionsIsolated_When_SameSnExistsInAnotherSession()
        {
            var firstPath = CreateWorkbook(new[] { "SN-1", "AA:01", "1", "2" });
            var secondPath = CreateWorkbook(new[] { "SN-1", "BB:02", "3", "4" });
            var first = await ImportAsync(firstPath, Yme009ExpectedWorkbook.Read(firstPath, null).Sha256, "BATCH-A");
            var second = await ImportAsync(secondPath, Yme009ExpectedWorkbook.Read(secondPath, null).Sha256, "BATCH-B");

            var fromFirst = await _storage.FindYme009ExpectedDeviceAsync(first.SessionId, "SN-1");
            var fromSecond = await _storage.FindYme009ExpectedDeviceAsync(second.SessionId, "SN-1");

            Assert.That(fromFirst.Device.WifiMac, Is.EqualTo("AA:01"));
            Assert.That(fromSecond.Device.WifiMac, Is.EqualTo("BB:02"));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(2));
        }

        [Test]
        public async Task Should_RejectSecondDataSet_When_SessionAlreadyHasOne()
        {
            var path = CreateWorkbook(Row("SN-1"));
            var imported = await ImportAsync(path, Yme009ExpectedWorkbook.Read(path, null).Sha256, "BATCH-UNIQUE");

            Assert.Throws<SQLiteException>(() => Execute(@"
INSERT INTO Yme009ExpectedDataSet (SessionId, FileName, RecordCount, Sha256, ImportedAt)
VALUES (" + imported.SessionId + ", 'other.xlsx', 1, 'abc', CURRENT_TIMESTAMP)"));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
        }

        [Test]
        public async Task Should_RejectDuplicateSn_When_SameDataSetAlreadyContainsSn()
        {
            var path = CreateWorkbook(Row("SN-1"));
            await ImportAsync(path, Yme009ExpectedWorkbook.Read(path, null).Sha256, "BATCH-SN");
            var dataSetId = Convert.ToInt32(Scalar("SELECT Id FROM Yme009ExpectedDataSet"));

            Assert.Throws<SQLiteException>(() => Execute(
                "INSERT INTO Yme009ExpectedDevice (DataSetId, Sn, WifiMac, Imei1, Imei2) VALUES ("
                + dataSetId + ", 'SN-1', 'AA', '1', '2')"));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDevice"), Is.EqualTo(1));
        }

        [Test]
        public async Task Should_CommitRows_When_ImportDoesNotRepeatEmptyFieldPrecheck()
        {
            var path = CreateWorkbook(Row("SN-1"), new[] { "SN-2", "AA", "", "2" });
            var precheck = Yme009ExpectedWorkbook.Read(path, null);
            var result = await ImportAsync(path, precheck.Sha256, "BATCH-EMPTY-FIELD");

            Assert.That(precheck.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(precheck.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredValueMissing));
            Assert.That(result.Committed, Is.True);
            Assert.That(result.RecordCount, Is.EqualTo(2));
            var second = await _storage.FindYme009ExpectedDeviceAsync(result.SessionId, "SN-2");
            Assert.That(second.Found, Is.True);
            Assert.That(second.Device.Imei1, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task Should_RollbackEverything_When_ImportHitsDuplicateSn()
        {
            var path = CreateWorkbook(Row("SN-1"), Row("SN-1"));
            var hash = Yme009ExpectedWorkbook.ReadForImport(path, null).Sha256;
            var result = await ImportAsync(path, hash, "BATCH-DUP");

            Assert.That(result.Committed, Is.False);
            Assert.That(result.SessionId, Is.EqualTo(0));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.WriteFailed));
            Assert.That(result.Reason, Is.Not.EqualTo(Yme009ExpectedReadReasons.SnDuplicate));
            AssertNoExpectedRows();
        }

        [Test]
        public async Task Should_RollbackEverything_When_FingerprintDoesNotMatchImportStream()
        {
            var path = CreateWorkbook(Row("SN-1"));
            var original = Yme009ExpectedWorkbook.Read(path, null).Sha256;
            Yme009ExpectedWorkbookFiles.Write(path, Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-2"));

            var result = await ImportAsync(path, original, "BATCH-HASH");

            Assert.That(result.Committed, Is.False);
            Assert.That(result.SessionId, Is.EqualTo(0));
            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.FingerprintMismatch));
            Assert.That(result.Sha256, Is.Not.EqualTo(original));
            Assert.That(result.Sha256, Is.EqualTo(Yme009ExpectedWorkbook.Read(path, null).Sha256));
            AssertNoExpectedRows();
        }

        [Test]
        public async Task Should_RollbackEverything_When_DeviceInsertFails()
        {
            Execute(@"
CREATE TRIGGER yme009_fail_second_device
BEFORE INSERT ON Yme009ExpectedDevice
WHEN (SELECT COUNT(*) FROM Yme009ExpectedDevice) >= 1
BEGIN
    SELECT RAISE(ABORT, 'forced write failure');
END;");
            var path = CreateWorkbook(Row("SN-1"), Row("SN-2"));
            var result = await ImportAsync(path, Yme009ExpectedWorkbook.Read(path, null).Sha256, "BATCH-WRITE");

            Assert.That(result.Committed, Is.False);
            Assert.That(result.SessionId, Is.EqualTo(0));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.WriteFailed));
            AssertNoExpectedRows();
        }

        [Test]
        public async Task Should_NotCommit_When_WorkbookHasNoDeviceRows()
        {
            var path = CreateWorkbook();
            var precheck = Yme009ExpectedWorkbook.Read(path, null);

            var result = await ImportAsync(path, precheck.Sha256, "BATCH-EMPTY");

            Assert.That(precheck.Status, Is.EqualTo(Yme009ExpectedReadStatus.Unresolved));
            Assert.That(result.Committed, Is.False);
            Assert.That(result.SessionId, Is.EqualTo(0));
            Assert.That(result.Status, Is.EqualTo(Yme009ExpectedReadStatus.Unresolved));
            Assert.That(result.Reason, Is.EqualTo(Yme009ExpectedReadReasons.UnresolvedNoDeviceRows));
            AssertNoExpectedRows();
        }

        [Test]
        public async Task Should_DistinguishMissingDataSetFromMissingDevice_When_SessionHasExpectedData()
        {
            Assert.That(await _storage.SessionHasYme009ExpectedDataSetAsync(1), Is.False);

            var path = CreateWorkbook(Row("AbC"));
            var result = await ImportAsync(path, Yme009ExpectedWorkbook.Read(path, null).Sha256, "BATCH-HAS");

            Assert.That(await _storage.SessionHasYme009ExpectedDataSetAsync(result.SessionId), Is.True);
            Assert.That(await _storage.SessionHasYme009ExpectedDataSetAsync(result.SessionId + 99), Is.False);
            var missing = await _storage.FindYme009ExpectedDeviceAsync(result.SessionId, "NO-SUCH");
            Assert.That(missing.Found, Is.False);
        }

        [Test]
        public void Should_ThrowInsteadOfReportingNotEnabled_When_ExpectedDataSetQueryFails()
        {
            Execute("DROP TABLE Yme009ExpectedDataSet");

            Assert.ThrowsAsync<SQLiteException>(async () =>
                await _storage.SessionHasYme009ExpectedDataSetAsync(1));
        }

        private async Task<Yme009ExpectedImportResult> ImportAsync(string path, string sha256, string sessionName)
        {
            return await _storage.ImportYme009ExpectedDataAsync(new Yme009ExpectedImportRequest
            {
                OrderId = _orderId,
                SessionName = sessionName,
                StartTime = new DateTime(2026, 11, 24, 9, 0, 0),
                FilePath = path,
                ExpectedSha256 = sha256
            });
        }

        private string CreateWorkbook(params string[][] rows)
        {
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".xlsx");
            Yme009ExpectedWorkbookFiles.Write(path, Yme009ExpectedWorkbookFiles.RequiredHeaders, rows);
            return path;
        }

        private static string[] Row(string sn)
        {
            return new[] { sn, "48:C8:62:16:BF:1F", "111111111111111", "222222222222222" };
        }

        private void AssertNoExpectedRows()
        {
            Assert.That(Count("SELECT COUNT(*) FROM TestSession"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDevice"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM TestRecord"), Is.EqualTo(0));
        }

        private int Count(string sql)
        {
            return Convert.ToInt32(Scalar(sql));
        }

        private object Scalar(string sql)
        {
            using (var connection = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteScalar();
                }
            }
        }

        private void Execute(string sql)
        {
            using (var connection = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
