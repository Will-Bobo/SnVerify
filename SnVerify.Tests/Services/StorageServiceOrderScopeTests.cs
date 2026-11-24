/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// StorageService 订单维度 SN / ChipId 唯一性查询单元测试（Phase3 扩展）。
    /// </summary>
    [TestFixture]
    public class StorageServiceOrderScopeTests
    {
        private IStorageService _storage;
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_OrderScope_{Guid.NewGuid()}.db");
            _storage = new StorageService(_dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            _storage?.Dispose();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        [Test]
        public async Task IsStickerSnPassedInOrderAsync_ReturnsTrueOnlyForPassRecordsInSameOrder()
        {
            await _storage.InitializeAsync();

            // 创建两个项目/订单
            var productA = await _storage.CreateProductAsync(new Product { ProductName = "PA", CreatedAt = DateTime.Now });
            var productB = await _storage.CreateProductAsync(new Product { ProductName = "PB", CreatedAt = DateTime.Now });

            var orderIdA = await _storage.CreateOrderAsync(new Order { ProductId = productA, OrderName = "ORDER_A", CreatedAt = DateTime.Now });
            var orderIdB = await _storage.CreateOrderAsync(new Order { ProductId = productB, OrderName = "ORDER_B", CreatedAt = DateTime.Now });

            var sessionA = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdA, SessionName = "ORDER_A_1", StartTime = DateTime.Now });
            var sessionB = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdB, SessionName = "ORDER_B_1", StartTime = DateTime.Now });

            // ORDER_A 中 SN1 为 PASS，SN2 为 FAIL
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA,
                StickerSN = "SN1",
                DeviceSN = "DEV1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA,
                StickerSN = "SN2",
                DeviceSN = "DEV2",
                Result = "FAIL",
                FailReason = "MISMATCH",
                VerifyTime = DateTime.Now
            });

            // ORDER_B 中 SN1 为 PASS（不同订单）
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionB,
                StickerSN = "SN1",
                DeviceSN = "DEV3",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            var inOrderA_SN1 = await _storage.IsStickerSnPassedInOrderAsync("ORDER_A", "SN1");
            var inOrderA_SN2 = await _storage.IsStickerSnPassedInOrderAsync("ORDER_A", "SN2");
            var inOrderB_SN1 = await _storage.IsStickerSnPassedInOrderAsync("ORDER_B", "SN1");

            Assert.That(inOrderA_SN1, Is.True, "ORDER_A 中 SN1 为 PASS，应返回 true");
            Assert.That(inOrderA_SN2, Is.False, "ORDER_A 中 SN2 仅有 FAIL，应返回 false");
            Assert.That(inOrderB_SN1, Is.True, "ORDER_B 中 SN1 为 PASS，应返回 true");
        }

        [Test]
        public async Task IsStickerSnPassedInBatchAsync_UsesProjectNameAndOrderNameAsBatchKey()
        {
            await _storage.InitializeAsync();

            // 创建两个产品/订单：PA-ORDER_A 与 PB-ORDER_A（同订单名，不同 ProjectName）
            var productA = await _storage.CreateProductAsync(new Product { ProductName = "PA", CreatedAt = DateTime.Now });
            var productB = await _storage.CreateProductAsync(new Product { ProductName = "PB", CreatedAt = DateTime.Now });

            var orderIdA1 = await _storage.CreateOrderAsync(new Order { ProductId = productA, OrderName = "ORDER_A", CreatedAt = DateTime.Now });
            var orderIdA2 = await _storage.CreateOrderAsync(new Order { ProductId = productB, OrderName = "ORDER_A", CreatedAt = DateTime.Now });

            var sessionA1 = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdA1, SessionName = "A_ORDER_A_1", StartTime = DateTime.Now });
            var sessionA2 = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdA2, SessionName = "B_ORDER_A_1", StartTime = DateTime.Now });

            // 在 Batch (A, ORDER_A) 中 SN1 为 PASS
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA1,
                StickerSN = "SN1",
                DeviceSN = "DEV1",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            // 在 Batch (B, ORDER_A) 中 SN1 为 PASS（不同 ProductCode，同订单名）
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA2,
                StickerSN = "SN1",
                DeviceSN = "DEV2",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            var inBatchA_SN1 = await _storage.IsStickerSnPassedInBatchAsync("PA", "ORDER_A", "SN1");
            var inBatchB_SN1 = await _storage.IsStickerSnPassedInBatchAsync("PB", "ORDER_A", "SN1");

            Assert.That(inBatchA_SN1, Is.True, "Batch (A, ORDER_A) 中 SN1 为 PASS，应返回 true");
            Assert.That(inBatchB_SN1, Is.True, "Batch (B, ORDER_A) 中 SN1 为 PASS，应返回 true");

            // 在 Batch (A, ORDER_A) 中 SN2 没有 PASS 记录
            var inBatchA_SN2 = await _storage.IsStickerSnPassedInBatchAsync("PA", "ORDER_A", "SN2");
            Assert.That(inBatchA_SN2, Is.False, "Batch (PA, ORDER_A) 中 SN2 无 PASS 记录，应返回 false");
        }

        [Test]
        public async Task Should_MatchOnlyPassInSameProjectAndOrder_When_QueryingHistoricalPass()
        {
            await _storage.InitializeAsync();
            var productA = await _storage.CreateProductAsync(new Product { ProductName = "YME009", ProductCode = "CODE-9", CreatedAt = DateTime.Now });
            var productB = await _storage.CreateProductAsync(new Product { ProductName = "OTHER", CreatedAt = DateTime.Now });
            var orderA = await _storage.CreateOrderAsync(new Order { ProductId = productA, OrderName = "ORD-A", CreatedAt = DateTime.Now });
            var orderB = await _storage.CreateOrderAsync(new Order { ProductId = productA, OrderName = "ORD-B", CreatedAt = DateTime.Now });
            var orderOther = await _storage.CreateOrderAsync(new Order { ProductId = productB, OrderName = "ORD-A", CreatedAt = DateTime.Now });
            var current = await _storage.CreateSessionAsync(new TestSession { OrderId = orderA, SessionName = "ORD-A_1", StartTime = DateTime.Now });
            var previous = await _storage.CreateSessionAsync(new TestSession { OrderId = orderA, SessionName = "ORD-A_0", StartTime = DateTime.Now });
            var otherOrder = await _storage.CreateSessionAsync(new TestSession { OrderId = orderB, SessionName = "ORD-B_1", StartTime = DateTime.Now });
            var otherProject = await _storage.CreateSessionAsync(new TestSession { OrderId = orderOther, SessionName = "OTHER_1", StartTime = DateTime.Now });

            await _storage.SaveTestRecordAsync(Record(previous, "SN-FAIL", "FAIL"));
            await _storage.SaveTestRecordAsync(Record(previous, "SN-TIME", "TIMEOUT"));
            await _storage.SaveTestRecordAsync(Record(previous, "SN-PASS", "PASS"));
            await _storage.SaveTestRecordAsync(Record(current, "SN-CURRENT", "PASS"));
            await _storage.SaveTestRecordAsync(Record(otherOrder, "SN-ORDER", "PASS"));
            await _storage.SaveTestRecordAsync(Record(otherProject, "SN-PROJECT", "PASS"));

            var scope = await _storage.GetYme009BatchScopeBySessionIdAsync(current);

            Assert.That(scope.ProductName, Is.EqualTo("YME009"));
            Assert.That(scope.OrderName, Is.EqualTo("ORD-A"));
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-NONE"), Is.False);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-FAIL"), Is.False);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-TIME"), Is.False);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-PASS"), Is.True);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-CURRENT"), Is.True);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-ORDER"), Is.False);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync(scope.ProductName, scope.OrderName, "SN-PROJECT"), Is.False);
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync("CODE-9", scope.OrderName, "SN-PASS"), Is.False);
        }

        [Test]
        public async Task Should_KeepHistoricalPass_When_NewFailRecordIsInserted()
        {
            await _storage.InitializeAsync();
            var product = await _storage.CreateProductAsync(new Product { ProductName = "YME009", CreatedAt = DateTime.Now });
            var order = await _storage.CreateOrderAsync(new Order { ProductId = product, OrderName = "ORD-A", CreatedAt = DateTime.Now });
            var session = await _storage.CreateSessionAsync(new TestSession { OrderId = order, SessionName = "ORD-A_1", StartTime = DateTime.Now });
            var pass = Record(session, "SN-1", "PASS");
            pass.DeviceSN = "DEVICE";
            pass.VerifyTime = new DateTime(2026, 11, 24, 8, 0, 0);
            await _storage.SaveTestRecordAsync(pass);
            var fail = Record(session, "SN-1", "FAIL");
            fail.FailReason = Yme009FailReasonCodes.SnDuplicate;
            fail.VerifyTime = new DateTime(2026, 11, 24, 8, 1, 0);
            await _storage.SaveTestRecordAsync(fail);

            var records = await _storage.GetTestRecordsBySessionAsync(session);

            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(records[0].Result, Is.EqualTo("PASS"));
            Assert.That(records[1].Result, Is.EqualTo("FAIL"));
            Assert.That(records[1].FailReason, Is.EqualTo("SN_DUPLICATE"));
            Assert.That(await _storage.IsStickerSnPassedInBatchAsync("YME009", "ORD-A", "SN-1"), Is.True);
        }

        private static TestRecord Record(int sessionId, string stickerSn, string result)
        {
            return new TestRecord
            {
                SessionId = sessionId,
                StickerSN = stickerSn,
                DeviceSN = "DEV-" + stickerSn,
                Result = result,
                VerifyTime = DateTime.Now
            };
        }

        [Test]
        public async Task IsChipIdPassedInOrderAsync_ReturnsTrueOnlyForPassRecordsInSameOrder()
        {
            await _storage.InitializeAsync();

            var product = await _storage.CreateProductAsync(new Product { ProductName = "PX", CreatedAt = DateTime.Now });
            var orderIdA = await _storage.CreateOrderAsync(new Order { ProductId = product, OrderName = "ORDER_CA", CreatedAt = DateTime.Now });
            var orderIdB = await _storage.CreateOrderAsync(new Order { ProductId = product, OrderName = "ORDER_CB", CreatedAt = DateTime.Now });

            var sessionA = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdA, SessionName = "ORDER_CA_1", StartTime = DateTime.Now });
            var sessionB = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdB, SessionName = "ORDER_CB_1", StartTime = DateTime.Now });

            // ORDER_CA 中 Chip F501 为 PASS，F502 为 FAIL
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA,
                StickerSN = "SN_CA1",
                DeviceSN = "DEV_CA1",
                ChipId = "F501",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA,
                StickerSN = "SN_CA2",
                DeviceSN = "DEV_CA2",
                ChipId = "F502",
                Result = "FAIL",
                FailReason = "CHIP_ERROR",
                VerifyTime = DateTime.Now
            });

            // ORDER_CB 中 Chip F501 再次 PASS（不同订单）
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionB,
                StickerSN = "SN_CB1",
                DeviceSN = "DEV_CB1",
                ChipId = "F501",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            var inOrderCA_F501 = await _storage.IsChipIdPassedInOrderAsync("ORDER_CA", "F501");
            var inOrderCA_F502 = await _storage.IsChipIdPassedInOrderAsync("ORDER_CA", "F502");
            var inOrderCB_F501 = await _storage.IsChipIdPassedInOrderAsync("ORDER_CB", "F501");

            Assert.That(inOrderCA_F501, Is.True, "ORDER_CA 中 F501 为 PASS，应返回 true");
            Assert.That(inOrderCA_F502, Is.False, "ORDER_CA 中 F502 仅有 FAIL，应返回 false");
            Assert.That(inOrderCB_F501, Is.True, "ORDER_CB 中 F501 为 PASS，应返回 true");
        }

        [Test]
        public async Task IsChipIdPassedInBatchAsync_UsesProjectNameAndOrderNameAsBatchKey()
        {
            await _storage.InitializeAsync();

            // ProductName PA / PB 分别代表两个项目
            var productA = await _storage.CreateProductAsync(new Product { ProductName = "PA", CreatedAt = DateTime.Now });
            var productB = await _storage.CreateProductAsync(new Product { ProductName = "PB", CreatedAt = DateTime.Now });

            var orderIdA = await _storage.CreateOrderAsync(new Order { ProductId = productA, OrderName = "ORDER_X", CreatedAt = DateTime.Now });
            var orderIdB = await _storage.CreateOrderAsync(new Order { ProductId = productB, OrderName = "ORDER_X", CreatedAt = DateTime.Now });

            var sessionA = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdA, SessionName = "PA_ORDER_X_1", StartTime = DateTime.Now });
            var sessionB = await _storage.CreateSessionAsync(new TestSession { OrderId = orderIdB, SessionName = "PB_ORDER_X_1", StartTime = DateTime.Now });

            // Batch (PA, ORDER_X) 中 Chip F501 为 PASS
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionA,
                StickerSN = "SN_A1",
                DeviceSN = "DEV_A1",
                ChipId = "F501",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            // Batch (PB, ORDER_X) 中 Chip F501 再次 PASS（不同 ProjectName，同一订单名）
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionB,
                StickerSN = "SN_B1",
                DeviceSN = "DEV_B1",
                ChipId = "F501",
                Result = "PASS",
                VerifyTime = DateTime.Now
            });

            var inBatchA_F501 = await _storage.IsChipIdPassedInBatchAsync("PA", "ORDER_X", "F501");
            var inBatchB_F501 = await _storage.IsChipIdPassedInBatchAsync("PB", "ORDER_X", "F501");
            var inBatchA_F502 = await _storage.IsChipIdPassedInBatchAsync("PA", "ORDER_X", "F502");

            Assert.That(inBatchA_F501, Is.True, "Batch (PA, ORDER_X) 中 F501 为 PASS，应返回 true");
            Assert.That(inBatchB_F501, Is.True, "Batch (PB, ORDER_X) 中 F501 为 PASS，应返回 true");
            Assert.That(inBatchA_F502, Is.False, "Batch (PA, ORDER_X) 中 F502 无 PASS 记录，应返回 false");
        }

        /// <summary>
        /// GetProductNameBySessionNameAsync：Session → Order → Product → ProductName。
        /// </summary>
        [Test]
        public async Task GetProductNameBySessionNameAsync_ReturnsProductName_WhenSessionExists()
        {
            await _storage.InitializeAsync();

            int productId = await _storage.CreateProductAsync(new Product { ProductName = "MyProject", CreatedAt = DateTime.Now });
            int orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "O1", CreatedAt = DateTime.Now });
            await _storage.CreateSessionAsync(new TestSession { OrderId = orderId, SessionName = "O1_20250101_120000", StartTime = DateTime.Now });

            var productName = await _storage.GetProductNameBySessionNameAsync("O1_20250101_120000");

            Assert.That(productName, Is.EqualTo("MyProject"));
        }

        [Test]
        public async Task GetProductNameBySessionNameAsync_ReturnsNull_WhenSessionNotFound()
        {
            await _storage.InitializeAsync();

            var productName = await _storage.GetProductNameBySessionNameAsync("NonExistent_Session");

            Assert.That(productName, Is.Null);
        }
    }
}

