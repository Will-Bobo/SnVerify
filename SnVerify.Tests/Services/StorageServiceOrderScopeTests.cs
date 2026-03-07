/// <author>
/// AI Assistant
/// </author>

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
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

