/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 存储服务实现，负责 SQLite 数据持久化和 Excel 导出（Phase2 扩展）
    /// </summary>
    public class StorageService : IStorageService, IDisposable
    {
        private readonly string _dbPath;
        private readonly IFileLogger _logger;
        private SQLiteConnection _connection;
        private readonly object _lockObject = new object();
        private readonly object _snapshotLock = new object();
        private StorageSnapshot _snapshot;
        private bool _disposed = false;

        /// <summary>
        /// 当前存储服务状态快照
        /// </summary>
        public StorageSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? StorageSnapshot.Idle();
                }
            }
            private set
            {
                lock (_snapshotLock)
                {
                    _snapshot = value;
                }
            }
        }

        /// <summary>
        /// 初始化存储服务
        /// </summary>
        /// <param name="dbPath">SQLite 数据库文件路径</param>
        /// <param name="logger">文件日志记录器（可选）</param>
        public StorageService(string dbPath, IFileLogger logger = null)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            _logger = logger ?? new NullFileLogger();
            _snapshot = StorageSnapshot.Idle();
            
            // 设置 EPPlus 许可证上下文（非商业用途）
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        /// <summary>
        /// 初始化 SQLite 数据库和表结构
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var connectionString = $"Data Source={_dbPath};Version=3;";
                _connection = new SQLiteConnection(connectionString);
                await Task.Run(() => _connection.Open());

                await CreateTablesAsync();
                _logger?.LogInfo($"数据库初始化成功: {_dbPath}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"数据库初始化失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 确保数据库连接已初始化（如果未初始化则自动初始化）
        /// </summary>
        private void EnsureConnectionInitialized()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                // 同步初始化连接（用于紧急情况）
                var connectionString = $"Data Source={_dbPath};Version=3;";
                _connection = new SQLiteConnection(connectionString);
                _connection.Open();
                
                // 同步创建表结构
                CreateTablesAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// 创建数据库表结构（Phase 2.5 Step 6：仅 Product / Order / TestSession / TestRecord）
        /// </summary>
        private async Task CreateTablesAsync()
        {
            // 新增四张业务基础表（必须幂等）
            var createProductTable = @"
CREATE TABLE IF NOT EXISTS Product (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductName TEXT    NOT NULL UNIQUE,
    Description TEXT,
    CreatedAt   DATETIME
);";

            var createOrderTable = @"
CREATE TABLE IF NOT EXISTS ""Order"" (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderName TEXT    NOT NULL UNIQUE,
    ProductId INTEGER NOT NULL,
    CreatedAt DATETIME,
    FOREIGN KEY (ProductId) REFERENCES Product(Id)
);";

            var createTestSessionTable = @"
CREATE TABLE IF NOT EXISTS TestSession (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionName TEXT    NOT NULL UNIQUE,
    OrderId     INTEGER NOT NULL,
    StartTime   DATETIME NOT NULL,
    EndTime     DATETIME,
    Status      TEXT,
    FOREIGN KEY (OrderId) REFERENCES ""Order""(Id)
);";

            var createTestRecordTable = @"
CREATE TABLE IF NOT EXISTS TestRecord (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId  INTEGER NOT NULL,
    StickerSN  TEXT    NOT NULL,
    DeviceSN   TEXT,
    Result     TEXT    NOT NULL,
    FailReason TEXT,
    VerifyTime DATETIME NOT NULL,
    FOREIGN KEY (SessionId) REFERENCES TestSession(Id)
);";

            // 索引（全部 IF NOT EXISTS，保持可重复执行）
            var createOrderNameUnique = @"
CREATE UNIQUE INDEX IF NOT EXISTS idx_order_ordername ON ""Order""(OrderName);";

            var createOrderProductIdx = @"
CREATE INDEX IF NOT EXISTS idx_order_productid ON ""Order""(ProductId);";

            var createSessionNameUnique = @"
CREATE UNIQUE INDEX IF NOT EXISTS idx_testsession_sessionname ON TestSession(SessionName);";

            var createSessionOrderIdx = @"
CREATE INDEX IF NOT EXISTS idx_testsession_orderid ON TestSession(OrderId);";

            var createTestRecordSessionIdx = @"
CREATE INDEX IF NOT EXISTS idx_testrecord_sessionid ON TestRecord(SessionId);";

            // 可选索引：StickerSN / DeviceSN
            var createTestRecordStickerIdx = @"
CREATE INDEX IF NOT EXISTS idx_testrecord_stickersn ON TestRecord(StickerSN);";

            var createTestRecordDeviceIdx = @"
CREATE INDEX IF NOT EXISTS idx_testrecord_devicesn ON TestRecord(DeviceSN);";

            // 建表顺序：先表后索引，保证外键依赖顺序
            await ExecuteNonQueryAsync(createProductTable);
            await ExecuteNonQueryAsync(createOrderTable);
            await ExecuteNonQueryAsync(createTestSessionTable);
            await ExecuteNonQueryAsync(createTestRecordTable);

            await ExecuteNonQueryAsync(createOrderNameUnique);
            await ExecuteNonQueryAsync(createOrderProductIdx);
            await ExecuteNonQueryAsync(createSessionNameUnique);
            await ExecuteNonQueryAsync(createSessionOrderIdx);
            await ExecuteNonQueryAsync(createTestRecordSessionIdx);
            await ExecuteNonQueryAsync(createTestRecordStickerIdx);
            await ExecuteNonQueryAsync(createTestRecordDeviceIdx);
        }

        /// <summary>
        /// 执行非查询 SQL 命令
        /// </summary>
        private async Task ExecuteNonQueryAsync(string sql)
        {
            // 注意：此方法在 CreateTablesAsync 中被调用，而 CreateTablesAsync 在 EnsureConnectionInitialized 中被调用
            // 因此这里不需要再次调用 EnsureConnectionInitialized，避免循环调用
            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    {
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    }

                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// 检查指定表的列是否存在
        /// </summary>
        private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            var sql = "PRAGMA table_info(" + tableName + ")";
            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return false;
                    using (var command = new SQLiteCommand(sql, _connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var name = reader.GetString(1);
                            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                    return false;
                }
            });
        }

        // 下面开始是基于 TestRecord 的实现；Batch / SnVerifyResult 相关代码在 Phase 2.5 Step 6 中已移除。

        /// <summary>
        /// 检查 StickerSN 是否存在于历史 PASS 绑定中（跨批次查询，基于 TestRecord）
        /// </summary>
        public async Task<bool> IsStickerSnInPassHistoryAsync(string stickerSN)
        {
            if (string.IsNullOrWhiteSpace(stickerSN))
                throw new ArgumentException("StickerSN 不能为空", nameof(stickerSN));

            EnsureConnectionInitialized();

            try
            {
                const string sql = @"
                    SELECT COUNT(1) FROM TestRecord 
                    WHERE Result = 'PASS' AND StickerSN = @StickerSN";

                var exists = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@StickerSN", stickerSN);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查 StickerSN 历史 PASS 记录失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查 DeviceSN 是否存在于历史 PASS 绑定中（跨批次查询，基于 TestRecord）
        /// </summary>
        public async Task<bool> IsDeviceSnInPassHistoryAsync(string deviceSN)
        {
            if (string.IsNullOrWhiteSpace(deviceSN))
                throw new ArgumentException("DeviceSN 不能为空", nameof(deviceSN));

            EnsureConnectionInitialized();

            try
            {
                const string sql = @"
                    SELECT COUNT(1) FROM TestRecord 
                    WHERE Result = 'PASS' AND DeviceSN = @DeviceSN";

                var exists = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@DeviceSN", deviceSN);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查 DeviceSN 历史 PASS 记录失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查绑定关系（StickerSN <-> DeviceSN）是否存在于历史 PASS 绑定中（跨批次查询，基于 TestRecord）
        /// </summary>
        public async Task<bool> IsBindingInPassHistoryAsync(string stickerSN, string deviceSN)
        {
            if (string.IsNullOrWhiteSpace(stickerSN))
                throw new ArgumentException("StickerSN 不能为空", nameof(stickerSN));
            if (string.IsNullOrWhiteSpace(deviceSN))
                throw new ArgumentException("DeviceSN 不能为空", nameof(deviceSN));

            EnsureConnectionInitialized();

            try
            {
                const string sql = @"
                    SELECT COUNT(1) FROM TestRecord 
                    WHERE Result = 'PASS' 
                    AND StickerSN = @StickerSN 
                    AND DeviceSN = @DeviceSN";

                var exists = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@StickerSN", stickerSN);
                            command.Parameters.AddWithValue("@DeviceSN", deviceSN);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查绑定关系历史 PASS 记录失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 更新现有的校验结果记录
        /// </summary>
        public async Task UpdateVerifyResultAsync(SnVerifyResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (result.Id <= 0)
                throw new ArgumentException("记录 ID 必须大于 0", nameof(result));
            if (string.IsNullOrWhiteSpace(result.BatchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.SN))
                throw new ArgumentException("SN 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.Result))
                throw new ArgumentException("校验结果不能为空", nameof(result));

            EnsureConnectionInitialized();

            try
            {
                Snapshot = StorageSnapshot.Processing(result.BatchId);

                var sql = @"
                    UPDATE SnVerifyResult 
                    SET Result = @Result, FailReason = @FailReason, DeviceSN = @DeviceSN, VerifyTime = @VerifyTime
                    WHERE Id = @Id";

                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@Id", result.Id);
                            command.Parameters.AddWithValue("@Result", result.Result);
                            command.Parameters.AddWithValue("@FailReason", (object)result.FailReason ?? DBNull.Value);
                            command.Parameters.AddWithValue("@DeviceSN", (object)result.DeviceSN ?? DBNull.Value);
                            command.Parameters.AddWithValue("@VerifyTime", result.VerifyTime);
                            command.ExecuteNonQuery();
                        }
                    }
                });

                var recordCount = await GetRecordCountAsync(result.BatchId);
                Snapshot = StorageSnapshot.Saved(result.SN, result.BatchId, recordCount);

                _logger?.LogInfo($"校验结果更新成功: Id={result.Id}, BatchId={result.BatchId}, SN={result.SN}, Result={result.Result}");
            }
            catch (Exception ex)
            {
                Snapshot = StorageSnapshot.Error($"更新失败: {ex.Message}", result.BatchId);
                _logger?.LogError($"更新校验结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存 SN 校验结果（Phase2：更新 Snapshot）
        /// </summary>
        public async Task SaveVerifyResultAsync(SnVerifyResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(result.BatchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.SN))
                throw new ArgumentException("SN 不能为空", nameof(result));
            if (string.IsNullOrWhiteSpace(result.Result))
                throw new ArgumentException("校验结果不能为空", nameof(result));

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                // 检查 SN 是否重复
                var isDuplicate = await IsSnDuplicateAsync(result.BatchId, result.SN);
                if (isDuplicate)
                {
                    Snapshot = StorageSnapshot.DuplicateSn(result.SN, result.BatchId);
                    _logger?.LogWarning($"SN 重复，保存失败: BatchId={result.BatchId}, SN={result.SN}");
                    throw new InvalidOperationException($"SN {result.SN} 在批次 {result.BatchId} 中已存在");
                }

                Snapshot = StorageSnapshot.Processing(result.BatchId);

                var insertSql = @"
                    INSERT INTO SnVerifyResult (BatchId, SN, DeviceSN, Result, FailReason, VerifyTime)
                    VALUES (@BatchId, @SN, @DeviceSN, @Result, @FailReason, @VerifyTime)";

                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        using (var command = new SQLiteCommand(insertSql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", result.BatchId);
                            command.Parameters.AddWithValue("@SN", result.SN);
                            command.Parameters.AddWithValue("@DeviceSN", (object)result.DeviceSN ?? DBNull.Value);
                            command.Parameters.AddWithValue("@Result", result.Result);
                            command.Parameters.AddWithValue("@FailReason", (object)result.FailReason ?? DBNull.Value);
                            command.Parameters.AddWithValue("@VerifyTime", result.VerifyTime);
                            command.ExecuteNonQuery();
                            
                            // 获取最后插入的 Id
                            using (var getIdCommand = new SQLiteCommand("SELECT last_insert_rowid()", _connection))
                            {
                                var insertedId = getIdCommand.ExecuteScalar();
                                result.Id = Convert.ToInt32(insertedId);
                            }
                        }
                    }
                });

                // 获取当前批次记录数
                var recordCount = await GetRecordCountAsync(result.BatchId);
                Snapshot = StorageSnapshot.Saved(result.SN, result.BatchId, recordCount);

                _logger?.LogInfo($"校验结果保存成功: BatchId={result.BatchId}, SN={result.SN}, Result={result.Result}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Snapshot = StorageSnapshot.Error($"保存失败: {ex.Message}", result.BatchId);
                _logger?.LogError($"保存校验结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查指定批次内 SN 是否重复（仅在同一批次中检查，不跨批次）。
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <param name="sn">待检查的 SN</param>
        /// <returns>如果在该批次中已存在相同 SN，则返回 true；否则返回 false。</returns>
        public async Task<bool> IsSnDuplicateAsync(string batchId, string sn)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            EnsureConnectionInitialized();

            const string sql = @"
                SELECT COUNT(1)
                FROM SnVerifyResult
                WHERE BatchId = @BatchId AND SN = @SN";

            var count = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    {
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    }

                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.Parameters.AddWithValue("@BatchId", batchId);
                        command.Parameters.AddWithValue("@SN", sn);
                        var obj = command.ExecuteScalar();
                        return Convert.ToInt32(obj);
                    }
                }
            });

            return count > 0;
        }

        /// <summary>
        /// 获取指定批次的记录数
        /// </summary>
        private async Task<int> GetRecordCountAsync(string batchId)
        {
            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            var sql = "SELECT COUNT(1) FROM SnVerifyResult WHERE BatchId = @BatchId";
            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    {
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    }

                    using (var command = new SQLiteCommand(sql, _connection))
                    {
                        command.Parameters.AddWithValue("@BatchId", batchId);
                        var count = command.ExecuteScalar();
                        return Convert.ToInt32(count);
                    }
                }
            });
        }

        /// <summary>
        /// 获取指定批次的所有校验结果
        /// </summary>
        public async Task<IReadOnlyList<SnVerifyResult>> GetResultsByBatchAsync(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));

            // 确保数据库连接已初始化
            EnsureConnectionInitialized();

            try
            {
                var sql = @"
                    SELECT Id, BatchId, SN, DeviceSN, Result, FailReason, VerifyTime
                    FROM SnVerifyResult
                    WHERE BatchId = @BatchId
                    ORDER BY VerifyTime ASC";

                var results = await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        {
                            throw new InvalidOperationException("数据库连接未初始化或已关闭");
                        }

                        var list = new List<SnVerifyResult>();
                        using (var command = new SQLiteCommand(sql, _connection))
                        {
                            command.Parameters.AddWithValue("@BatchId", batchId);
                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    list.Add(new SnVerifyResult
                                    {
                                        Id = reader.GetInt32(0),
                                        BatchId = reader.GetString(1),
                                        SN = reader.GetString(2),
                                        DeviceSN = reader.IsDBNull(3) ? null : reader.GetString(3),
                                        Result = reader.GetString(4),
                                        FailReason = reader.IsDBNull(5) ? null : reader.GetString(5),
                                        VerifyTime = reader.GetDateTime(6)
                                    });
                                }
                            }
                        }
                        return list;
                    }
                });

                return results.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"查询批次结果失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 导出批次结果到 Excel 文件（Phase2：支持 PASS/FAIL 分表，更新 Snapshot）
        /// </summary>
        public async Task ExportBatchResultAsync(string batchId, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("批次 ID 不能为空", nameof(batchId));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            try
            {
                Snapshot = StorageSnapshot.Processing(batchId);

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var results = await GetResultsByBatchAsync(batchId);
                var fileName = $"{batchId}.xlsx";
                var filePath = Path.Combine(outputDirectory, fileName);

                await Task.Run(() =>
                {
                    using (var package = new ExcelPackage())
                    {
                        // 创建 PASS Sheet
                        var passSheet = package.Workbook.Worksheets.Add("PASS");
                        WriteSheetHeader(passSheet);
                        var passResults = results.Where(r => r.Result == "PASS").ToList();
                        WriteSheetData(passSheet, passResults, startRow: 2);

                        // 创建 FAIL Sheet（包含 FAIL 和 TIMEOUT）
                        var failSheet = package.Workbook.Worksheets.Add("FAIL");
                        WriteSheetHeader(failSheet);
                        var failResults = results.Where(r => r.Result == "FAIL" || r.Result == "TIMEOUT").ToList();
                        WriteSheetData(failSheet, failResults, startRow: 2);

                        // 保存文件
                        var fileInfo = new FileInfo(filePath);
                        package.SaveAs(fileInfo);
                    }
                });

                Snapshot = StorageSnapshot.Saved(null, batchId, results.Count);
                _logger?.LogInfo($"批次结果导出成功: BatchId={batchId}, FilePath={filePath}");
            }
            catch (Exception ex)
            {
                Snapshot = StorageSnapshot.Error($"导出失败: {ex.Message}", batchId);
                _logger?.LogError($"导出批次结果失败: {ex.Message}", ex);
                throw;
            }
        }

        // ---------- Phase 2.5 Step 6：Product / Order / TestSession / TestRecord ----------

        /// <summary>
        /// 创建产品记录，返回自增 Id。
        /// </summary>
        public async Task<int> CreateProductAsync(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));
            if (string.IsNullOrWhiteSpace(product.ProductName))
                throw new ArgumentException("ProductName 不能为空", nameof(product));

            EnsureConnectionInitialized();

            var sql = @"
                INSERT INTO Product (ProductName, Description, CreatedAt)
                VALUES (@ProductName, @Description, @CreatedAt)";

            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@ProductName", product.ProductName);
                        cmd.Parameters.AddWithValue("@Description", (object)product.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CreatedAt", (object)product.CreatedAt ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                        using (var getId = new SQLiteCommand("SELECT last_insert_rowid()", _connection))
                        {
                            var id = Convert.ToInt32(getId.ExecuteScalar());
                            product.Id = id;
                            return id;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 获取所有产品列表。
        /// </summary>
        public async Task<IReadOnlyList<Product>> GetAllProductsAsync()
        {
            EnsureConnectionInitialized();
            var sql = @"SELECT Id, ProductName, Description, CreatedAt FROM Product ORDER BY CreatedAt DESC";
            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    var results = new List<Product>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            results.Add(new Product
                            {
                                Id = r.GetInt32(0),
                                ProductName = r.GetString(1),
                                Description = r.IsDBNull(2) ? null : r.GetString(2),
                                CreatedAt = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3)
                            });
                        }
                    }
                    return results;
                }
            });
            return list.AsReadOnly();
        }

        /// <summary>
        /// 创建订单记录，返回自增 Id。
        /// </summary>
        public async Task<int> CreateOrderAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));
            if (string.IsNullOrWhiteSpace(order.OrderName))
                throw new ArgumentException("OrderName 不能为空", nameof(order));

            EnsureConnectionInitialized();

            var sql = @"
                INSERT INTO ""Order"" (OrderName, ProductId, CreatedAt)
                VALUES (@OrderName, @ProductId, @CreatedAt)";

            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderName", order.OrderName);
                        cmd.Parameters.AddWithValue("@ProductId", order.ProductId);
                        cmd.Parameters.AddWithValue("@CreatedAt", (object)order.CreatedAt ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                        using (var getId = new SQLiteCommand("SELECT last_insert_rowid()", _connection))
                        {
                            var id = Convert.ToInt32(getId.ExecuteScalar());
                            order.Id = id;
                            return id;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 判断给定订单名称是否已存在（全局唯一）。
        /// </summary>
        public async Task<bool> OrderNameExistsAsync(string orderName)
        {
            if (string.IsNullOrWhiteSpace(orderName))
                throw new ArgumentException("OrderName 不能为空", nameof(orderName));

            EnsureConnectionInitialized();
            var sql = @"SELECT 1 FROM ""Order"" WHERE OrderName = @OrderName LIMIT 1";
            var exists = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return false;
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderName", orderName);
                        var o = cmd.ExecuteScalar();
                        return o != null;
                    }
                }
            });
            return exists;
        }

        /// <summary>
        /// 获取所有订单列表。
        /// </summary>
        public async Task<IReadOnlyList<Order>> GetAllOrdersAsync()
        {
            EnsureConnectionInitialized();
            var sql = @"SELECT Id, OrderName, ProductId, CreatedAt FROM ""Order"" ORDER BY CreatedAt DESC";
            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    var results = new List<Order>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            results.Add(new Order
                            {
                                Id = r.GetInt32(0),
                                OrderName = r.GetString(1),
                                ProductId = r.GetInt32(2),
                                CreatedAt = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3)
                            });
                        }
                    }
                    return results;
                }
            });
            return list.AsReadOnly();
        }

        /// <summary>
        /// 获取所有 ProjectId 列表（去重后，按字典序排序）。
        /// </summary>
        public async Task<IReadOnlyList<string>> GetAllProjectIdsAsync()
        {
            EnsureConnectionInitialized();

            // 注意：这里假定 Order 表中已存在 ProjectId 列（Phase 2.5 设计）。
            const string sql = @"SELECT DISTINCT ProjectId FROM ""Order"" WHERE ProjectId IS NOT NULL AND ProjectId <> '' ORDER BY ProjectId";

            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");

                    var results = new List<string>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (!r.IsDBNull(0))
                                results.Add(r.GetString(0));
                        }
                    }
                    return results;
                }
            });

            return list.AsReadOnly();
        }

        /// <summary>
        /// 创建测试会话记录，返回自增 Id。
        /// </summary>
        public async Task<int> CreateSessionAsync(TestSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(session.SessionName))
                throw new ArgumentException("SessionName 不能为空", nameof(session));

            EnsureConnectionInitialized();

            var sql = @"
                INSERT INTO TestSession (SessionName, OrderId, StartTime, EndTime, Status)
                VALUES (@SessionName, @OrderId, @StartTime, @EndTime, @Status)";

            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionName", session.SessionName);
                        cmd.Parameters.AddWithValue("@OrderId", session.OrderId);
                        cmd.Parameters.AddWithValue("@StartTime", session.StartTime);
                        cmd.Parameters.AddWithValue("@EndTime", (object)session.EndTime ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Status", (object)session.Status ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                        using (var getId = new SQLiteCommand("SELECT last_insert_rowid()", _connection))
                        {
                            var id = Convert.ToInt32(getId.ExecuteScalar());
                            session.Id = id;
                            return id;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 按订单 Id 获取该订单下所有会话。
        /// </summary>
        public async Task<IReadOnlyList<TestSession>> GetSessionsByOrderIdAsync(int orderId)
        {
            EnsureConnectionInitialized();
            var sql = @"SELECT Id, SessionName, OrderId, StartTime, EndTime, Status FROM TestSession WHERE OrderId = @OrderId ORDER BY StartTime ASC";
            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    var results = new List<TestSession>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderId", orderId);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                results.Add(new TestSession
                                {
                                    Id = r.GetInt32(0),
                                    SessionName = r.GetString(1),
                                    OrderId = r.GetInt32(2),
                                    StartTime = r.GetDateTime(3),
                                    EndTime = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                                    Status = r.IsDBNull(5) ? null : r.GetString(5)
                                });
                            }
                        }
                    }
                    return results;
                }
            });
            return list.AsReadOnly();
        }

        /// <summary>
        /// 判断业务会话名是否已存在。
        /// </summary>
        public async Task<bool> SessionNameExistsAsync(string sessionName)
        {
            if (string.IsNullOrWhiteSpace(sessionName))
                throw new ArgumentException("SessionName 不能为空", nameof(sessionName));

            EnsureConnectionInitialized();
            var sql = @"SELECT 1 FROM TestSession WHERE SessionName = @SessionName LIMIT 1";
            var exists = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return false;
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionName", sessionName);
                        var o = cmd.ExecuteScalar();
                        return o != null;
                    }
                }
            });
            return exists;
        }

        /// <summary>
        /// 保存一条测试记录。
        /// </summary>
        public async Task SaveTestRecordAsync(TestRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (record.SessionId <= 0)
                throw new ArgumentException("SessionId 必须大于 0", nameof(record));
            if (string.IsNullOrWhiteSpace(record.StickerSN))
                throw new ArgumentException("StickerSN 不能为空", nameof(record));
            if (string.IsNullOrWhiteSpace(record.Result))
                throw new ArgumentException("Result 不能为空", nameof(record));

            EnsureConnectionInitialized();

            var sql = @"
                INSERT INTO TestRecord (SessionId, StickerSN, DeviceSN, Result, FailReason, VerifyTime)
                VALUES (@SessionId, @StickerSN, @DeviceSN, @Result, @FailReason, @VerifyTime)";

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", record.SessionId);
                        cmd.Parameters.AddWithValue("@StickerSN", record.StickerSN);
                        cmd.Parameters.AddWithValue("@DeviceSN", (object)record.DeviceSN ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Result", record.Result);
                        cmd.Parameters.AddWithValue("@FailReason", (object)record.FailReason ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@VerifyTime", record.VerifyTime);
                        cmd.ExecuteNonQuery();
                        using (var getId = new SQLiteCommand("SELECT last_insert_rowid()", _connection))
                        {
                            record.Id = Convert.ToInt32(getId.ExecuteScalar());
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 按 SessionId 获取所有 TestRecord（内部 INT 主键）。
        /// </summary>
        public async Task<IReadOnlyList<TestRecord>> GetTestRecordsBySessionAsync(int sessionId)
        {
            EnsureConnectionInitialized();

            var sql = @"
                SELECT Id, SessionId, StickerSN, DeviceSN, Result, FailReason, VerifyTime
                FROM TestRecord WHERE SessionId = @SessionId ORDER BY VerifyTime ASC";

            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    var results = new List<TestRecord>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                results.Add(new TestRecord
                                {
                                    Id = r.GetInt32(0),
                                    SessionId = r.GetInt32(1),
                                    StickerSN = r.GetString(2),
                                    DeviceSN = r.IsDBNull(3) ? null : r.GetString(3),
                                    Result = r.GetString(4),
                                    FailReason = r.IsDBNull(5) ? null : r.GetString(5),
                                    VerifyTime = r.GetDateTime(6)
                                });
                            }
                        }
                    }
                    return results;
                }
            });
            return list.AsReadOnly();
        }

        /// <summary>
        /// 根据业务 SessionName 查找内部自增 Session Id（TestSession.Id）；若不存在则返回 null。
        /// </summary>
        public async Task<int?> GetInternalSessionIdBySessionNameAsync(string sessionName)
        {
            if (string.IsNullOrWhiteSpace(sessionName))
                return null;

            EnsureConnectionInitialized();

            const string sql = @"
                SELECT Id
                FROM TestSession
                WHERE SessionName = @SessionName
                LIMIT 1";

            int? internalId = null;

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                    {
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    }

                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionName", sessionName);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                internalId = reader.GetInt32(0);
                            }
                        }
                    }
                }
            });

            return internalId;
        }

        /// <summary>
        /// 按业务 SessionId（字符串，如 OrderId_yyyyMMdd_HHmmss）获取所有 TestRecord。
        /// 实现方式：先根据 SessionName 查到内部自增 Id，再复用 INT 版本查询；若未找到则返回空列表。
        /// </summary>
        public async Task<IReadOnlyList<TestRecord>> GetTestRecordsBySessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new List<TestRecord>().AsReadOnly();
            }

            var internalSessionId = await GetInternalSessionIdBySessionNameAsync(sessionId);
            if (!internalSessionId.HasValue)
            {
                return new List<TestRecord>().AsReadOnly();
            }

            return await GetTestRecordsBySessionAsync(internalSessionId.Value);
        }

        /// <summary>
        /// 按 SessionId + StickerSN 获取最近一条 TestRecord；若不存在则返回 null。
        /// </summary>
        public async Task<TestRecord> GetTestRecordBySessionAndStickerSnAsync(int sessionId, string stickerSN)
        {
            if (string.IsNullOrWhiteSpace(stickerSN))
                throw new ArgumentException("StickerSN 不能为空", nameof(stickerSN));

            EnsureConnectionInitialized();

            var sql = @"
                SELECT Id, SessionId, StickerSN, DeviceSN, Result, FailReason, VerifyTime
                FROM TestRecord WHERE SessionId = @SessionId AND StickerSN = @StickerSN ORDER BY VerifyTime DESC LIMIT 1";

            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        cmd.Parameters.AddWithValue("@StickerSN", stickerSN);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) return null;
                            return new TestRecord
                            {
                                Id = r.GetInt32(0),
                                SessionId = r.GetInt32(1),
                                StickerSN = r.GetString(2),
                                DeviceSN = r.IsDBNull(3) ? null : r.GetString(3),
                                Result = r.GetString(4),
                                FailReason = r.IsDBNull(5) ? null : r.GetString(5),
                                VerifyTime = r.GetDateTime(6)
                            };
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 更新已有 TestRecord（记录必须含 Id）。
        /// </summary>
        public async Task UpdateTestRecordAsync(TestRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (record.Id <= 0)
                throw new ArgumentException("TestRecord.Id 必须大于 0", nameof(record));

            EnsureConnectionInitialized();

            var sql = @"
                UPDATE TestRecord SET DeviceSN = @DeviceSN, Result = @Result, FailReason = @FailReason, VerifyTime = @VerifyTime
                WHERE Id = @Id";

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", record.Id);
                        cmd.Parameters.AddWithValue("@DeviceSN", (object)record.DeviceSN ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Result", record.Result);
                        cmd.Parameters.AddWithValue("@FailReason", (object)record.FailReason ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@VerifyTime", record.VerifyTime);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// 按业务 OrderId（订单名称 OrderName）查该订单下所有 TestSession（Phase 2.5：新 TestSession 模型）
        /// </summary>
        public async Task<IReadOnlyList<TestSession>> GetSessionsByOrderIdAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));
            EnsureConnectionInitialized();

            // 将业务 orderId 视为 OrderName，解析出 Order.Id
            const string resolveSql = @"SELECT Id FROM ""Order"" WHERE OrderName = @OrderName LIMIT 1";
            int? orderPk = null;
            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(resolveSql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderName", orderId);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                                orderPk = r.GetInt32(0);
                        }
                    }
                }
            });
            if (!orderPk.HasValue)
                return new List<TestSession>().AsReadOnly();

            var sql = @"SELECT Id, SessionName, OrderId, StartTime, EndTime, Status FROM TestSession WHERE OrderId = @OrderId ORDER BY StartTime ASC";
            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    var results = new List<TestSession>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderId", orderPk.Value);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                results.Add(new TestSession
                                {
                                    Id = r.GetInt32(0),
                                    SessionName = r.GetString(1),
                                    OrderId = r.GetInt32(2),
                                    StartTime = r.GetDateTime(3),
                                    EndTime = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                                    Status = r.IsDBNull(5) ? null : r.GetString(5)
                                });
                            }
                        }
                    }
                    return results;
                }
            });
            return list.AsReadOnly();
        }

        /// <summary>
        /// 按 ProjectId 查该项目下所有 TestSession（Phase 2.5：Order 无 ProjectId，暂返回空）
        /// </summary>
        public async Task<IReadOnlyList<TestSession>> GetSessionsByProjectIdAsync(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            await Task.CompletedTask;
            return new List<TestSession>().AsReadOnly();
        }

        /// <summary>
        /// 判断业务 SessionId（SessionName）是否已存在（Phase 2.5：新 TestSession 模型）
        /// </summary>
        public async Task<bool> SessionExistsAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;
            EnsureConnectionInitialized();
            var sql = "SELECT 1 FROM TestSession WHERE SessionName = @SessionName LIMIT 1";
            var result = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return false;
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@SessionName", sessionId);
                        var o = cmd.ExecuteScalar();
                        return o != null;
                    }
                }
            });
            return result;
        }

        /// <summary>
        /// 判断给定订单是否已存在（兼容旧接口名，语义等同于按订单名称检查）。
        /// </summary>
        public async Task<bool> OrderExistsAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));

            EnsureConnectionInitialized();
            // Phase 2.5 Step 6：不再有字符串 OrderId 列，这里将 orderId 视为业务上的 OrderName。
            var sql = @"SELECT COUNT(1) FROM ""Order"" WHERE OrderName = @OrderName";
            var count = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderName", orderId);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            });
            return count > 0;
        }

        /// <summary>
        /// 按 Session 导出：单 Session → xlsx 双 Sheet（PASS 原样、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条）+ txt（Phase 2.5）
        /// </summary>
        public async Task ExportBySessionAsync(int sessionId, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            EnsureConnectionInitialized();

            var records = await GetTestRecordsBySessionAsync(sessionId);
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var passRecords = records.Where(r => r.Result == "PASS").ToList();
            var failRecordsRaw = records.Where(r => r.Result == "FAIL" || r.Result == "TIMEOUT").ToList();
            var seen = new HashSet<(string, string)>(new ValueTupleOrdinalComparer());
            var failRecordsDeduped = new List<TestRecord>();
            foreach (var r in failRecordsRaw)
            {
                var key = (r.StickerSN ?? "", r.DeviceSN ?? "");
                if (seen.Add(key))
                    failRecordsDeduped.Add(r);
            }

            var xlsxPath = Path.Combine(outputDirectory, $"{sessionId}.xlsx");
            await Task.Run(() =>
            {
                using (var package = new ExcelPackage())
                {
                    var passSheet = package.Workbook.Worksheets.Add("PASS");
                    WriteTestRecordSheetHeader(passSheet);
                    WriteTestRecordSheetData(passSheet, passRecords, startRow: 2);

                    var failSheet = package.Workbook.Worksheets.Add("FAIL");
                    WriteTestRecordSheetHeader(failSheet);
                    WriteTestRecordSheetData(failSheet, failRecordsDeduped, startRow: 2);

                    package.SaveAs(new FileInfo(xlsxPath));
                }
            });

            var txtPath = Path.Combine(outputDirectory, $"{sessionId}.txt");
            await Task.Run(() =>
            {
                using (var writer = new StreamWriter(txtPath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine($"SessionId: {sessionId}");
                    writer.WriteLine($"PASS: {passRecords.Count}, FAIL(去重后): {failRecordsDeduped.Count}");
                    foreach (var r in passRecords)
                        writer.WriteLine($"PASS\t{r.StickerSN}\t{r.DeviceSN}\t{r.VerifyTime:yyyy年M月d日 HH:mm:ss}");
                    foreach (var r in failRecordsDeduped)
                        writer.WriteLine($"FAIL\t{r.StickerSN}\t{r.DeviceSN}\t{r.Result}\t{r.FailReason}\t{r.VerifyTime:yyyy年M月d日 HH:mm:ss}");
                }
            });

            _logger?.LogInfo($"按 Session 导出成功: SessionId={sessionId}, xlsx={xlsxPath}, txt={txtPath}");
        }

        private static void WriteTestRecordSheetHeader(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Id";
            sheet.Cells[1, 2].Value = "条形码SN";
            sheet.Cells[1, 3].Value = "设备SN";
            sheet.Cells[1, 4].Value = "Result";
            sheet.Cells[1, 5].Value = "FailReason";
            sheet.Cells[1, 6].Value = "VerifyTime";
            using (var range = sheet.Cells[1, 1, 1, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
        }

        private static void WriteTestRecordSheetData(ExcelWorksheet sheet, IList<TestRecord> records, int startRow)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var row = startRow + i;
                var r = records[i];
                sheet.Cells[row, 1].Value = r.Id;
                sheet.Cells[row, 2].Value = r.StickerSN;
                sheet.Cells[row, 3].Value = r.DeviceSN ?? string.Empty;
                sheet.Cells[row, 4].Value = r.Result;
                sheet.Cells[row, 5].Value = r.FailReason ?? string.Empty;
                sheet.Cells[row, 6].Value = r.VerifyTime.ToString("yyyy年M月d日 HH:mm:ss");
            }
            if (records.Count > 0 && sheet.Dimension != null)
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        /// <summary>
        /// 写入 Sheet 表头
        /// </summary>
        private void WriteSheetHeader(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Id";
            sheet.Cells[1, 2].Value = "条形码SN";
            sheet.Cells[1, 3].Value = "设备SN";
            sheet.Cells[1, 4].Value = "Result";
            sheet.Cells[1, 5].Value = "FailReason";
            sheet.Cells[1, 6].Value = "VerifyTime";

            // 设置表头样式
            using (var range = sheet.Cells[1, 1, 1, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
        }

        /// <summary>
        /// 写入 Sheet 数据
        /// </summary>
        private void WriteSheetData(ExcelWorksheet sheet, IList<SnVerifyResult> results, int startRow)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var row = startRow + i;
                var result = results[i];
                sheet.Cells[row, 1].Value = result.Id;
                sheet.Cells[row, 2].Value = result.SN;
                sheet.Cells[row, 3].Value = result.DeviceSN ?? string.Empty;
                sheet.Cells[row, 4].Value = result.Result;
                // 将 VerifyTime 格式化为可读中文日期时间格式，例如 “2026年1月11日 13:21:22”
                sheet.Cells[row, 5].Value = result.FailReason ?? string.Empty;
                sheet.Cells[row, 6].Value = result.VerifyTime.ToString("yyyy年M月d日 HH:mm:ss");
            }

            // 自动调整列宽
            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            System.GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源（内部实现）
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// 用于 (string, string) 元组的按序比较器，保证去重时按字符串序比较。
        /// </summary>
        private sealed class ValueTupleOrdinalComparer : IEqualityComparer<(string, string)>
        {
            private static readonly StringComparer Ordinal = StringComparer.Ordinal;

            public bool Equals((string, string) x, (string, string) y) =>
                Ordinal.Equals(x.Item1 ?? "", y.Item1 ?? "") && Ordinal.Equals(x.Item2 ?? "", y.Item2 ?? "");

            public int GetHashCode((string, string) obj)
            {
                unchecked
                {
                    int h = Ordinal.GetHashCode(obj.Item1 ?? "");
                    h = (h * 31) + Ordinal.GetHashCode(obj.Item2 ?? "");
                    return h;
                }
            }
        }
    }
}
