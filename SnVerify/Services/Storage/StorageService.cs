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
using SnVerify.Domain.Export;
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
            // 新增业务基础表（必须幂等）
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
    OrderName TEXT    NOT NULL,
    ProductId INTEGER NOT NULL,
    CreatedAt DATETIME,
    UNIQUE(OrderName, ProductId),
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
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId           INTEGER NOT NULL,
    StickerSN           TEXT    NOT NULL,
    DeviceSN            TEXT,
    WifiMac             TEXT,
    ChipId              TEXT,
    BoardVersion        TEXT,
    ChargeBoardVersion  TEXT,
    Result              TEXT    NOT NULL,
    FailReason          TEXT,
    VerifyTime          DATETIME NOT NULL,
    ExpectedVersion     TEXT,
    ActualVersion       TEXT,
    FOREIGN KEY (SessionId) REFERENCES TestSession(Id)
);";

            // 索引（全部 IF NOT EXISTS，保持可重复执行）
            var createOrderNameProductUnique = @"
CREATE UNIQUE INDEX IF NOT EXISTS idx_order_ordername_productid ON ""Order""(OrderName, ProductId);";

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
CREATE INDEX IF NOT EXISTS idx_testrecord_stickersn_result ON TestRecord(StickerSN, Result);";

            var createTestRecordDeviceIdx = @"
CREATE INDEX IF NOT EXISTS idx_testrecord_devicesn_result ON TestRecord(DeviceSN, Result);";

            var createTestRecordChipIdx = @"
CREATE INDEX IF NOT EXISTS idx_testrecord_chipid_result ON TestRecord(ChipId, Result);";

            var createVerificationParameterTable = @"
CREATE TABLE IF NOT EXISTS VerificationParameter (
    ProjectId                  TEXT PRIMARY KEY,
    ExpectedAndroidVersion     TEXT,
    ExpectedBoardVersion       TEXT,
    ExpectedChargeBoardVersion TEXT
);";

            // 建表顺序：先表结构（含迁移），再索引，保证外键与列依赖顺序
            await ExecuteNonQueryAsync(createProductTable);
            await ExecuteNonQueryAsync(createOrderTable);
            await ExecuteNonQueryAsync(createTestSessionTable);
            await ExecuteNonQueryAsync(createTestRecordTable);
            await ExecuteNonQueryAsync(createVerificationParameterTable);

            // 先做表结构迁移（例如为旧库添加 ExpectedVersion / ActualVersion / ChipId 等列）
            await MigrateOrderToOrderNameProductIdUniqueAsync();
            await MigrateTestRecordAddColumnsAsync();

            // 再统一创建索引，确保引用的列已存在
            await ExecuteNonQueryAsync(createOrderNameProductUnique);
            await ExecuteNonQueryAsync(createOrderProductIdx);
            await ExecuteNonQueryAsync(createSessionNameUnique);
            await ExecuteNonQueryAsync(createSessionOrderIdx);
            await ExecuteNonQueryAsync(createTestRecordSessionIdx);
            await ExecuteNonQueryAsync(createTestRecordStickerIdx);
            await ExecuteNonQueryAsync(createTestRecordDeviceIdx);
            await ExecuteNonQueryAsync(createTestRecordChipIdx);
        }

        /// <summary>
        /// 迁移：将 Order 表从 OrderName 唯一改为 (OrderName, ProductId) 唯一。
        /// </summary>
        private async Task MigrateOrderToOrderNameProductIdUniqueAsync()
        {
            var needsMigration = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return false;
                    using (var cmd = new SQLiteCommand("PRAGMA index_list(\"Order\")", _connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var name = r.GetString(1);
                            if (name == "idx_order_ordername")
                                return true;
                        }
                    }
                    return false;
                }
            });
            if (!needsMigration)
                return;

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return;
                    using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = OFF", _connection))
                        cmd.ExecuteNonQuery();

                    const string createNew = @"
CREATE TABLE ""Order_new"" (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderName TEXT    NOT NULL,
    ProductId INTEGER NOT NULL,
    CreatedAt DATETIME,
    UNIQUE(OrderName, ProductId),
    FOREIGN KEY (ProductId) REFERENCES Product(Id)
);";
                    using (var c1 = new SQLiteCommand(createNew, _connection))
                        c1.ExecuteNonQuery();

                    using (var c2 = new SQLiteCommand(@"INSERT INTO ""Order_new"" (Id, OrderName, ProductId, CreatedAt) SELECT Id, OrderName, ProductId, CreatedAt FROM ""Order""", _connection))
                        c2.ExecuteNonQuery();

                    using (var c3 = new SQLiteCommand(@"DROP TABLE ""Order""", _connection))
                        c3.ExecuteNonQuery();

                    using (var c4 = new SQLiteCommand(@"ALTER TABLE ""Order_new"" RENAME TO ""Order""", _connection))
                        c4.ExecuteNonQuery();

                    using (var c5 = new SQLiteCommand("PRAGMA foreign_keys = ON", _connection))
                        c5.ExecuteNonQuery();
                }
            });
        }

        /// <summary>
        /// 迁移：为已有 TestRecord 表添加 Phase 3 所需列（若不存在）。
        /// </summary>
        private async Task MigrateTestRecordAddColumnsAsync()
        {
            // 版本列
            foreach (var col in new[] { "ExpectedVersion", "ActualVersion" })
            {
                if (!await ColumnExistsAsync("TestRecord", col))
                {
                    await ExecuteNonQueryAsync($"ALTER TABLE TestRecord ADD COLUMN {col} TEXT");
                }
            }

            // 设备扩展信息列
            foreach (var col in new[] { "WifiMac", "ChipId", "BoardVersion", "ChargeBoardVersion" })
            {
                if (!await ColumnExistsAsync("TestRecord", col))
                {
                    await ExecuteNonQueryAsync($"ALTER TABLE TestRecord ADD COLUMN {col} TEXT");
                }
            }
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
        /// 检查给定 SN 是否在历史 PASS 绑定中（跨批次查询）。PASS 时 StickerSN = DeviceSN，故仅按 SN 查一次即可。
        /// </summary>
        public async Task<bool> IsBindingInPassHistoryAsync(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            EnsureConnectionInitialized();

            try
            {
                const string sql = @"
                    SELECT 1 FROM TestRecord
                    WHERE StickerSN = @SN AND Result = 'PASS'
                    LIMIT 1";

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
                            command.Parameters.AddWithValue("@SN", sn);
                            var hasRow = command.ExecuteScalar();
                            return hasRow != null && hasRow != DBNull.Value;
                        }
                    }
                });

                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查历史 PASS 记录失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查给定贴纸 SN 是否在指定订单内已产生 PASS 记录（Order 维度唯一性检查）。
        /// OrderId 为业务订单名（OrderName）。
        /// </summary>
        public async Task<bool> IsStickerSnPassedInOrderAsync(string orderId, string sn)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            EnsureConnectionInitialized();

            try
            {
                const string sql = @"
                    SELECT COUNT(1)
                    FROM TestRecord r
                    INNER JOIN TestSession s ON r.SessionId = s.Id
                    INNER JOIN ""Order"" o ON s.OrderId = o.Id
                    WHERE o.OrderName = @OrderId
                      AND r.StickerSN = @StickerSN
                      AND r.Result = 'PASS'";

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
                            command.Parameters.AddWithValue("@OrderId", orderId);
                            command.Parameters.AddWithValue("@StickerSN", sn);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查订单内 StickerSN PASS 记录失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 检查给定 ChipId 是否在指定订单内已产生 PASS 记录（Order 维度唯一性检查）。
        /// OrderId 为业务订单名（OrderName）。
        /// </summary>
        public async Task<bool> IsChipIdPassedInOrderAsync(string orderId, string chipId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));
            if (string.IsNullOrWhiteSpace(chipId))
                throw new ArgumentException("ChipId 不能为空", nameof(chipId));

            EnsureConnectionInitialized();

            try
            {
                const string sql = @"
                    SELECT COUNT(1)
                    FROM TestRecord r
                    INNER JOIN TestSession s ON r.SessionId = s.Id
                    INNER JOIN ""Order"" o ON s.OrderId = o.Id
                    WHERE o.OrderName = @OrderId
                      AND r.ChipId = @ChipId
                      AND r.Result = 'PASS'";

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
                            command.Parameters.AddWithValue("@OrderId", orderId);
                            command.Parameters.AddWithValue("@ChipId", chipId);
                            var count = command.ExecuteScalar();
                            return Convert.ToInt32(count) > 0;
                        }
                    }
                });

                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"检查订单内 ChipId PASS 记录失败: {ex.Message}", ex);
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
        /// 按产品名称获取产品 Id，不存在则返回 null。
        /// </summary>
        public async Task<int?> GetProductIdByProductNameAsync(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return null;
            EnsureConnectionInitialized();
            var sql = @"SELECT Id FROM Product WHERE ProductName = @ProductName LIMIT 1";
            var id = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return (int?)null;
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@ProductName", productName.Trim());
                        var o = cmd.ExecuteScalar();
                        if (o == null || o == DBNull.Value) return null;
                        return Convert.ToInt32(o);
                    }
                }
            });
            return id;
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
        /// 按订单名称更新订单的 ProductId（用于修正历史订单的 ProductId 为 0 的情况）。
        /// </summary>
        public async Task SetOrderProductIdAsync(string orderName, int productId)
        {
            if (string.IsNullOrWhiteSpace(orderName))
                throw new ArgumentException("OrderName 不能为空", nameof(orderName));
            EnsureConnectionInitialized();
            var sql = @"UPDATE ""Order"" SET ProductId = @ProductId WHERE OrderName = @OrderName";
            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@ProductId", productId);
                        cmd.Parameters.AddWithValue("@OrderName", orderName);
                        cmd.ExecuteNonQuery();
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
        /// Phase 2.5：Order 无 ProjectId 列，用 Product.ProductName 作为“项目”标识（Order 通过 ProductId 关联 Product）。
        /// </summary>
        public async Task<IReadOnlyList<string>> GetAllProjectIdsAsync()
        {
            EnsureConnectionInitialized();

            const string sql = @"SELECT DISTINCT p.ProductName FROM ""Order"" o INNER JOIN Product p ON o.ProductId = p.Id WHERE p.ProductName IS NOT NULL AND p.ProductName <> '' ORDER BY p.ProductName";

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
                INSERT INTO TestRecord (SessionId, StickerSN, DeviceSN, WifiMac, ChipId, BoardVersion, ChargeBoardVersion, Result, FailReason, VerifyTime, ExpectedVersion, ActualVersion)
                VALUES (@SessionId, @StickerSN, @DeviceSN, @WifiMac, @ChipId, @BoardVersion, @ChargeBoardVersion, @Result, @FailReason, @VerifyTime, @ExpectedVersion, @ActualVersion)";

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
                        cmd.Parameters.AddWithValue("@WifiMac", (object)record.WifiMac ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChipId", (object)record.ChipId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@BoardVersion", (object)record.BoardVersion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChargeBoardVersion", (object)record.ChargeBoardVersion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Result", record.Result);
                        cmd.Parameters.AddWithValue("@FailReason", (object)record.FailReason ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@VerifyTime", record.VerifyTime);
                        cmd.Parameters.AddWithValue("@ExpectedVersion", (object)record.ExpectedVersion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ActualVersion", (object)record.ActualVersion ?? DBNull.Value);
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
                SELECT Id, SessionId, StickerSN, DeviceSN, WifiMac, ChipId, BoardVersion, ChargeBoardVersion, Result, FailReason, VerifyTime, ExpectedVersion, ActualVersion
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
                                    WifiMac = r.IsDBNull(4) ? null : r.GetString(4),
                                    ChipId = r.IsDBNull(5) ? null : r.GetString(5),
                                    BoardVersion = r.IsDBNull(6) ? null : r.GetString(6),
                                    ChargeBoardVersion = r.IsDBNull(7) ? null : r.GetString(7),
                                    Result = r.GetString(8),
                                    FailReason = r.IsDBNull(9) ? null : r.GetString(9),
                                    VerifyTime = r.GetDateTime(10),
                                    ExpectedVersion = r.IsDBNull(11) ? null : r.GetString(11),
                                    ActualVersion = r.IsDBNull(12) ? null : r.GetString(12)
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
        /// 根据业务 SessionName 获取完整 TestSession；若不存在则返回 null。
        /// </summary>
        public async Task<TestSession> GetSessionBySessionNameAsync(string sessionName)
        {
            if (string.IsNullOrWhiteSpace(sessionName))
                return null;

            EnsureConnectionInitialized();

            const string sql = @"
                SELECT Id, SessionName, OrderId, StartTime, EndTime, Status
                FROM TestSession
                WHERE SessionName = @SessionName
                LIMIT 1";

            TestSession session = null;

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
                                session = new TestSession
                                {
                                    Id = reader.GetInt32(0),
                                    SessionName = reader.GetString(1),
                                    OrderId = reader.GetInt32(2),
                                    StartTime = reader.GetDateTime(3),
                                    EndTime = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                                    Status = reader.IsDBNull(5) ? null : reader.GetString(5)
                                };
                            }
                        }
                    }
                }
            });

            return session;
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
                SELECT Id, SessionId, StickerSN, DeviceSN, WifiMac, ChipId, BoardVersion, ChargeBoardVersion, Result, FailReason, VerifyTime, ExpectedVersion, ActualVersion
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
                                WifiMac = r.IsDBNull(4) ? null : r.GetString(4),
                                ChipId = r.IsDBNull(5) ? null : r.GetString(5),
                                BoardVersion = r.IsDBNull(6) ? null : r.GetString(6),
                                ChargeBoardVersion = r.IsDBNull(7) ? null : r.GetString(7),
                                Result = r.GetString(8),
                                FailReason = r.IsDBNull(9) ? null : r.GetString(9),
                                VerifyTime = r.GetDateTime(10),
                                ExpectedVersion = r.IsDBNull(11) ? null : r.GetString(11),
                                ActualVersion = r.IsDBNull(12) ? null : r.GetString(12)
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
                UPDATE TestRecord 
                SET DeviceSN = @DeviceSN,
                    WifiMac = @WifiMac,
                    ChipId = @ChipId,
                    BoardVersion = @BoardVersion,
                    ChargeBoardVersion = @ChargeBoardVersion,
                    Result = @Result,
                    FailReason = @FailReason,
                    VerifyTime = @VerifyTime
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
                        cmd.Parameters.AddWithValue("@WifiMac", (object)record.WifiMac ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChipId", (object)record.ChipId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@BoardVersion", (object)record.BoardVersion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChargeBoardVersion", (object)record.ChargeBoardVersion ?? DBNull.Value);
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
        /// 按 ProjectId 查该项目下所有 TestSession（Phase 2.5：ProjectId 视为 ProductName，经 Order→Product 关联查询）
        /// </summary>
        public async Task<IReadOnlyList<TestSession>> GetSessionsByProjectIdAsync(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            EnsureConnectionInitialized();

            const string sql = @"
                SELECT s.Id, s.SessionName, s.OrderId, s.StartTime, s.EndTime, s.Status
                FROM TestSession s
                INNER JOIN ""Order"" o ON s.OrderId = o.Id
                INNER JOIN Product p ON o.ProductId = p.Id
                WHERE p.ProductName = @ProjectId
                ORDER BY s.StartTime ASC";

            var list = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");

                    var results = new List<TestSession>();
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@ProjectId", projectId);
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

        /// <inheritdoc />
        public async Task<bool> OrderExistsByOrderNameAndProductAsync(string orderName, int productId)
        {
            if (string.IsNullOrWhiteSpace(orderName))
                throw new ArgumentException("OrderName 不能为空", nameof(orderName));

            EnsureConnectionInitialized();
            var sql = @"SELECT COUNT(1) FROM ""Order"" WHERE OrderName = @OrderName AND ProductId = @ProductId";
            var count = await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@OrderName", orderName);
                        cmd.Parameters.AddWithValue("@ProductId", productId);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            });
            return count > 0;
        }

        /// <summary>
        /// 获取指定 ProjectId 下配置的版本校验参数；不存在时返回 null。
        /// </summary>
        public async Task<VerificationParameter> GetVerificationParameterAsync(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            }

            EnsureConnectionInitialized();

            const string sql = @"
                SELECT ProjectId, ExpectedAndroidVersion, ExpectedBoardVersion, ExpectedChargeBoardVersion
                FROM VerificationParameter
                WHERE ProjectId = @ProjectId
                LIMIT 1";

            VerificationParameter parameter = null;

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
                        cmd.Parameters.AddWithValue("@ProjectId", projectId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                parameter = new VerificationParameter
                                {
                                    ProjectId = reader.GetString(0),
                                    ExpectedAndroidVersion = reader.IsDBNull(1) ? null : reader.GetString(1),
                                    ExpectedBoardVersion = reader.IsDBNull(2) ? null : reader.GetString(2),
                                    ExpectedChargeBoardVersion = reader.IsDBNull(3) ? null : reader.GetString(3)
                                };
                            }
                        }
                    }
                }
            });

            return parameter;
        }

        /// <summary>
        /// 保存或更新指定 ProjectId 的版本校验参数。
        /// </summary>
        public async Task SaveVerificationParameterAsync(VerificationParameter parameter)
        {
            if (parameter == null)
                throw new ArgumentNullException(nameof(parameter));
            if (string.IsNullOrWhiteSpace(parameter.ProjectId))
                throw new ArgumentException("ProjectId 不能为空", nameof(parameter));

            EnsureConnectionInitialized();

            const string selectSql = @"SELECT COUNT(1) FROM VerificationParameter WHERE ProjectId = @ProjectId";
            const string insertSql = @"
                INSERT INTO VerificationParameter (ProjectId, ExpectedAndroidVersion, ExpectedBoardVersion, ExpectedChargeBoardVersion)
                VALUES (@ProjectId, @ExpectedAndroidVersion, @ExpectedBoardVersion, @ExpectedChargeBoardVersion)";
            const string updateSql = @"
                UPDATE VerificationParameter
                SET ExpectedAndroidVersion = @ExpectedAndroidVersion,
                    ExpectedBoardVersion = @ExpectedBoardVersion,
                    ExpectedChargeBoardVersion = @ExpectedChargeBoardVersion
                WHERE ProjectId = @ProjectId";

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        throw new InvalidOperationException("数据库连接未初始化或已关闭");

                    int existingCount;
                    using (var cmd = new SQLiteCommand(selectSql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@ProjectId", parameter.ProjectId);
                        existingCount = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    var sql = existingCount > 0 ? updateSql : insertSql;
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@ProjectId", parameter.ProjectId);
                        cmd.Parameters.AddWithValue("@ExpectedAndroidVersion", (object)parameter.ExpectedAndroidVersion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ExpectedBoardVersion", (object)parameter.ExpectedBoardVersion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ExpectedChargeBoardVersion", (object)parameter.ExpectedChargeBoardVersion ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
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
        public Task ExportBySessionAsync(int sessionId, string outputDirectory)
        {
            return ExportBySessionAsync(sessionId, outputDirectory, ExportRecordFilter.All);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 1) 调用 GetTestRecordsBySessionAsync 获取所有 TestRecord
        /// 2) 使用 FilterRecordsByVerificationType 按 ExportRecordFilter 过滤
        /// 3) 过滤后为空 → 不生成任何文件，记录跳过日志后直接返回
        /// 4) 过滤后非空 → 生成 XLSX（PASS/FAIL 双 Sheet）和 TXT，仅此时写入“导出成功”日志
        /// 5) 异常统一捕获并记录，不抛出到调用方
        /// </remarks>
        public async Task ExportBySessionAsync(int sessionId, string outputDirectory, ExportRecordFilter filter)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            filter = filter ?? ExportRecordFilter.All;

            try
            {
                EnsureConnectionInitialized();

                var records = await GetTestRecordsBySessionAsync(sessionId);
                var filtered = FilterRecordsByVerificationType(records, filter).ToList();

                // 过滤后为空不导出：Session 无记录，或过滤后无符合条件记录
                if (filtered.Count == 0)
                {
                    _logger?.LogInfo($"Session={sessionId} 在当前过滤条件下无记录，跳过导出");
                    return;
                }

                var passRecords = filtered.Where(r => r.Result == "PASS").ToList();
                var failRecordsRaw = filtered.Where(r => r.Result == "FAIL" || r.Result == "TIMEOUT").ToList();
                var seen = new HashSet<(string, string)>(new ValueTupleOrdinalComparer());
                var failRecordsDeduped = new List<TestRecord>();
                foreach (var r in failRecordsRaw)
                {
                    var key = (r.StickerSN ?? "", r.DeviceSN ?? "");
                    if (seen.Add(key))
                        failRecordsDeduped.Add(r);
                }

                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

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
            catch (Exception ex)
            {
                _logger?.LogError($"按 Session 导出失败: SessionId={sessionId}, {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 按 ExportRecordFilter 过滤记录。约定：StickerSN=="-" 为 VersionMatch，否则为 SnMatch。
        /// </summary>
        private static IEnumerable<TestRecord> FilterRecordsByVerificationType(IReadOnlyList<TestRecord> records, ExportRecordFilter filter)
        {
            if (records == null) return Enumerable.Empty<TestRecord>();
            return records.Where(r =>
            {
                var isVersionMatch = r.StickerSN == "-";
                if (isVersionMatch)
                    return filter.IncludeVersionMatch;
                return filter.IncludeSnMatch;
            });
        }

        /// <summary>
        /// 写入 TestRecord 表头。列顺序：Id, 条形码SN, 设备SN, Result, FailReason, VerifyTime, 目标版本号, 设备版本号。
        /// VersionMatch 类型（StickerSN=="-"）使用第 7、8 列；SnMatch 类型两列保持空。
        /// </summary>
        private static void WriteTestRecordSheetHeader(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Id";
            sheet.Cells[1, 2].Value = "条形码SN";
            sheet.Cells[1, 3].Value = "设备SN";
            sheet.Cells[1, 4].Value = "Result";
            sheet.Cells[1, 5].Value = "FailReason";
            sheet.Cells[1, 6].Value = "VerifyTime";
            sheet.Cells[1, 7].Value = "目标版本号";
            sheet.Cells[1, 8].Value = "设备版本号";
            using (var range = sheet.Cells[1, 1, 1, 8])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
        }

        /// <summary>
        /// 写入 TestRecord 数据。VersionMatch（StickerSN=="-"）填充 ExpectedVersion、ActualVersion；SnMatch 两列保持空。
        /// </summary>
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
                // VersionMatch（StickerSN=="-"）才填充版本列；SnMatch 保持空
                var isVersionMatch = r.StickerSN == "-";
                sheet.Cells[row, 7].Value = isVersionMatch ? (r.ExpectedVersion ?? string.Empty) : string.Empty;
                sheet.Cells[row, 8].Value = isVersionMatch ? (r.ActualVersion ?? string.Empty) : string.Empty;
            }
            if (records.Count > 0 && sheet.Dimension != null)
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        /// <summary>
        /// 写入 Sheet 表头（与 WriteTestRecordSheetHeader 列一致，含目标版本号、设备版本号）
        /// </summary>
        private void WriteSheetHeader(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Id";
            sheet.Cells[1, 2].Value = "条形码SN";
            sheet.Cells[1, 3].Value = "设备SN";
            sheet.Cells[1, 4].Value = "Result";
            sheet.Cells[1, 5].Value = "FailReason";
            sheet.Cells[1, 6].Value = "VerifyTime";
            sheet.Cells[1, 7].Value = "目标版本号";
            sheet.Cells[1, 8].Value = "设备版本号";

            using (var range = sheet.Cells[1, 1, 1, 8])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
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
