/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：Session 生命周期服务实现。使用 SessionIdGenerator.Format 生成 SessionId，禁止手写或拼接。
/// </remarks>

using System;
using System.Threading.Tasks;
using System.Linq;
using SnVerify.Domain.Models;
using SnVerify.Domain.Validation;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Session
{
    /// <summary>
    /// Session 生命周期服务实现：创建/开始/结束 Session，当前 Session 查询；与 Start/End 按钮逻辑挂接。
    /// </summary>
    public class SessionLifecycleService : ISessionLifecycleService
    {
        private readonly IStorageService _storage;
        private readonly IFileLogger _logger;
        private readonly object _snapshotLock = new object();
        private SessionSnapshot _snapshot;

        /// <inheritdoc />
        public SessionSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? SessionSnapshot.Idle();
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
        /// 初始化 Session 生命周期服务
        /// </summary>
        public SessionLifecycleService(IStorageService storage, IFileLogger logger = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? new NullFileLogger();
            _snapshot = SessionSnapshot.Idle();
        }

        /// <inheritdoc />
        public string CreateAndStartSession(string orderId, string orderName = null, string projectId = null, string productCode = null)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                Snapshot = SessionSnapshot.Error("OrderId 不能为空");
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));
            }

            lock (_snapshotLock)
            {
                if (Snapshot.IsActive)
                {
                    Snapshot = SessionSnapshot.Error($"已有活动 Session {Snapshot.SessionId}，无法开始新 Session", null);
                    throw new InvalidOperationException($"已有活动 Session {Snapshot.SessionId}，无法开始新 Session");
                }
            }

            var at = DateTime.Now;
            var sessionId = SessionIdGenerator.Format(orderId, at);

            try
            {
                // 防止同一秒内快速 End/Start 导致 SessionName 重复（SessionId 格式到秒）
                // 仅通过调整传入 Format 的时间来保证仍符合既定格式：OrderId_yyyyMMdd_HHmmss
                var guard = 0;
                while (_storage.SessionExistsAsync(sessionId).GetAwaiter().GetResult())
                {
                    at = at.AddSeconds(1);
                    sessionId = SessionIdGenerator.Format(orderId, at);
                    guard++;
                    if (guard > 10)
                        throw new InvalidOperationException("生成 SessionId 失败：短时间内重复冲突过多");
                }

                // Phase 2.5：Order 使用新的模型（Id / OrderName / ProductId / CreatedAt），
                // 这里按订单名称（orderName ?? orderId）进行存在性检查与创建。
                var displayOrderName = orderName ?? orderId;

                // 解析或创建 Product（项目名 = ProductName），供 Order 关联
                int productId = 0;
                if (!string.IsNullOrWhiteSpace(projectId))
                {
                    var existingProductId = _storage.GetProductIdByProductNameAsync(projectId.Trim()).GetAwaiter().GetResult();
                    if (existingProductId.HasValue)
                    {
                        productId = existingProductId.Value;
                    }
                    else
                    {
                        var product = new Product
                        {
                            ProductName = projectId.Trim(),
                            ProductCode = string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim(),
                            Description = null,
                            CreatedAt = at
                        };
                        productId = _storage.CreateProductAsync(product).GetAwaiter().GetResult();
                    }
                }

                // 按订单名称 + 项目（ProductId）联合判断：该项目下该订单是否已存在；不存在则创建（OrderName + ProductId 唯一）
                var orderExists = _storage.OrderExistsByOrderNameAndProductAsync(displayOrderName, productId).GetAwaiter().GetResult();
                if (!orderExists)
                {
                    var order = new Order
                    {
                        OrderName = displayOrderName,
                        ProductId = productId,
                        CreatedAt = at
                    };
                    _storage.CreateOrderAsync(order).GetAwaiter().GetResult();
                }

                // 从当前订单列表中根据 OrderName + ProductId 找到对应的内部 Id，供 TestSession 使用。
                var allOrders = _storage.GetAllOrdersAsync().GetAwaiter().GetResult();
                var orderEntity = allOrders.FirstOrDefault(o => o.OrderName == displayOrderName && o.ProductId == productId);
                if (orderEntity == null)
                {
                    throw new InvalidOperationException($"未能找到订单记录: {displayOrderName} (项目Id={productId})");
                }

                var session = new TestSession
                {
                    // 使用业务可读 SessionName（等同于 SessionId 字符串）
                    SessionName = sessionId,
                    OrderId = orderEntity.Id,
                    StartTime = at,
                    // Status 可按需扩展，这里保持空值
                };
                _storage.CreateSessionAsync(session).GetAwaiter().GetResult();

                Snapshot = SessionSnapshot.Active(sessionId, orderId, at);
                _logger?.LogInfo($"Session 创建并开始: SessionId={sessionId}, OrderId={orderId}");
                return sessionId;
            }
            catch (Exception ex)
            {
                Snapshot = SessionSnapshot.Error($"创建 Session 失败: {ex.Message}", sessionId);
                _logger?.LogError($"创建 Session 失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <inheritdoc />
        public void StartSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                Snapshot = SessionSnapshot.Error("SessionId 不能为空");
                throw new ArgumentException("SessionId 不能为空", nameof(sessionId));
            }

            lock (_snapshotLock)
            {
                if (Snapshot.IsActive)
                {
                    Snapshot = SessionSnapshot.Error($"已有活动 Session {Snapshot.SessionId}，无法开始 {sessionId}", sessionId);
                    throw new InvalidOperationException($"已有活动 Session {Snapshot.SessionId}，无法开始新 Session");
                }
            }

            var exists = _storage.SessionExistsAsync(sessionId).GetAwaiter().GetResult();
            if (!exists)
            {
                Snapshot = SessionSnapshot.Error($"Session {sessionId} 不存在", sessionId);
                throw new InvalidOperationException($"Session {sessionId} 不存在");
            }
            var startTime = DateTime.Now;
            Snapshot = SessionSnapshot.Active(sessionId, null, startTime);
            _logger?.LogInfo($"Session 开始: SessionId={sessionId}");
        }

        /// <inheritdoc />
        public void EndSession()
        {
            lock (_snapshotLock)
            {
                if (!Snapshot.IsActive)
                {
                    Snapshot = SessionSnapshot.Error("没有活动 Session，无法结束");
                    throw new InvalidOperationException("没有活动 Session，无法结束");
                }

                var sessionId = Snapshot.SessionId;
                var orderId = Snapshot.OrderId;
                var startTime = Snapshot.StartTime ?? DateTime.Now;
                var endTime = DateTime.Now;
                Snapshot = SessionSnapshot.Ended(sessionId, orderId, startTime, endTime);
                _logger?.LogInfo($"Session 结束: SessionId={sessionId}");
            }
        }

        /// <inheritdoc />
        public string GetCurrentSessionId()
        {
            var s = Snapshot;
            return s.IsActive ? s.SessionId : null;
        }
    }
}
