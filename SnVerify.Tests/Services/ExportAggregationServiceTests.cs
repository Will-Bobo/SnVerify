using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SnVerify.Domain.Export;
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
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
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
        public async Task ExportByOrderIdAsync_Should_Include_Actual_SessionLogs()
        {
            // Arrange
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logServiceMock = new Mock<ILoggingService>(MockBehavior.Strict);
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

            // 空 Session 优化：仅当 ExportBySessionAsync 生成 Excel 时才导出日志；Mock 需创建 Excel 文件
            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ExportRecordFilter>()))
                .Callback<int, string, ExportRecordFilter>((id, dir, _) =>
                {
                    using (var pkg = new ExcelPackage())
                    {
                        pkg.Workbook.Worksheets.Add("PASS");
                        pkg.SaveAs(new FileInfo(Path.Combine(dir, $"{id}.xlsx")));
                    }
                })
                .Returns(Task.CompletedTask);

            // 为每个 Session 创建对应的日志文件，并通过 ILoggingService 暴露路径
            foreach (var s in sessions)
            {
                var logPath = Path.Combine(_outputDir, $"session_{s.SessionName}.log");
                File.WriteAllText(logPath, $"LOG-{s.SessionName}");
                logServiceMock
                    .Setup(ls => ls.GetLogFilePath(s.SessionName))
                    .Returns(logPath);
            }

            var service = new ExportAggregationService(storageMock.Object, logger, logServiceMock.Object);

            // Act
            await service.ExportByOrderIdAsync(orderName, _outputDir);

            // Assert
            var zipPath = Path.Combine(_outputDir, $"{orderName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "应生成按订单命名的 ZIP 文件");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);

                var expected1Log = $"{orderName}/{sessions[0].SessionName}.log";
                var expected2Log = $"{orderName}/{sessions[1].SessionName}.log";

                Assert.That(entries.ContainsKey(expected1Log), Is.True, "应包含 Session1 的日志文件");
                Assert.That(entries.ContainsKey(expected2Log), Is.True, "应包含 Session2 的日志文件");

                // 内容应与 LogService 提供的日志完全一致
                foreach (var s in sessions)
                {
                    var expectedEntryName = $"{orderName}/{s.SessionName}.log";
                    using (var entryStream = entries[expectedEntryName].Open())
                    using (var reader = new StreamReader(entryStream))
                    {
                        var zipContent = reader.ReadToEnd();
                        var logPath = Path.Combine(_outputDir, $"session_{s.SessionName}.log");
                        var originalContent = File.ReadAllText(logPath);
                        Assert.That(zipContent, Is.EqualTo(originalContent), $"ZIP 中 {expectedEntryName} 内容应与 LogService 日志一致");
                    }
                }
            }

            storageMock.VerifyAll();
            logServiceMock.VerifyAll();
        }

        [Test]
        public async Task ExportByProjectIdAsync_Should_Include_Actual_SessionLogs()
        {
            // Arrange
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logServiceMock = new Mock<ILoggingService>(MockBehavior.Strict);
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
                .Setup(s => s.GetAllOrdersAsync())
                .ReturnsAsync(new[] { order1, order2 });

            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ExportRecordFilter>()))
                .Callback<int, string, ExportRecordFilter>((id, dir, _) =>
                {
                    using (var pkg = new ExcelPackage())
                    {
                        pkg.Workbook.Worksheets.Add("PASS");
                        pkg.SaveAs(new FileInfo(Path.Combine(dir, $"{id}.xlsx")));
                    }
                })
                .Returns(Task.CompletedTask);

            // 为每个 Session 创建对应的日志文件，并通过 ILoggingService 暴露路径
            foreach (var s in sessions)
            {
                var logPath = Path.Combine(_outputDir, $"session_{s.SessionName}.log");
                File.WriteAllText(logPath, $"LOG-{s.SessionName}");
                logServiceMock
                    .Setup(ls => ls.GetLogFilePath(s.SessionName))
                    .Returns(logPath);
            }

            var service = new ExportAggregationService(storageMock.Object, logger, logServiceMock.Object);

            // Act
            await service.ExportByProjectIdAsync(productName, _outputDir);

            // Assert
            var zipPath = Path.Combine(_outputDir, $"{productName}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "应生成按产品命名的 ZIP 文件");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);

                var expected1Log = $"{productName}/{order1.OrderName}/{sessions[0].SessionName}.log";
                var expected2Log = $"{productName}/{order2.OrderName}/{sessions[1].SessionName}.log";

                Assert.That(entries.ContainsKey(expected1Log), Is.True, "应包含 Order_Alpha 的 Session 日志");
                Assert.That(entries.ContainsKey(expected2Log), Is.True, "应包含 Order_Beta 的 Session 日志");

                foreach (var s in sessions)
                {
                    var orderName = s.OrderId == order1.Id ? order1.OrderName : order2.OrderName;
                    var expectedEntryName = $"{productName}/{orderName}/{s.SessionName}.log";
                    using (var entryStream = entries[expectedEntryName].Open())
                    using (var reader = new StreamReader(entryStream))
                    {
                        var zipContent = reader.ReadToEnd();
                        var logPath = Path.Combine(_outputDir, $"session_{s.SessionName}.log");
                        var originalContent = File.ReadAllText(logPath);
                        Assert.That(zipContent, Is.EqualTo(originalContent), $"ZIP 中 {expectedEntryName} 内容应与 LogService 日志一致");
                    }
                }
            }

            storageMock.VerifyAll();
            logServiceMock.VerifyAll();
        }

        [Test]
        public async Task Exported_Log_Content_Should_Be_Identical_To_LogService()
        {
            // Arrange
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logServiceMock = new Mock<ILoggingService>(MockBehavior.Strict);
            var logger = new NullFileLogger();

            var productName = "Prod_Log";
            var order = new Order { Id = 4001, OrderName = "Order_Log", ProductId = 1 };
            var sessionName = "Order_Log_20260126_150000";

            var sessions = new[]
            {
                new TestSession { Id = 501, OrderId = order.Id, SessionName = sessionName, StartTime = DateTime.Now }
            };

            storageMock
                .Setup(s => s.GetSessionsByProjectIdAsync(productName))
                .ReturnsAsync(sessions);

            storageMock
                .Setup(s => s.GetAllOrdersAsync())
                .ReturnsAsync(new[] { order });

            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ExportRecordFilter>()))
                .Callback<int, string, ExportRecordFilter>((id, dir, _) =>
                {
                    using (var pkg = new ExcelPackage())
                    {
                        pkg.Workbook.Worksheets.Add("PASS");
                        pkg.SaveAs(new FileInfo(Path.Combine(dir, $"{id}.xlsx")));
                    }
                })
                .Returns(Task.CompletedTask);

            // 为单个 Session 写入多行日志，验证导出内容逐字一致
            var logPath = Path.Combine(_outputDir, $"session_{sessionName}.log");
            var originalLines = new[]
            {
                "FIRST LINE",
                "SECOND LINE",
                "THIRD LINE"
            };
            File.WriteAllLines(logPath, originalLines);

            logServiceMock
                .Setup(ls => ls.GetLogFilePath(sessionName))
                .Returns(logPath);

            var service = new ExportAggregationService(storageMock.Object, logger, logServiceMock.Object);

            // Act
            await service.ExportByProjectIdAsync(productName, _outputDir);

            // Assert
            var safeProduct = "Prod_Log";
            var zipPath = Path.Combine(_outputDir, $"{safeProduct}.zip");
            Assert.That(File.Exists(zipPath), Is.True, "ZIP 文件名应存在");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.Single(e => e.FullName.EndsWith($"{sessionName}.log"));
                using (var entryStream = entry.Open())
                using (var reader = new StreamReader(entryStream))
                {
                    var exportedContent = reader.ReadToEnd();
                    var originalContent = string.Join(Environment.NewLine, originalLines) + Environment.NewLine;
                    Assert.That(exportedContent, Is.EqualTo(originalContent), "导出的日志内容应与 LogService 源日志逐字一致");
                }
            }

            storageMock.VerifyAll();
            logServiceMock.VerifyAll();
        }

        /// <summary>
        /// 异常场景：单个 Session 导出异常，其他 Session 正常导出，异常被记录不抛出
        /// </summary>
        [Test]
        public async Task ExportByOrderId_WhenOneSessionFails_OthersContinueAndLogError()
        {
            var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
            var logServiceMock = new Mock<ILoggingService>(MockBehavior.Strict);
            var logger = new NullFileLogger();

            var orderName = "Order_Ex";
            var sessions = new[]
            {
                new TestSession { Id = 201, OrderId = 1001, SessionName = "Order_Ex_20260126_100000", StartTime = DateTime.Now },
                new TestSession { Id = 202, OrderId = 1001, SessionName = "Order_Ex_20260126_110000", StartTime = DateTime.Now }
            };

            storageMock.Setup(s => s.GetSessionsByOrderIdAsync(orderName)).ReturnsAsync(sessions);

            storageMock
                .Setup(s => s.ExportBySessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ExportRecordFilter>()))
                .Callback<int, string, ExportRecordFilter>((id, dir, _) =>
                {
                    if (id == 201)
                        throw new InvalidOperationException("模拟 Session 201 导出失败");
                    using (var pkg = new ExcelPackage())
                    {
                        pkg.Workbook.Worksheets.Add("PASS");
                        pkg.SaveAs(new FileInfo(Path.Combine(dir, $"{id}.xlsx")));
                    }
                })
                .Returns(Task.CompletedTask);

            var logPath2 = Path.Combine(_outputDir, "session_Order_Ex_20260126_110000.log");
            File.WriteAllText(logPath2, "LOG-Session2");
            logServiceMock.Setup(ls => ls.GetLogFilePath("Order_Ex_20260126_110000")).Returns(logPath2);

            var service = new ExportAggregationService(storageMock.Object, logger, logServiceMock.Object);

            await service.ExportByOrderIdAsync(orderName, _outputDir);

            var zipPath = Path.Combine(_outputDir, $"{orderName}.zip");
            Assert.That(File.Exists(zipPath), Is.True);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(e => e.FullName, e => e);
                Assert.That(entries.ContainsKey($"{orderName}/Order_Ex_20260126_100000.xlsx"), Is.False, "失败 Session 不应有 Excel");
                Assert.That(entries.ContainsKey($"{orderName}/Order_Ex_20260126_110000.xlsx"), Is.True, "成功 Session 应有 Excel");
                Assert.That(entries.ContainsKey($"{orderName}/Order_Ex_20260126_110000.log"), Is.True, "成功 Session 应有日志");
            }
        }
    }
}

