/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5：SessionLifecycleService 单元测试，重点验证 CreateAndStartSession 中 Product 创建/查找和 Order.ProductId 设置逻辑。
/// </remarks>

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Logging;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class SessionLifecycleServiceTests
    {
        private IStorageService _storage;
        private SessionLifecycleService _sessionService;
        private string _dbPath;

        [SetUp]
        public async Task SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_SessionLifecycle_{Guid.NewGuid()}.db");
            _storage = new StorageService(_dbPath);
            await _storage.InitializeAsync();
            _sessionService = new SessionLifecycleService(_storage);
        }

        [TearDown]
        public void TearDown()
        {
            _sessionService = null;
            _storage?.Dispose();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        [Test]
        public async Task CreateAndStartSession_WhenProjectIdProvided_ShouldCreateProductIfNotExists()
        {
            var orderId = "ORDER001";
            var projectId = "PROJECT001";

            var sessionId = _sessionService.CreateAndStartSession(orderId, orderId, projectId);

            Assert.That(sessionId, Is.Not.Null);
            Assert.That(_sessionService.Snapshot.IsActive, Is.True);

            // 验证 Product 已创建
            var productId = await _storage.GetProductIdByProductNameAsync(projectId);
            Assert.That(productId, Is.Not.Null, "Product 应该已创建");

            // 验证 Order 已关联到 Product
            var orders = await _storage.GetAllOrdersAsync();
            var order = orders.FirstOrDefault(o => o.OrderName == orderId);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.ProductId, Is.EqualTo(productId.Value), "Order 应该关联到创建的 Product");
        }

        [Test]
        public async Task CreateAndStartSession_WhenProjectIdExists_ShouldReuseExistingProduct()
        {
            var orderId1 = "ORDER001";
            var orderId2 = "ORDER002";
            var projectId = "PROJECT002";

            // 第一次创建 Session，应该创建 Product
            var sessionId1 = _sessionService.CreateAndStartSession(orderId1, orderId1, projectId);
            var productId1 = await _storage.GetProductIdByProductNameAsync(projectId);
            Assert.That(productId1, Is.Not.Null);

            // 结束第一个 Session
            _sessionService.EndSession();

            // 第二次创建 Session，应该复用同一个 Product
            var sessionId2 = _sessionService.CreateAndStartSession(orderId2, orderId2, projectId);
            var productId2 = await _storage.GetProductIdByProductNameAsync(projectId);

            Assert.That(productId2, Is.EqualTo(productId1), "应该复用已存在的 Product");
            
            // 验证两个 Order 都关联到同一个 Product
            var orders = await _storage.GetAllOrdersAsync();
            var order1 = orders.FirstOrDefault(o => o.OrderName == orderId1);
            var order2 = orders.FirstOrDefault(o => o.OrderName == orderId2);
            Assert.That(order1.ProductId, Is.EqualTo(productId1.Value));
            Assert.That(order2.ProductId, Is.EqualTo(productId1.Value));
        }

        [Test]
        public async Task CreateAndStartSession_WhenProjectIdIsEmpty_ShouldCreateOrderWithProductIdZero()
        {
            var orderId = "ORDER003";

            var sessionId = _sessionService.CreateAndStartSession(orderId, orderId, null);

            Assert.That(sessionId, Is.Not.Null);

            // 验证 Order 的 ProductId 为 0
            var orders = await _storage.GetAllOrdersAsync();
            var order = orders.FirstOrDefault(o => o.OrderName == orderId);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.ProductId, Is.EqualTo(0), "未提供 projectId 时，Order.ProductId 应为 0");
        }

        [Test]
        public async Task CreateAndStartSession_WhenOrderExistsWithProductIdZero_ShouldUpdateProductIdIfProjectIdProvided()
        {
            var orderId = "ORDER004";
            var projectId = "PROJECT004";

            // 第一次创建 Session，不提供 projectId
            var sessionId1 = _sessionService.CreateAndStartSession(orderId, orderId, null);
            var orders1 = await _storage.GetAllOrdersAsync();
            var order1 = orders1.FirstOrDefault(o => o.OrderName == orderId);
            Assert.That(order1.ProductId, Is.EqualTo(0), "第一次创建时 ProductId 应为 0");

            // 结束 Session
            _sessionService.EndSession();

            // 第二次创建 Session，提供 projectId，应该更新 Order 的 ProductId
            var sessionId2 = _sessionService.CreateAndStartSession(orderId, orderId, projectId);
            var orders2 = await _storage.GetAllOrdersAsync();
            var order2 = orders2.FirstOrDefault(o => o.OrderName == orderId);

            Assert.That(order2.ProductId, Is.Not.EqualTo(0), "提供 projectId 后，Order.ProductId 应该被更新");
            
            // 验证 Product 已创建
            var productId = await _storage.GetProductIdByProductNameAsync(projectId);
            Assert.That(productId, Is.Not.Null);
            Assert.That(order2.ProductId, Is.EqualTo(productId.Value), "Order.ProductId 应该等于创建的 Product.Id");
        }

        [Test]
        public async Task CreateAndStartSession_WhenOrderAlreadyHasProductId_ShouldNotUpdateProductId()
        {
            var orderId = "ORDER005";
            var projectId1 = "PROJECT005A";
            var projectId2 = "PROJECT005B";

            // 第一次创建 Session，使用 projectId1
            var sessionId1 = _sessionService.CreateAndStartSession(orderId, orderId, projectId1);
            var productId1 = await _storage.GetProductIdByProductNameAsync(projectId1);
            var orders1 = await _storage.GetAllOrdersAsync();
            var order1 = orders1.FirstOrDefault(o => o.OrderName == orderId);
            Assert.That(order1.ProductId, Is.EqualTo(productId1.Value));

            // 结束 Session
            _sessionService.EndSession();

            // 第二次创建 Session，使用不同的 projectId2，但 Order 已存在且 ProductId 不为 0
            // 根据当前实现，不会更新已存在的 Order 的 ProductId（只有当 ProductId == 0 时才更新）
            var sessionId2 = _sessionService.CreateAndStartSession(orderId, orderId, projectId2);
            var orders2 = await _storage.GetAllOrdersAsync();
            var order2 = orders2.FirstOrDefault(o => o.OrderName == orderId);

            // Order 的 ProductId 应该保持不变（仍为 projectId1 的 ProductId）
            Assert.That(order2.ProductId, Is.EqualTo(productId1.Value), "Order 已有 ProductId 时不应被更新");
        }

        [Test]
        public void CreateAndStartSession_WhenOrderIdIsEmpty_ShouldThrowArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _sessionService.CreateAndStartSession("", null, "PROJECT");
            });
        }

        [Test]
        public void CreateAndStartSession_WhenSessionAlreadyActive_ShouldThrowInvalidOperationException()
        {
            var orderId1 = "ORDER006";
            var orderId2 = "ORDER007";

            _sessionService.CreateAndStartSession(orderId1, orderId1, "PROJECT006");

            Assert.Throws<InvalidOperationException>(() =>
            {
                _sessionService.CreateAndStartSession(orderId2, orderId2, "PROJECT007");
            });
        }
    }
}
