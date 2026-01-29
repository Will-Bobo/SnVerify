using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class ExportAggregationServiceTests
    {
        private string _outputDir;

        [SetUp]
        public void SetUp()
        {
            _outputDir = Path.Combine(Path.GetTempPath(), $"SnVerify_ExportAggregation_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_outputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir))
            {
                Directory.Delete(_outputDir, true);
            }
        }

        [Test]
        public async Task ExportByOrderIdAsync_ShouldCreateZipWithExpectedStructure_AndNotUseIds()
        {
            // Arrange
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logger = new NullFileLogger();

            var orderName = "Order_A";
            var sessions = new[]
            {
                new TestSession { Id = 101, OrderId = 1001, SessionName = "Order_A_20260126_100000", StartTime = DateTime.Now },
                new TestSession { Id = 102, OrderId = 1001, SessionName = "Order_A_20260126_110000", StartTime = DateTime.Now }
            };

            storageMock
                .Setup(s => s.GetSessionsByOrderIdAsync(orderName))
                .ReturnsAsync(sessions);

            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>()))
                .Returns<int, string>((sessionId, dir) =>
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, $"{sessionId}.xlsx"), $"xlsx-{sessionId}");
                    File.WriteAllText(Path.Combine(dir, $"{sessionId}.txt"), $"txt-{sessionId}");
                    return Task.CompletedTask;
                });

            var service = new ExportAggregationService(storageMock.Object, logger);

            // Act
            await service.ExportByOrderIdAsync(orderName, _outputDir);

            // Assert
            var zipPath = Path.Combine(_outputDir, $"{orderName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "应生成按订单命名的 ZIP 文件");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();

                var expected1Xlsx = $"{orderName}/{sessions[0].SessionName}.xlsx";
                var expected1Txt = $"{orderName}/{sessions[0].SessionName}.txt";
                var expected2Xlsx = $"{orderName}/{sessions[1].SessionName}.xlsx";
                var expected2Txt = $"{orderName}/{sessions[1].SessionName}.txt";

                Assert.That(entryNames, Does.Contain(expected1Xlsx), "应包含 Session1 的 xlsx");
                Assert.That(entryNames, Does.Contain(expected1Txt), "应包含 Session1 的 txt");
                Assert.That(entryNames, Does.Contain(expected2Xlsx), "应包含 Session2 的 xlsx");
                Assert.That(entryNames, Does.Contain(expected2Txt), "应包含 Session2 的 txt");

                // 不应在任何文件或目录名中直接使用内部 Id（101/102/1001）
                Assert.That(entryNames.All(n => !n.Contains("101") && !n.Contains("102") && !n.Contains("1001")),
                    Is.True,
                    "ZIP 内路径不应包含内部数据库 Id");
            }

            storageMock.VerifyAll();
        }

        [Test]
        public async Task ExportByProjectIdAsync_ShouldCreateZipWithExpectedStructure_AndNotUseIds()
        {
            // Arrange
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logger = new NullFileLogger();

            var productName = "Product_X";

            var order1 = new Order { Id = 2001, OrderName = "Order_Alpha", ProductId = 1 };
            var order2 = new Order { Id = 2002, OrderName = "Order_Beta", ProductId = 1 };

            var sessions = new[]
            {
                new TestSession { Id = 301, OrderId = order1.Id, SessionName = "Order_Alpha_20260126_120000", StartTime = DateTime.Now },
                new TestSession { Id = 302, OrderId = order2.Id, SessionName = "Order_Beta_20260126_130000", StartTime = DateTime.Now }
            };

            storageMock
                .Setup(s => s.GetSessionsByProjectIdAsync(productName))
                .ReturnsAsync(sessions);

            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>()))
                .Returns<int, string>((sessionId, dir) =>
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, $"{sessionId}.xlsx"), $"xlsx-{sessionId}");
                    File.WriteAllText(Path.Combine(dir, $"{sessionId}.txt"), $"txt-{sessionId}");
                    return Task.CompletedTask;
                });

            storageMock
                .Setup(s => s.GetAllOrdersAsync())
                .ReturnsAsync(new[] { order1, order2 });

            var service = new ExportAggregationService(storageMock.Object, logger);

            // Act
            await service.ExportByProjectIdAsync(productName, _outputDir);

            // Assert
            var zipPath = Path.Combine(_outputDir, $"{productName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "应生成按产品命名的 ZIP 文件");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();

                var expected1Xlsx = $"{productName}/{order1.OrderName}/{sessions[0].SessionName}.xlsx";
                var expected1Txt = $"{productName}/{order1.OrderName}/{sessions[0].SessionName}.txt";
                var expected2Xlsx = $"{productName}/{order2.OrderName}/{sessions[1].SessionName}.xlsx";
                var expected2Txt = $"{productName}/{order2.OrderName}/{sessions[1].SessionName}.txt";

                Assert.That(entryNames, Does.Contain(expected1Xlsx), "应包含 Order_Alpha 的 Session xlsx");
                Assert.That(entryNames, Does.Contain(expected1Txt), "应包含 Order_Alpha 的 Session txt");
                Assert.That(entryNames, Does.Contain(expected2Xlsx), "应包含 Order_Beta 的 Session xlsx");
                Assert.That(entryNames, Does.Contain(expected2Txt), "应包含 Order_Beta 的 Session txt");

                // 不应在任何文件或目录名中直接使用内部 Id（2001/2002/301/302）
                Assert.That(entryNames.All(n =>
                        !n.Contains("2001") && !n.Contains("2002") &&
                        !n.Contains("301") && !n.Contains("302")),
                    Is.True,
                    "ZIP 内路径不应包含内部数据库 Id");
            }

            storageMock.VerifyAll();
        }

        [Test]
        public async Task Export_ShouldSanitizeIllegalCharactersInNames()
        {
            // Arrange
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logger = new NullFileLogger();

            var rawProductName = "Prod/Name:001";
            var rawOrderName = "Order*Name?002";
            var rawSessionName = "Sess\"Name<2026>|01";

            var order = new Order { Id = 4001, OrderName = rawOrderName, ProductId = 1 };

            var sessions = new[]
            {
                new TestSession { Id = 501, OrderId = order.Id, SessionName = rawSessionName, StartTime = DateTime.Now }
            };

            storageMock
                .Setup(s => s.GetSessionsByProjectIdAsync(rawProductName))
                .ReturnsAsync(sessions);

            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>()))
                .Returns<int, string>((sessionId, dir) =>
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, $"{sessionId}.xlsx"), $"xlsx-{sessionId}");
                    File.WriteAllText(Path.Combine(dir, $"{sessionId}.txt"), $"txt-{sessionId}");
                    return Task.CompletedTask;
                });

            storageMock
                .Setup(s => s.GetAllOrdersAsync())
                .ReturnsAsync(new[] { order });

            var service = new ExportAggregationService(storageMock.Object, logger);

            // Act
            await service.ExportByProjectIdAsync(rawProductName, _outputDir);

            // Assert
            // 非法字符应被替换为下划线
            var safeProduct = "Prod_Name_001";
            var zipPath = Path.Combine(_outputDir, $"{safeProduct}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "ZIP 文件名应为经过安全处理的 ProductName");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entryNames = archive.Entries.Select(e => e.FullName).ToList();
                var safeOrder = "Order_Name_002";
                var safeSession = "Sess_Name_2026__01";

                var expectedXlsx = $"{safeProduct}/{safeOrder}/{safeSession}.xlsx";
                var expectedTxt = $"{safeProduct}/{safeOrder}/{safeSession}.txt";

                Assert.That(entryNames, Does.Contain(expectedXlsx), "应使用安全化后的 SessionName 作为文件名（xlsx）");
                Assert.That(entryNames, Does.Contain(expectedTxt), "应使用安全化后的 SessionName 作为文件名（txt）");

                // ZIP 内部路径不应包含任何非法字符
                Assert.That(entryNames.All(name =>
                        !name.Contains("/") || name.StartsWith("Prod_")), // 只有目录分隔符允许
                    Is.True);
                Assert.That(entryNames.All(name =>
                        !name.Contains(":") &&
                        !name.Contains("*") &&
                        !name.Contains("?") &&
                        !name.Contains("\"") &&
                        !name.Contains("<") &&
                        !name.Contains(">") &&
                        !name.Contains("|")),
                    Is.True,
                    "ZIP 内路径不应包含未被替换的非法字符");
            }

            storageMock.VerifyAll();
        }
    }
}

