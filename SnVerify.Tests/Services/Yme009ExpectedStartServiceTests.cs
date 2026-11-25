using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Domain.State;
using SnVerify.Services.ExpectedData;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009ExpectedStartServiceTests
    {
        private StorageService _storage;
        private Yme009ExpectedDataService _service;
        private string _dbPath;
        private string _directory;

        [SetUp]
        public async Task SetUp()
        {
            var token = Guid.NewGuid().ToString("N");
            _dbPath = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Start_" + token + ".db");
            _directory = Path.Combine(Path.GetTempPath(), "SnVerify_Yme009Start_" + token);
            Directory.CreateDirectory(_directory);
            _storage = new StorageService(_dbPath);
            await _storage.InitializeAsync();
            _service = new Yme009ExpectedDataService(_storage);
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
        public async Task Should_StageFileNameAndCount_When_WorkbookPassesPrecheck()
        {
            var path = Workbook(Row("SN-1"), Row("SN-2"));

            var staged = await _service.StageAsync(path);

            Assert.That(staged.Status, Is.EqualTo(Yme009ExpectedReadStatus.Accepted));
            Assert.That(staged.File.FileName, Is.EqualTo(Path.GetFileName(path)));
            Assert.That(staged.File.RecordCount, Is.EqualTo(2));
            Assert.That(staged.File.PrecheckPassed, Is.True);
            Assert.That(staged.File.Sha256, Is.EqualTo(Yme009ExpectedWorkbook.ComputeSha256(path)));
            Assert.That(Count("SELECT COUNT(*) FROM TestSession"), Is.EqualTo(0));
        }

        [Test]
        public async Task Should_RejectStaging_When_HeaderMissingOrValueEmptyOrSnRepeats()
        {
            var missing = Workbook(new[] { "SN", "WIFIMAC", "IMEI1" }, new[] { new[] { "SN-1", "AA", "1" } });
            var empty = Workbook(new[] { "SN-1", "AA", "", "2" });
            var duplicate = Workbook(Row("SN-1"), Row("SN-1"));

            var missingResult = await _service.StageAsync(missing);
            var emptyResult = await _service.StageAsync(empty);
            var duplicateResult = await _service.StageAsync(duplicate);

            Assert.That(missingResult.File, Is.Null);
            Assert.That(missingResult.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredHeaderMissing));
            Assert.That(emptyResult.File, Is.Null);
            Assert.That(emptyResult.Reason, Is.EqualTo(Yme009ExpectedReadReasons.RequiredValueMissing));
            Assert.That(duplicateResult.File, Is.Null);
            Assert.That(duplicateResult.Reason, Is.EqualTo(Yme009ExpectedReadReasons.SnDuplicate));
        }

        [Test]
        public async Task Should_LeaveHeaderOnlyUnresolved_When_WorkbookHasNoDeviceRows()
        {
            var path = Workbook();

            var staged = await _service.StageAsync(path);

            Assert.That(staged.Status, Is.EqualTo(Yme009ExpectedReadStatus.Unresolved));
            Assert.That(staged.Reason, Is.EqualTo(Yme009ExpectedReadReasons.UnresolvedNoDeviceRows));
            Assert.That(staged.File, Is.Null);
        }

        [Test]
        public async Task Should_FailBeforeAnySession_When_StagedFileDisappears()
        {
            var path = Workbook(Row("SN-1"));
            var staged = await _service.StageAsync(path);
            File.Delete(path);

            var started = await _service.StartAsync(Request(staged.File));

            Assert.That(started.Committed, Is.False);
            Assert.That(started.Reason, Is.EqualTo(Yme009ExpectedReadReasons.FileUnreadable));
            Assert.That(Count("SELECT COUNT(*) FROM TestSession"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(0));
        }

        [Test]
        public async Task Should_FailBeforeAnySession_When_FingerprintChanges()
        {
            var path = Workbook(Row("SN-1"));
            var staged = await _service.StageAsync(path);
            Yme009ExpectedWorkbookFiles.Write(path, Yme009ExpectedWorkbookFiles.RequiredHeaders, Row("SN-2"));

            var started = await _service.StartAsync(Request(staged.File));

            Assert.That(started.Committed, Is.False);
            Assert.That(started.Reason, Is.EqualTo(Yme009ExpectedReadReasons.FingerprintMismatch));
            Assert.That(started.OperatorMessage, Is.EqualTo(Yme009ExpectedOperatorText.FingerprintMismatch));
            Assert.That(Count("SELECT COUNT(*) FROM TestSession"), Is.EqualTo(0));
        }

        [Test]
        public async Task Should_CommitSessionBeforeActivation_When_FingerprintMatches()
        {
            var path = Workbook(Row("SN-1"), Row("SN-2"));
            var staged = await _service.StageAsync(path);
            var lifecycle = new SessionLifecycleService(_storage);

            var started = await _service.StartAsync(Request(staged.File));

            Assert.That(started.Committed, Is.True);
            Assert.That(started.RecordCount, Is.EqualTo(2));
            Assert.That(lifecycle.Snapshot.IsActive, Is.False);
            var sessions = await _storage.GetSessionsByOrderIdAsync(OrderId());
            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(sessions[0].Status, Is.EqualTo(Yme009SessionStatus.Running));
            Assert.That(sessions[0].EndTime, Is.Null);
            Assert.That(sessions[0].SessionName, Is.EqualTo(started.SessionName));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDevice"), Is.EqualTo(2));

            lifecycle.StartSession(started.SessionName, "ORD");

            Assert.That(lifecycle.Snapshot.IsActive, Is.True);
            Assert.That(lifecycle.Snapshot.SessionId, Is.EqualTo(started.SessionName));
            Assert.That(lifecycle.Snapshot.OrderId, Is.EqualTo("ORD"));
        }

        [Test]
        public async Task Should_KeepRunningSessionAndRejectReplacement_When_ProcessDoesNotEndIt()
        {
            var first = Workbook(Row("SN-1"));
            var staged = await _service.StageAsync(first);
            var started = await _service.StartAsync(Request(staged.File));
            Execute("INSERT INTO TestRecord (SessionId, StickerSN, Result, VerifyTime) VALUES (" + started.SessionId + ", 'SN-1', 'PASS', '2026-11-25 10:00:00')");

            var again = await _service.StartAsync(Request(staged.File));
            var running = await _storage.GetYme009RunningSessionsAsync();

            Assert.That(again.Committed, Is.False);
            Assert.That(again.Reason, Is.EqualTo(Yme009ExpectedReadReasons.SessionStillRunning));
            Assert.That(again.OperatorMessage, Is.EqualTo(Yme009ExpectedOperatorText.SessionStillRunning));
            Assert.That(running, Has.Count.EqualTo(1));
            Assert.That(running[0].SessionId, Is.EqualTo(started.SessionId));
            Assert.That(running[0].SessionName, Is.EqualTo(started.SessionName));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _storage.EnsureYme009ExpectedDataReplaceAllowedAsync(started.SessionId));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
            Assert.That(Scalar("SELECT Result FROM TestRecord"), Is.EqualTo("PASS"));
        }

        [Test]
        public async Task Should_KeepOldDataSet_When_CompletedSessionStartsAnotherTask()
        {
            var firstPath = Workbook(Row("SN-1"));
            var firstStaged = await _service.StageAsync(firstPath);
            var first = await _service.StartAsync(Request(firstStaged.File));
            var firstFile = Scalar("SELECT FileName FROM Yme009ExpectedDataSet WHERE SessionId = " + first.SessionId) as string;
            Execute("INSERT INTO TestRecord (SessionId, StickerSN, Result, VerifyTime) VALUES (" + first.SessionId + ", 'SN-1', 'PASS', '2026-11-25 10:00:00')");

            await _storage.CompleteYme009SessionAsync(first.SessionName);

            var completed = await _storage.GetSessionsByOrderIdAsync(OrderId());
            Assert.That(completed[0].Status, Is.EqualTo(Yme009SessionStatus.Completed));
            Assert.That(completed[0].EndTime, Is.Not.Null);
            Assert.That(await _storage.GetYme009RunningSessionsAsync(), Is.Empty);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _storage.EnsureYme009ExpectedDataReplaceAllowedAsync(first.SessionId));

            var secondPath = Workbook(Row("SN-2"));
            var secondStaged = await _service.StageAsync(secondPath);
            var second = await _service.StartAsync(Request(secondStaged.File));

            Assert.That(second.Committed, Is.True);
            Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId));
            Assert.That(Scalar("SELECT FileName FROM Yme009ExpectedDataSet WHERE SessionId = " + first.SessionId), Is.EqualTo(firstFile));
            Assert.That(Scalar("SELECT Status FROM TestSession WHERE Id = " + first.SessionId), Is.EqualTo(Yme009SessionStatus.Completed));
            Assert.That(Scalar("SELECT Result FROM TestRecord WHERE SessionId = " + first.SessionId), Is.EqualTo("PASS"));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(2));
        }

        [Test]
        public async Task Should_IgnoreHistoricalNullSession_When_QueryingRunningTasks()
        {
            var path = Workbook(Row("SN-1"));
            var staged = await _service.StageAsync(path);
            var started = await _service.StartAsync(Request(staged.File));
            Execute("UPDATE TestSession SET Status = NULL, EndTime = NULL WHERE Id = " + started.SessionId);

            var running = await _storage.GetYme009RunningSessionsAsync();

            Assert.That(running, Is.Empty);
            Assert.That(Scalar("SELECT Status FROM TestSession WHERE Id = " + started.SessionId), Is.Null.Or.EqualTo(DBNull.Value));
            Assert.That(Scalar("SELECT EndTime FROM TestSession WHERE Id = " + started.SessionId), Is.Null.Or.EqualTo(DBNull.Value));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(1));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDevice"), Is.EqualTo(1));
            Assert.DoesNotThrowAsync(async () => await _storage.EnsureYme009ExpectedDataReplaceAllowedAsync(started.SessionId));
        }

        [Test]
        public async Task Should_RollbackSession_When_ImportWriteFails()
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + _dbPath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
CREATE TRIGGER yme009_fail_device
BEFORE INSERT ON Yme009ExpectedDevice
BEGIN
    SELECT RAISE(ABORT, 'forced write failure');
END;";
                    command.ExecuteNonQuery();
                }
            }

            var path = Workbook(Row("SN-1"));
            var staged = await _service.StageAsync(path);
            var started = await _service.StartAsync(Request(staged.File));

            Assert.That(started.Committed, Is.False);
            Assert.That(started.Reason, Is.EqualTo(Yme009ExpectedReadReasons.WriteFailed));
            Assert.That(Count("SELECT COUNT(*) FROM TestSession"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDataSet"), Is.EqualTo(0));
            Assert.That(Count("SELECT COUNT(*) FROM Yme009ExpectedDevice"), Is.EqualTo(0));
        }

        private Yme009ExpectedStartRequest Request(Yme009ExpectedFileStaging file)
        {
            return new Yme009ExpectedStartRequest
            {
                ProjectId = "P",
                OrderId = "ORD",
                ProductCode = "YME009",
                File = file
            };
        }

        private int OrderId()
        {
            return Convert.ToInt32(Scalar("SELECT Id FROM [Order]"));
        }

        private string Workbook(params string[][] rows)
        {
            return Workbook(Yme009ExpectedWorkbookFiles.RequiredHeaders, rows);
        }

        private string Workbook(string[] headers, string[][] rows)
        {
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".xlsx");
            Yme009ExpectedWorkbookFiles.Write(path, headers, rows);
            return path;
        }

        private static string[] Row(string sn)
        {
            return new[] { sn, "48:C8:62:16:BF:1F", "111111111111111", "222222222222222" };
        }

        private void Execute(string sql)
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + _dbPath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }

        private int Count(string sql)
        {
            return Convert.ToInt32(Scalar(sql));
        }

        private object Scalar(string sql)
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + _dbPath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteScalar();
                }
            }
        }
    }
}
