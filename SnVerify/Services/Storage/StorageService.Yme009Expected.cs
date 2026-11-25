using System;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using SnVerify.Domain.Product;
using SnVerify.Services.ExpectedData;

namespace SnVerify.Services.Storage
{
    public partial class StorageService
    {
        private const string CreateYme009ExpectedDataSetTable = @"
CREATE TABLE IF NOT EXISTS Yme009ExpectedDataSet (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId   INTEGER NOT NULL UNIQUE,
    FileName    TEXT,
    RecordCount INTEGER NOT NULL,
    Sha256      TEXT,
    ImportedAt  DATETIME,
    FOREIGN KEY (SessionId) REFERENCES TestSession(Id)
);";

        private const string CreateYme009ExpectedDeviceTable = @"
CREATE TABLE IF NOT EXISTS Yme009ExpectedDevice (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    DataSetId INTEGER NOT NULL,
    Sn        TEXT    NOT NULL,
    WifiMac   TEXT    NOT NULL,
    Imei1     TEXT    NOT NULL,
    Imei2     TEXT    NOT NULL,
    UNIQUE(DataSetId, Sn),
    FOREIGN KEY (DataSetId) REFERENCES Yme009ExpectedDataSet(Id)
);";

        private async Task CreateYme009ExpectedTablesAsync()
        {
            await ExecuteNonQueryAsync(CreateYme009ExpectedDataSetTable);
            await ExecuteNonQueryAsync(CreateYme009ExpectedDeviceTable);
        }

        public async Task<Yme009ExpectedImportResult> ImportYme009ExpectedDataAsync(Yme009ExpectedImportRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.OrderId <= 0)
                throw new ArgumentException("OrderId 必须大于 0", nameof(request));
            if (string.IsNullOrWhiteSpace(request.SessionName))
                throw new ArgumentException("SessionName 不能为空", nameof(request));
            if (string.IsNullOrWhiteSpace(request.FilePath))
                throw new ArgumentException("FilePath 不能为空", nameof(request));

            EnsureConnectionInitialized();
            return await Task.Run(() => ImportYme009ExpectedData(request));
        }

        public async Task<bool> SessionHasYme009ExpectedDataSetAsync(int sessionId)
        {
            if (sessionId <= 0)
                return false;

            EnsureConnectionInitialized();
            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");

                    const string sql = @"
SELECT 1
FROM Yme009ExpectedDataSet
WHERE SessionId = @SessionId
LIMIT 1";
                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        var value = command.ExecuteScalar();
                        return value != null && value != DBNull.Value;
                    }
                }
            });
        }

        public async Task<Yme009ExpectedDeviceLookup> FindYme009ExpectedDeviceAsync(int sessionId, string sn)
        {
            var key = Yme009TextComparison.Normalize(sn);
            if (sessionId <= 0 || key.Length == 0)
                return Yme009ExpectedDeviceLookup.NotFound;

            EnsureConnectionInitialized();
            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");

                    const string sql = @"
SELECT d.Sn, d.WifiMac, d.Imei1, d.Imei2
FROM Yme009ExpectedDevice d
INNER JOIN Yme009ExpectedDataSet s ON s.Id = d.DataSetId
WHERE s.SessionId = @SessionId AND d.Sn = @Sn
LIMIT 1";
                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@Sn", key);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                return Yme009ExpectedDeviceLookup.NotFound;

                            return Yme009ExpectedDeviceLookup.Hit(new Yme009ExpectedDeviceRow(
                                reader.GetString(0),
                                reader.GetString(1),
                                reader.GetString(2),
                                reader.GetString(3)));
                        }
                    }
                }
            });
        }

        private Yme009ExpectedImportResult ImportYme009ExpectedData(Yme009ExpectedImportRequest request)
        {
            lock (_lockObject)
            {
                if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    throw new InvalidOperationException("数据库连接未初始化或已关闭");

                SQLiteTransaction transaction = null;
                try
                {
                    transaction = _connection.BeginTransaction();
                    var sessionId = InsertSession(transaction, request);
                    var dataSetId = InsertDataSet(transaction, sessionId, request);
                    var read = Yme009ExpectedWorkbook.ReadForImport(request.FilePath, row => InsertDevice(transaction, dataSetId, row));
                    if (read.Reason == Yme009ExpectedReadReasons.FileUnreadable)
                    {
                        RollbackImport(ref transaction);
                        return new Yme009ExpectedImportResult(read.Status, read.Reason, false, 0, 0, read.Sha256);
                    }

                    if (!HashesEqual(read.Sha256, request.ExpectedSha256))
                    {
                        RollbackImport(ref transaction);
                        return new Yme009ExpectedImportResult(
                            Yme009ExpectedReadStatus.Rejected,
                            Yme009ExpectedReadReasons.FingerprintMismatch,
                            false,
                            0,
                            0,
                            read.Sha256);
                    }

                    if (read.Status != Yme009ExpectedReadStatus.Accepted)
                    {
                        RollbackImport(ref transaction);
                        return new Yme009ExpectedImportResult(read.Status, read.Reason, false, 0, 0, read.Sha256);
                    }

                    UpdateDataSet(transaction, dataSetId, read.RecordCount, read.Sha256);
                    transaction.Commit();
                    transaction.Dispose();
                    transaction = null;
                    return new Yme009ExpectedImportResult(
                        Yme009ExpectedReadStatus.Accepted,
                        null,
                        true,
                        sessionId,
                        read.RecordCount,
                        read.Sha256);
                }
                catch (Exception ex)
                {
                    RollbackImport(ref transaction);
                    _logger.LogError("YME009 预期数据导入失败，事务已回滚: " + ex.Message, ex);
                    if (ex is SQLiteException || ex is InvalidOperationException)
                    {
                        return new Yme009ExpectedImportResult(
                            Yme009ExpectedReadStatus.Rejected,
                            Yme009ExpectedReadReasons.WriteFailed,
                            false,
                            0,
                            0,
                            string.Empty);
                    }

                    if (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is XmlException)
                    {
                        return new Yme009ExpectedImportResult(
                            Yme009ExpectedReadStatus.Rejected,
                            Yme009ExpectedReadReasons.FileUnreadable,
                            false,
                            0,
                            0,
                            string.Empty);
                    }

                    throw;
                }
            }
        }

        private int InsertSession(SQLiteTransaction transaction, Yme009ExpectedImportRequest request)
        {
            const string sql = @"
INSERT INTO TestSession (SessionName, OrderId, StartTime, EndTime, Status)
VALUES (@SessionName, @OrderId, @StartTime, NULL, NULL)";
            using (var command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.Parameters.AddWithValue("@SessionName", request.SessionName);
                command.Parameters.AddWithValue("@OrderId", request.OrderId);
                command.Parameters.AddWithValue("@StartTime", request.StartTime);
                command.ExecuteNonQuery();
            }

            return ReadLastInsertId();
        }

        private int InsertDataSet(SQLiteTransaction transaction, int sessionId, Yme009ExpectedImportRequest request)
        {
            const string sql = @"
INSERT INTO Yme009ExpectedDataSet (SessionId, FileName, RecordCount, Sha256, ImportedAt)
VALUES (@SessionId, @FileName, 0, NULL, @ImportedAt)";
            using (var command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.Parameters.AddWithValue("@SessionId", sessionId);
                command.Parameters.AddWithValue("@FileName", Path.GetFileName(request.FilePath));
                command.Parameters.AddWithValue("@ImportedAt", DateTime.Now);
                command.ExecuteNonQuery();
            }

            return ReadLastInsertId();
        }

        private void InsertDevice(SQLiteTransaction transaction, int dataSetId, Yme009ExpectedDeviceRow row)
        {
            const string sql = @"
INSERT INTO Yme009ExpectedDevice (DataSetId, Sn, WifiMac, Imei1, Imei2)
VALUES (@DataSetId, @Sn, @WifiMac, @Imei1, @Imei2)";
            using (var command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.Parameters.AddWithValue("@DataSetId", dataSetId);
                command.Parameters.AddWithValue("@Sn", row.Sn);
                command.Parameters.AddWithValue("@WifiMac", row.WifiMac);
                command.Parameters.AddWithValue("@Imei1", row.Imei1);
                command.Parameters.AddWithValue("@Imei2", row.Imei2);
                command.ExecuteNonQuery();
            }
        }

        private void UpdateDataSet(SQLiteTransaction transaction, int dataSetId, int recordCount, string sha256)
        {
            const string sql = @"
UPDATE Yme009ExpectedDataSet
SET RecordCount = @RecordCount, Sha256 = @Sha256
WHERE Id = @Id";
            using (var command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.Parameters.AddWithValue("@RecordCount", recordCount);
                command.Parameters.AddWithValue("@Sha256", sha256);
                command.Parameters.AddWithValue("@Id", dataSetId);
                command.ExecuteNonQuery();
            }
        }

        private int ReadLastInsertId()
        {
            var rowId = _connection.LastInsertRowId;
            if (rowId <= 0 || rowId > int.MaxValue)
                throw new InvalidOperationException("预期数据主键无效");
            return (int)rowId;
        }

        private void RollbackImport(ref SQLiteTransaction transaction)
        {
            if (transaction == null)
                return;

            try
            {
                transaction.Rollback();
            }
            catch (Exception ex)
            {
                _logger.LogError("YME009 预期数据事务回滚失败: " + ex.Message, ex);
            }

            transaction.Dispose();
            transaction = null;
        }

        private static bool HashesEqual(string actual, string expected)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
                return false;
            return string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
