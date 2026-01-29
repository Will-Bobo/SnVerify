using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Coordination;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// Phase 2.5：围绕 TestRecord / Session 维度的 SN 历史与 Session 内重复 SN 行为测试。
    /// 不依赖任何 Batch / SnVerifyResult 结构。
    /// </summary>
    [TestFixture]
    public class SnHistoryAndSessionTests
    {
        private string _dbPath = null!;
        private IStorageService _storageService = null!;

        [SetUp]
        public async Task SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"SnVerify_Phase25_{Guid.NewGuid():N}.db");
            _storageService = new StorageService(_dbPath);
            await _storageService.InitializeAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _storageService.Dispose();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        /// <summary>
        /// SN 历史 PASS 查询：仅 Result = 'PASS' 的 TestRecord 参与历史判断，
        /// FAIL / TIMEOUT 记录不影响 Is*InPassHistory 结果。
        /// </summary>
        [Test]
        public async Task PassHistoryQueries_ShouldOnlyConsiderPassRecords()
        {
            // Arrange: 创建 Product -> Order -> Session
            var product = new Product { ProductName = "P_PHASE25" };
            await _storageService.CreateProductAsync(product);

            var order = new Order { OrderName = "O_PHASE25", ProductId = product.Id };
            await _storageService.CreateOrderAsync(order);

            var session = new TestSession
            {
                SessionName = "O_PHASE25_20250101_000000",
                OrderId = order.Id,
                StartTime = DateTime.Now
            };
            await _storageService.CreateSessionAsync(session);

            var now = DateTime.Now;

            // 一条 PASS 记录
            await _storageService.SaveTestRecordAsync(new TestRecord
            {
                SessionId = session.Id,
                StickerSN = "S_PASS",
                DeviceSN = "D_PASS",
                Result = "PASS",
                FailReason = null,
                VerifyTime = now
            });

            // 一条 FAIL 记录
            await _storageService.SaveTestRecordAsync(new TestRecord
            {
                SessionId = session.Id,
                StickerSN = "S_FAIL",
                DeviceSN = "D_FAIL",
                Result = "FAIL",
                FailReason = "MISMATCH",
                VerifyTime = now
            });

            // 一条 TIMEOUT 记录
            await _storageService.SaveTestRecordAsync(new TestRecord
            {
                SessionId = session.Id,
                StickerSN = "S_TIMEOUT",
                DeviceSN = null,
                Result = "TIMEOUT",
                FailReason = "TIMEOUT",
                VerifyTime = now
            });

            // Act & Assert: 仅 PASS 记录参与历史 PASS 查询
            Assert.That(await _storageService.IsStickerSnInPassHistoryAsync("S_PASS"), Is.True);
            Assert.That(await _storageService.IsDeviceSnInPassHistoryAsync("D_PASS"), Is.True);
            Assert.That(await _storageService.IsBindingInPassHistoryAsync("S_PASS", "D_PASS"), Is.True);

            Assert.That(await _storageService.IsStickerSnInPassHistoryAsync("S_FAIL"), Is.False);
            Assert.That(await _storageService.IsDeviceSnInPassHistoryAsync("D_FAIL"), Is.False);
            Assert.That(await _storageService.IsStickerSnInPassHistoryAsync("S_TIMEOUT"), Is.False);
        }

        /// <summary>
        /// Session 内重复 SN：同一 Session 中先 PASS 再次扫描同一 StickerSN / DeviceSN，
        /// 第二次应命中规则 2（设备SN已存在）FAIL，且历史 PASS 记录保持不变。
        /// </summary>
        [Test]
        public async Task SessionRepeatSn_ShouldFailSecondTime_AndKeepPassHistory()
        {
            // Arrange: 创建 Product -> Order -> Session，与 ProcessCoordinator 使用的 SessionId 对齐
            var product = new Product { ProductName = "P_REPEAT" };
            await _storageService.CreateProductAsync(product);

            var order = new Order { OrderName = "O_REPEAT", ProductId = product.Id };
            await _storageService.CreateOrderAsync(order);

            var sessionId = "O_REPEAT_20250101_010101";
            var session = new TestSession
            {
                SessionName = sessionId,
                OrderId = order.Id,
                StartTime = DateTime.Now
            };
            await _storageService.CreateSessionAsync(session);

            var adbMock = new Moq.Mock<IAdbAccessService>();
            var loggingMock = new Moq.Mock<ILoggingService>();

            // 两次检验都返回相同 DeviceSN（SetupSequence 使用 Returns(Task.FromResult(...))）
            adbMock
                .SetupSequence(m => m.ReadDeviceSnAsync(Moq.It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(AdbSnReadResult.Success("SN001")))
                .Returns(Task.FromResult(AdbSnReadResult.Success("SN001")));

            loggingMock.Setup(l => l.LogInfo(Moq.It.IsAny<string>()));

            var coordinator = new ProcessCoordinator(
                sessionId,
                _storageService,
                adbMock.Object,
                loggingMock.Object);

            // Act 1: 第一次检验，预期 PASS
            await coordinator.StartVerificationAsync("SN001");
            var snapshot1 = coordinator.Snapshot;

            // Act 2: 同一 Session 再次检验相同 SN，预期 FAIL（设备SN已存在）
            await coordinator.StartVerificationAsync("SN001");
            var snapshot2 = coordinator.Snapshot;

            // Assert：第一次 PASS
            Assert.That(snapshot1.LastResult, Is.EqualTo("PASS"));
            Assert.That(snapshot1.FailReason, Is.Null);

            // 第二次命中规则 2：设备SN已存在
            Assert.That(snapshot2.LastResult, Is.EqualTo("FAIL"));
            Assert.That(snapshot2.FailReason, Is.EqualTo("设备SN已存在"));

            // 历史 PASS 查询仍然认为 SN001 / DeviceSN 在 PASS 历史中存在
            Assert.That(await _storageService.IsStickerSnInPassHistoryAsync("SN001"), Is.True);
            Assert.That(await _storageService.IsDeviceSnInPassHistoryAsync("SN001"), Is.True);
            Assert.That(await _storageService.IsBindingInPassHistoryAsync("SN001", "SN001"), Is.True);
        }

        /// <summary>
        /// FAIL / TIMEOUT 不影响 PASS 历史：
        /// 同一 Session 中先 PASS，再 FAIL/TIMEOUT，PASS 依然保留在历史 PASS 查询中。
        /// </summary>
        [Test]
        public async Task FailOrTimeout_ShouldNotErasePassHistory()
        {
            // Arrange: 复用上面的 Product / Order / Session 创建流程
            var product = new Product { ProductName = "P_FAIL_KEEP_PASS" };
            await _storageService.CreateProductAsync(product);

            var order = new Order { OrderName = "O_FAIL_KEEP_PASS", ProductId = product.Id };
            await _storageService.CreateOrderAsync(order);

            var sessionId = "O_FAIL_KEEP_PASS_20250101_020202";
            var session = new TestSession
            {
                SessionName = sessionId,
                OrderId = order.Id,
                StartTime = DateTime.Now
            };
            await _storageService.CreateSessionAsync(session);

            var adbMock = new Moq.Mock<IAdbAccessService>();
            var loggingMock = new Moq.Mock<ILoggingService>();

            // 第一次：成功读取 SN001 → PASS
            // 第二次：模拟 ADB 超时 → TIMEOUT（SetupSequence 使用 Returns(Task.FromResult(...))）
            adbMock
                .SetupSequence(m => m.ReadDeviceSnAsync(Moq.It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(AdbSnReadResult.Success("SN001")))
                .Returns(Task.FromResult(AdbSnReadResult.Failure("Timeout", isTimeout: true)));

            var coordinator = new ProcessCoordinator(
                sessionId,
                _storageService,
                adbMock.Object,
                loggingMock.Object);

            // Act 1: PASS
            await coordinator.StartVerificationAsync("SN001");

            // Act 2: TIMEOUT
            await coordinator.StartVerificationAsync("SN001");

            var snapshot2 = coordinator.Snapshot;

            // Assert：第二次为 TIMEOUT
            Assert.That(snapshot2.LastResult, Is.EqualTo("TIMEOUT"));
            Assert.That(snapshot2.FailReason, Is.EqualTo("ADB读取设备超时"));

            // 历史 PASS 查询仍然认为 SN001 存在于 PASS 历史中
            Assert.That(await _storageService.IsStickerSnInPassHistoryAsync("SN001"), Is.True);
            Assert.That(await _storageService.IsDeviceSnInPassHistoryAsync("SN001"), Is.True);
            Assert.That(await _storageService.IsBindingInPassHistoryAsync("SN001", "SN001"), Is.True);
        }
    }
}

