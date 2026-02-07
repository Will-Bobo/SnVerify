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

            // 验证 Order 已关联到 Product（按 OrderName + ProductId 查找）
            var orders = await _storage.GetAllOrdersAsync();
            var order = orders.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == productId.Value);
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
            
            // 验证两个 Order 都关联到同一个 Product（OrderName + ProductId 唯一）
            var orders = await _storage.GetAllOrdersAsync();
            var order1 = orders.FirstOrDefault(o => o.OrderName == orderId1 && o.ProductId == productId1.Value);
            var order2 = orders.FirstOrDefault(o => o.OrderName == orderId2 && o.ProductId == productId1.Value);
            Assert.That(order1, Is.Not.Null);
            Assert.That(order2, Is.Not.Null);
            Assert.That(order1.ProductId, Is.EqualTo(productId1.Value));
            Assert.That(order2.ProductId, Is.EqualTo(productId1.Value));
        }

        [Test]
        public async Task CreateAndStartSession_WhenProjectIdIsEmpty_ShouldCreateOrderWithProductIdZero()
        {
            var orderId = "ORDER003";

            var sessionId = _sessionService.CreateAndStartSession(orderId, orderId, null);

            Assert.That(sessionId, Is.Not.Null);

            // 验证 Order 的 ProductId 为 0（无项目时）
            var orders = await _storage.GetAllOrdersAsync();
            var order = orders.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == 0);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.ProductId, Is.EqualTo(0), "未提供 projectId 时，Order.ProductId 应为 0");
        }

        /// <summary>
        /// 同一 OrderName 先无项目、后有项目时：创建两条订单（OrderName + ProductId 唯一），项目下的订单使用新创建的 Product。
        /// </summary>
        [Test]
        public async Task CreateAndStartSession_WhenOrderExistsWithProductIdZero_ThenWithProject_ShouldCreateNewOrderForProject()
        {
            var orderId = "ORDER004";
            var projectId = "PROJECT004";

            // 第一次创建 Session，不提供 projectId
            var sessionId1 = _sessionService.CreateAndStartSession(orderId, orderId, null);
            var orders1 = await _storage.GetAllOrdersAsync();
            var order1 = orders1.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == 0);
            Assert.That(order1, Is.Not.Null, "第一次创建时应有 (OrderName, ProductId=0) 的订单");
            Assert.That(order1.ProductId, Is.EqualTo(0));

            // 结束 Session
            _sessionService.EndSession();

            // 第二次创建 Session，提供 projectId：应在该项目下创建新订单（不更新已有订单）
            var sessionId2 = _sessionService.CreateAndStartSession(orderId, orderId, projectId);
            var productId = await _storage.GetProductIdByProductNameAsync(projectId);
            Assert.That(productId, Is.Not.Null);

            var orders2 = await _storage.GetAllOrdersAsync();
            var orderWithZero = orders2.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == 0);
            var orderWithProject = orders2.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == productId.Value);

            Assert.That(orderWithZero, Is.Not.Null, "原无项目订单应保留");
            Assert.That(orderWithProject, Is.Not.Null, "应创建该项目下的新订单");
            Assert.That(orderWithProject.ProductId, Is.EqualTo(productId.Value));
        }

        /// <summary>
        /// 同一 OrderName 在不同项目下：应创建各自的订单（OrderName + ProductId 唯一）。
        /// </summary>
        [Test]
        public async Task CreateAndStartSession_WhenSameOrderNameInDifferentProjects_ShouldCreateSeparateOrders()
        {
            var orderId = "ORDER005";
            var projectId1 = "PROJECT005A";
            var projectId2 = "PROJECT005B";

            // 第一次创建 Session，使用 projectId1
            var sessionId1 = _sessionService.CreateAndStartSession(orderId, orderId, projectId1);
            var productId1 = await _storage.GetProductIdByProductNameAsync(projectId1);
            var orders1 = await _storage.GetAllOrdersAsync();
            var order1 = orders1.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == productId1.Value);
            Assert.That(order1, Is.Not.Null);
            Assert.That(order1.ProductId, Is.EqualTo(productId1.Value));

            // 结束 Session
            _sessionService.EndSession();

            // 第二次创建 Session，使用不同的 projectId2：应创建该项目的订单
            var sessionId2 = _sessionService.CreateAndStartSession(orderId, orderId, projectId2);
            var productId2 = await _storage.GetProductIdByProductNameAsync(projectId2);
            var orders2 = await _storage.GetAllOrdersAsync();

            var orderForP1 = orders2.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == productId1.Value);
            var orderForP2 = orders2.FirstOrDefault(o => o.OrderName == orderId && o.ProductId == productId2.Value);

            Assert.That(orderForP1, Is.Not.Null, "项目1的订单应保留");
            Assert.That(orderForP2, Is.Not.Null, "项目2下应创建新订单");
            Assert.That(orders2.Count(o => o.OrderName == orderId), Is.EqualTo(2), "同一 OrderName 在不同项目下应有两条订单");
        }

        /// <summary>
        /// 验证：当 orderName 与 orderId 不同时，应使用 displayOrderName 判断该项目下订单是否存在，应复用而不重复创建。
        /// </summary>
        [Test]
        public async Task CreateAndStartSession_WhenOrderNameDiffersFromOrderId_ShouldUseDisplayOrderNameForExistenceCheck()
        {
            var displayOrderName = "DISPLAY_ORDER_001";
            var projectId = "PROJECT_CHECK";

            // 第一次创建 Session，使用 displayOrderName 作为 orderId 和 orderName
            _sessionService.CreateAndStartSession(displayOrderName, displayOrderName, projectId);
            _sessionService.EndSession();

            // 第二次创建 Session：orderId 不同，但 orderName 与已有订单相同、同一项目
            // 应复用该项目下已有订单，不创建重复
            var sessionId = _sessionService.CreateAndStartSession("DifferentOrderId", displayOrderName, projectId);

            Assert.That(sessionId, Is.Not.Null);
            var productId = await _storage.GetProductIdByProductNameAsync(projectId);
            var orders = await _storage.GetAllOrdersAsync();
            Assert.That(orders.Count(o => o.OrderName == displayOrderName && o.ProductId == productId.Value), Is.EqualTo(1), "同项目下不应创建重复订单");
        }

        /// <summary>
        /// 验证：同一 OrderName、同一项目下，应复用已有订单，不创建重复（OrderName + ProductId 唯一）。
        /// </summary>
        [Test]
        public async Task CreateAndStartSession_WhenOrderExistsUnderProduct_ShouldNotCreateDuplicate()
        {
            var orderName = "ORDER_SAME_PROJECT";
            var projectId = "PROJECT_UNIQUE";

            _sessionService.CreateAndStartSession(orderName, orderName, projectId);
            _sessionService.EndSession();

            // 同一订单名、同一项目：应复用已有订单，不创建新订单
            var sessionId = _sessionService.CreateAndStartSession(orderName, orderName, projectId);
            Assert.That(sessionId, Is.Not.Null);

            var productId = await _storage.GetProductIdByProductNameAsync(projectId);
            var orders = await _storage.GetAllOrdersAsync();
            Assert.That(orders.Count(o => o.OrderName == orderName && o.ProductId == productId.Value), Is.EqualTo(1), "同项目下同名订单应唯一");
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
