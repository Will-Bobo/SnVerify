/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// StorageService 迁移场景单元测试：验证旧版 TestRecord 结构在 InitializeAsync 下能平滑迁移到当前版本。
    /// </summary>
    [TestFixture]
    public class StorageServiceMigrationTests
    {
        private string _dbPath;
        private IStorageService _storageService;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Migration_{Guid.NewGuid():N}.db");
        }

        [TearDown]
        public void TearDown()
        {
            _storageService?.Dispose();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        /// <summary>
        /// 构造一个不包含 ChipId / WifiMac / BoardVersion / ChargeBoardVersion / ExpectedVersion / ActualVersion / ExpectedBoardVersion / ExpectedChargeBoardVersion 的旧 TestRecord 结构，
        /// 然后调用 InitializeAsync，验证迁移后列和索引均存在且不抛异常。
        /// </summary>
        [Test]
        public async Task InitializeAsync_ShouldMigrateLegacyTestRecordSchema_AddNewColumnsAndIndexes()
        {
            // Arrange: 使用旧结构创建数据库文件
            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();

                const string createProductTableLegacy = @"
CREATE TABLE IF NOT EXISTS Product (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductName TEXT    NOT NULL UNIQUE,
    Description TEXT,
    CreatedAt   DATETIME
);";

                const string createOrderTableLegacy = @"
CREATE TABLE IF NOT EXISTS ""Order"" (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderName TEXT    NOT NULL,
    ProductId INTEGER NOT NULL,
    CreatedAt DATETIME,
    UNIQUE(OrderName),
    FOREIGN KEY (ProductId) REFERENCES Product(Id)
);";

                const string createTestSessionTableLegacy = @"
CREATE TABLE IF NOT EXISTS TestSession (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionName TEXT    NOT NULL UNIQUE,
    OrderId     INTEGER NOT NULL,
    StartTime   DATETIME NOT NULL,
    EndTime     DATETIME,
    Status      TEXT,
    FOREIGN KEY (OrderId) REFERENCES ""Order""(Id)
);";

                // 旧版 TestRecord：无 WifiMac / ChipId / BoardVersion / ChargeBoardVersion / ExpectedVersion / ActualVersion
                const string createTestRecordTableLegacy = @"
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

                using (var cmd = new SQLiteCommand(createProductTableLegacy, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SQLiteCommand(createOrderTableLegacy, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SQLiteCommand(createTestSessionTableLegacy, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SQLiteCommand(createTestRecordTableLegacy, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            // Act: 使用 StorageService.InitializeAsync 触发迁移逻辑
            _storageService = new StorageService(_dbPath);
            await _storageService.InitializeAsync();

            // Assert: 验证列与索引都已存在
            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();

                // 列检查
                var columns = new List<string>();
                using (var cmd = new SQLiteCommand("PRAGMA table_info(TestRecord);", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(1));
                    }
                }

                Assert.That(columns, Does.Contain("WifiMac"), "迁移后应包含 WifiMac 列");
                Assert.That(columns, Does.Contain("ChipId"), "迁移后应包含 ChipId 列");
                Assert.That(columns, Does.Contain("BoardVersion"), "迁移后应包含 BoardVersion 列");
                Assert.That(columns, Does.Contain("ChargeBoardVersion"), "迁移后应包含 ChargeBoardVersion 列");
                Assert.That(columns, Does.Contain("ExpectedVersion"), "迁移后应包含 ExpectedVersion 列");
                Assert.That(columns, Does.Contain("ActualVersion"), "迁移后应包含 ActualVersion 列");
                Assert.That(columns, Does.Contain("ExpectedBoardVersion"), "迁移后应包含 ExpectedBoardVersion 列（Phase3 目标主板版本）");
                Assert.That(columns, Does.Contain("ExpectedChargeBoardVersion"), "迁移后应包含 ExpectedChargeBoardVersion 列（Phase3 目标充电板版本）");

                // 索引检查：确保 ChipId 相关索引存在
                var indexNames = new List<string>();
                using (var cmd = new SQLiteCommand("PRAGMA index_list(TestRecord);", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        indexNames.Add(reader.GetString(1));
                    }
                }

                Assert.That(indexNames, Does.Contain("idx_testrecord_chipid_result"),
                    "迁移后应存在 idx_testrecord_chipid_result 索引");
            }
        }
    }
}

