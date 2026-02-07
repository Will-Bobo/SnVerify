/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 1 TDD：Order/TestSession/TestRecord 表与按 Session 导出单元测试。
/// 契约：PASS 原样、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条；单 Session → xlsx + txt。
/// </remarks>

using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Models;
using SnVerify.Domain.Validation;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Phase25StorageServiceTests
    {
        private IStorageService _storage;
        private string _dbPath;
        private string _outDir;

        [SetUp]
        public void SetUp()
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Phase25_{Guid.NewGuid()}.db");
            _outDir = Path.Combine(Path.GetTempPath(), $"SnVerify_Phase25_Export_{Guid.NewGuid()}");
            Directory.CreateDirectory(_outDir);
            _storage = new StorageService(_dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            _storage?.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_outDir)) Directory.Delete(_outDir, true);
        }

        [Test]
        public async Task InitializeAsync_ShouldCreateProduct_Order_TestSession_TestRecord_Tables()
        {
            await _storage.InitializeAsync();

            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                var tables = conn.GetSchema("Tables");
                var names = tables.Rows.Cast<System.Data.DataRow>()
                    .Select(r => r["TABLE_NAME"].ToString())
                    .ToList();
                Assert.That(names, Does.Contain("Product"));
                Assert.That(names, Does.Contain("Order"));
                Assert.That(names, Does.Contain("TestSession"));
                Assert.That(names, Does.Contain("TestRecord"));
            }
        }

        [Test]
        public async Task CreateOrderAsync_And_CreateSessionAsync_And_SaveTestRecordAsync_Work()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product
            {
                ProductName = "Prod1",
                Description = "Product1",
                CreatedAt = DateTime.Now
            });

            var orderId = await _storage.CreateOrderAsync(new Order
            {
                ProductId = productId,
                OrderName = "Order1",
                CreatedAt = DateTime.Now
            });

            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "Order1_20260126_143000",
                StartTime = DateTime.Now
            });

            var rec = new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "STICK001",
                DeviceSN = "DEV001",
                Result = "PASS",
                VerifyTime = DateTime.Now
            };
            await _storage.SaveTestRecordAsync(rec);

            var list = await _storage.GetTestRecordsBySessionAsync(sessionId);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].StickerSN, Is.EqualTo("STICK001"));
            Assert.That(list[0].Result, Is.EqualTo("PASS"));
        }

        [Test]
        public async Task ExportBySessionAsync_PASS_AsIs_FAIL_DedupedByStickerDevice_KeepsFirst()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "Prod1" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Ord1" });
            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "Ord1_20260126_143000",
                StartTime = DateTime.Now
            });

            var baseTime = new DateTime(2026, 1, 26, 14, 31, 0);
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S1", DeviceSN = "D1", Result = "PASS", VerifyTime = baseTime });
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S2", DeviceSN = "D2", Result = "FAIL", FailReason = "first", VerifyTime = baseTime.AddSeconds(1) });
            await _storage.SaveTestRecordAsync(new TestRecord { SessionId = sessionId, StickerSN = "S2", DeviceSN = "D2", Result = "FAIL", FailReason = "second", VerifyTime = baseTime.AddSeconds(2) });

            await _storage.ExportBySessionAsync(sessionId, _outDir);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            Assert.That(File.Exists(xlsxPath), Is.True);

            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var passSheet = package.Workbook.Worksheets["PASS"];
                Assert.That(passSheet, Is.Not.Null);
                Assert.That(passSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(2)); // header + 1 PASS
                var failSheet = package.Workbook.Worksheets["FAIL"];
                Assert.That(failSheet, Is.Not.Null);
                // FAIL 去重后应只有 1 条 (S2,D2)
                Assert.That(failSheet.Dimension?.Rows ?? 0, Is.GreaterThanOrEqualTo(2));
                var failRow2 = failSheet.Cells[2, 2].Text; // 条形码SN
                var failRow3 = failSheet.Cells[2, 5].Text; // FailReason → 应保留第一条 "first"
                Assert.That(failRow2, Is.EqualTo("S2"));
                Assert.That(failRow3, Is.EqualTo("first"));
            }

            var txtPath = Path.Combine(_outDir, $"{sessionId}.txt");
            Assert.That(File.Exists(txtPath), Is.True);
        }

        [Test]
        public async Task GetTestRecordsBySessionAsync_WhenSessionHasNoRecords_ReturnsEmptyList()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "Prod1" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Ord1" });
            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "Ord1_20260126_143000",
                StartTime = DateTime.Now
            });

            var list = await _storage.GetTestRecordsBySessionAsync(sessionId);

            Assert.That(list, Is.Not.Null);
            Assert.That(list.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetTestRecordsBySessionAsync_WhenSessionIdNotExists_ReturnsEmptyList()
        {
            await _storage.InitializeAsync();

            var list = await _storage.GetTestRecordsBySessionAsync(-1);

            Assert.That(list, Is.Not.Null);
            Assert.That(list.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// 根据 Export_Semantics：Session 无记录时不生成任何文件
        /// </summary>
        [Test]
        public async Task ExportBySessionAsync_WhenSessionHasNoRecords_DoesNotCreateAnyFiles()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "Prod1" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "Ord1" });
            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "Ord1_20260126_143000",
                StartTime = DateTime.Now
            });

            await _storage.ExportBySessionAsync(sessionId, _outDir);

            var xlsxPath = Path.Combine(_outDir, $"{sessionId}.xlsx");
            var txtPath = Path.Combine(_outDir, $"{sessionId}.txt");
            Assert.That(File.Exists(xlsxPath), Is.False, "Session 无记录不应生成 XLSX");
            Assert.That(File.Exists(txtPath), Is.False, "Session 无记录不应生成 TXT");
        }

        [Test]
        public async Task IsStickerSnInPassHistoryAsync_ShouldReturnTrueOnlyForPassRecords()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdH1" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "OrdH1" });
            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "OrdH1_20260126_150000",
                StartTime = DateTime.Now
            });

            // 一个 PASS，一个 FAIL，StickerSN 相同
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "STICK-H-1",
                DeviceSN = "DEV-H-1",
                Result = "PASS",
                VerifyTime = DateTime.Now.AddSeconds(-10)
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "STICK-H-FAIL",
                DeviceSN = "DEV-H-FAIL",
                Result = "FAIL",
                FailReason = "mismatch",
                VerifyTime = DateTime.Now
            });

            var existsPass = await _storage.IsStickerSnInPassHistoryAsync("STICK-H-1");
            var existsFailOnly = await _storage.IsStickerSnInPassHistoryAsync("STICK-H-FAIL");

            Assert.That(existsPass, Is.True, "存在 PASS 记录时应返回 true");
            Assert.That(existsFailOnly, Is.False, "只有 FAIL 记录时不应视为历史 PASS");
        }

        [Test]
        public async Task IsDeviceSnInPassHistoryAsync_ShouldReturnTrueOnlyForPassRecords()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdH2" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "OrdH2" });
            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "OrdH2_20260126_150500",
                StartTime = DateTime.Now
            });

            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S-PASS",
                DeviceSN = "DEV-H-2",
                Result = "PASS",
                VerifyTime = DateTime.Now.AddSeconds(-5)
            });
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S-FAIL",
                DeviceSN = "DEV-H-FAIL",
                Result = "FAIL",
                FailReason = "mismatch",
                VerifyTime = DateTime.Now
            });

            var existsPass = await _storage.IsDeviceSnInPassHistoryAsync("DEV-H-2");
            var existsFailOnly = await _storage.IsDeviceSnInPassHistoryAsync("DEV-H-FAIL");

            Assert.That(existsPass, Is.True);
            Assert.That(existsFailOnly, Is.False);
        }

        [Test]
        public async Task IsBindingInPassHistoryAsync_ShouldMatchStickerAndDeviceSnInPassRecords()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "ProdH3" });
            var orderId = await _storage.CreateOrderAsync(new Order { ProductId = productId, OrderName = "OrdH3" });
            var sessionId = await _storage.CreateSessionAsync(new TestSession
            {
                OrderId = orderId,
                SessionName = "OrdH3_20260126_151000",
                StartTime = DateTime.Now
            });

            // PASS 绑定
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S-BIND",
                DeviceSN = "D-BIND",
                Result = "PASS",
                VerifyTime = DateTime.Now.AddMinutes(-1)
            });

            // 相同绑定但为 FAIL，不应计入历史 PASS 绑定
            await _storage.SaveTestRecordAsync(new TestRecord
            {
                SessionId = sessionId,
                StickerSN = "S-BIND",
                DeviceSN = "D-BIND",
                Result = "FAIL",
                FailReason = "mismatch",
                VerifyTime = DateTime.Now
            });

            // PASS 时 StickerSN=DeviceSN，仅传一个 SN 查询即可
            var existsPassBinding = await _storage.IsBindingInPassHistoryAsync("S-BIND");
            var notExistsBinding = await _storage.IsBindingInPassHistoryAsync("S-NEVER");

            Assert.That(existsPassBinding, Is.True, "存在 PASS 绑定时应返回 true");
            Assert.That(notExistsBinding, Is.False, "该 SN 无 PASS 记录时应返回 false");
        }

        [Test]
        public async Task GetProductIdByProductNameAsync_WhenProductExists_ReturnsId()
        {
            await _storage.InitializeAsync();

            var createdProductId = await _storage.CreateProductAsync(new Product
            {
                ProductName = "TestProduct",
                Description = "Test Description",
                CreatedAt = DateTime.Now
            });

            var foundProductId = await _storage.GetProductIdByProductNameAsync("TestProduct");

            Assert.That(foundProductId, Is.Not.Null);
            Assert.That(foundProductId.Value, Is.EqualTo(createdProductId));
        }

        [Test]
        public async Task GetProductIdByProductNameAsync_WhenProductNotExists_ReturnsNull()
        {
            await _storage.InitializeAsync();

            var foundProductId = await _storage.GetProductIdByProductNameAsync("NonExistentProduct");

            Assert.That(foundProductId, Is.Null);
        }

        [Test]
        public async Task GetProductIdByProductNameAsync_WhenProductNameIsEmpty_ReturnsNull()
        {
            await _storage.InitializeAsync();

            var foundProductId = await _storage.GetProductIdByProductNameAsync("");

            Assert.That(foundProductId, Is.Null);
        }

        [Test]
        public async Task SetOrderProductIdAsync_ShouldUpdateOrderProductId()
        {
            await _storage.InitializeAsync();

            var productId1 = await _storage.CreateProductAsync(new Product { ProductName = "Product1" });
            var productId2 = await _storage.CreateProductAsync(new Product { ProductName = "Product2" });
            var orderId = await _storage.CreateOrderAsync(new Order
            {
                OrderName = "OrderToUpdate",
                ProductId = productId1,
                CreatedAt = DateTime.Now
            });

            // 验证初始 ProductId
            var allOrders = await _storage.GetAllOrdersAsync();
            var order = allOrders.FirstOrDefault(o => o.OrderName == "OrderToUpdate");
            Assert.That(order, Is.Not.Null);
            Assert.That(order.ProductId, Is.EqualTo(productId1));

            // 更新 ProductId
            await _storage.SetOrderProductIdAsync("OrderToUpdate", productId2);

            // 验证更新后的 ProductId
            allOrders = await _storage.GetAllOrdersAsync();
            order = allOrders.FirstOrDefault(o => o.OrderName == "OrderToUpdate");
            Assert.That(order, Is.Not.Null);
            Assert.That(order.ProductId, Is.EqualTo(productId2));
        }

        [Test]
        public async Task SetOrderProductIdAsync_WhenOrderNameIsEmpty_ThrowsArgumentException()
        {
            await _storage.InitializeAsync();

            Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _storage.SetOrderProductIdAsync("", 1);
            });
        }

        [Test]
        public async Task OrderExistsByOrderNameAndProductAsync_ReturnsTrueWhenOrderExistsWithMatchingProduct()
        {
            await _storage.InitializeAsync();

            var productId = await _storage.CreateProductAsync(new Product { ProductName = "P1", CreatedAt = DateTime.Now });
            await _storage.CreateOrderAsync(new Order { OrderName = "O1", ProductId = productId, CreatedAt = DateTime.Now });

            var exists = await _storage.OrderExistsByOrderNameAndProductAsync("O1", productId);
            Assert.That(exists, Is.True);
        }

        [Test]
        public async Task OrderExistsByOrderNameAndProductAsync_ReturnsFalseWhenOrderNameExistsButDifferentProduct()
        {
            await _storage.InitializeAsync();

            var p1 = await _storage.CreateProductAsync(new Product { ProductName = "P1", CreatedAt = DateTime.Now });
            var p2 = await _storage.CreateProductAsync(new Product { ProductName = "P2", CreatedAt = DateTime.Now });
            await _storage.CreateOrderAsync(new Order { OrderName = "O1", ProductId = p1, CreatedAt = DateTime.Now });

            var exists = await _storage.OrderExistsByOrderNameAndProductAsync("O1", p2);
            Assert.That(exists, Is.False);
        }

        [Test]
        public async Task OrderExistsByOrderNameAndProductAsync_ReturnsTrueWhenSameOrderNameInDifferentProducts()
        {
            await _storage.InitializeAsync();

            var p1 = await _storage.CreateProductAsync(new Product { ProductName = "P1", CreatedAt = DateTime.Now });
            var p2 = await _storage.CreateProductAsync(new Product { ProductName = "P2", CreatedAt = DateTime.Now });
            await _storage.CreateOrderAsync(new Order { OrderName = "O1", ProductId = p1, CreatedAt = DateTime.Now });
            await _storage.CreateOrderAsync(new Order { OrderName = "O1", ProductId = p2, CreatedAt = DateTime.Now });

            Assert.That(await _storage.OrderExistsByOrderNameAndProductAsync("O1", p1), Is.True);
            Assert.That(await _storage.OrderExistsByOrderNameAndProductAsync("O1", p2), Is.True);
        }
    }
}
