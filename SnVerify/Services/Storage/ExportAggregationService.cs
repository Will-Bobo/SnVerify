/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：导出聚合由 B 做。覆盖确认逻辑可由调用方（C）或本服务参数控制，本阶段仅做聚合与逐 Session 导出。
/// </remarks>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Infrastructure.Export;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 导出聚合服务实现：按 OrderId/ProjectId 查 Session 列表，逐 Session 按 ProductCode 选择 Exporter 导出。
    /// </summary>
    public class ExportAggregationService : IExportAggregationService
    {
        private readonly IStorageService _storage;
        private readonly ISessionExporterFactory _exporterFactory;
        private readonly IFileLogger _logger;
        private readonly ILoggingService _loggingService;

        /// <summary>
        /// 初始化导出聚合服务
        /// </summary>
        /// <param name="storage">存储服务（需提供 GetSessionsByOrderIdAsync、GetSessionsByProjectIdAsync、GetProductCodeBySessionIdAsync）</param>
        /// <param name="logger">日志（可选，仅用于记录导出过程）</param>
        /// <param name="loggingService">运行时日志服务（用于查询 Session 对应的日志文件路径）</param>
        /// <param name="exporterFactory">按 ProductCode 选择 Exporter 的工厂；null 时内部创建默认工厂</param>
        public ExportAggregationService(IStorageService storage, IFileLogger logger = null, ILoggingService loggingService = null, ISessionExporterFactory exporterFactory = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? new NullFileLogger();
            _loggingService = loggingService;
            _exporterFactory = exporterFactory ?? new SessionExporterFactory(storage, new ProductExportRegistry(), new DefaultExportValueResolver());
        }

        /// <inheritdoc />
        public async Task ExportByOrderIdAsync(string orderId, string outputDirectory, ExportRecordFilter filter = null)
        {
            filter = filter ?? ExportRecordFilter.All;
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);

            var sessions = await _storage.GetSessionsByOrderIdAsync(orderId);
            if (sessions == null || sessions.Count == 0)
            {
                _logger?.LogInfo($"按订单导出时无 Session: OrderId={orderId}");
                return;
            }

            var safeOrderName = ToSafeFileName(orderId);
            var zipFilePath = Path.Combine(outputDirectory, safeOrderName + ".zip");

            if (File.Exists(zipFilePath))
            {
                throw new InvalidOperationException($"目标 ZIP 已存在，无法导出：{zipFilePath}");
            }

            string tempDir = null;
            try
            {
                tempDir = CreateTempDirectory();

                using (var zipStream = new FileStream(zipFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var s in sessions)
                    {
                        var safeSessionName = ToSafeFileName(s.SessionName);
                        bool sessionHadRecords = false;

                        // 1) 按 ProductCode 选择 Exporter 生成结果表格
                        try
                        {
                            var productCode = await _storage.GetProductCodeBySessionIdAsync(s.Id).ConfigureAwait(false);
                            var exporter = _exporterFactory.GetExporter(productCode);
                            var context = new ExportContext
                            {
                                SessionId = s.Id,
                                SessionName = s.SessionName,
                                OutputDirectory = tempDir,
                                Filter = filter
                            };
                            await exporter.ExportAsync(context).ConfigureAwait(false);
                            var excelPath = Path.Combine(tempDir, $"{s.Id}.xlsx");
                            if (File.Exists(excelPath))
                            {
                                sessionHadRecords = true;
                                var excelEntryName = $"{safeOrderName}/{safeSessionName}.xlsx";
                                archive.CreateEntryFromFile(excelPath, excelEntryName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"按订单导出 Session={s.SessionName} 表格失败：{ex.Message}", ex);
                        }

                        // 2) 空 Session 不导出日志：仅当有记录（生成了 Excel）时才导出日志
                        if (!sessionHadRecords || _loggingService == null)
                            continue;

                        var sessionLogPath = _loggingService.GetLogFilePath(s.SessionName);
                        if (string.IsNullOrEmpty(sessionLogPath) || !File.Exists(sessionLogPath))
                            continue;

                        var logEntryName = $"{safeOrderName}/{safeSessionName}.log";
                        archive.CreateEntryFromFile(sessionLogPath, logEntryName);
                    }
                }
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }

            _logger?.LogInfo($"按订单导出 ZIP 完成: OrderId={orderId}, SessionCount={sessions.Count}, Zip={zipFilePath}");
        }

        /// <inheritdoc />
        public async Task ExportByProjectIdAsync(string projectId, string outputDirectory, ExportRecordFilter filter = null)
        {
            filter = filter ?? ExportRecordFilter.All;
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);

            var sessions = await _storage.GetSessionsByProjectIdAsync(projectId);
            if (sessions == null || sessions.Count == 0)
            {
                _logger?.LogInfo($"按项目导出时无 Session: ProjectId={projectId}");
                return;
            }

            // ProjectId 在当前阶段即为 ProductName
            var safeProductName = ToSafeFileName(projectId);
            var zipFilePath = Path.Combine(outputDirectory, safeProductName + ".zip");

            if (File.Exists(zipFilePath))
            {
                throw new InvalidOperationException($"目标 ZIP 已存在，无法导出：{zipFilePath}");
            }

            var orders = await _storage.GetAllOrdersAsync();
            var orderNameById = orders.ToDictionary(o => o.Id, o => o.OrderName);

            string tempDir = null;
            try
            {
                tempDir = CreateTempDirectory();

                using (var zipStream = new FileStream(zipFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var s in sessions)
                    {
                        if (!orderNameById.TryGetValue(s.OrderId, out var orderName))
                        {
                            // 数据不一致时跳过该 Session
                            continue;
                        }

                        var safeOrderName = ToSafeFileName(orderName);
                        var safeSessionName = ToSafeFileName(s.SessionName);
                        bool sessionHadRecords = false;

                        // 1) 按 ProductCode 选择 Exporter 生成结果表格
                        try
                        {
                            var productCode = await _storage.GetProductCodeBySessionIdAsync(s.Id).ConfigureAwait(false);
                            var exporter = _exporterFactory.GetExporter(productCode);
                            var context = new ExportContext
                            {
                                SessionId = s.Id,
                                SessionName = s.SessionName,
                                OutputDirectory = tempDir,
                                Filter = filter
                            };
                            await exporter.ExportAsync(context).ConfigureAwait(false);
                            var excelPath = Path.Combine(tempDir, $"{s.Id}.xlsx");
                            if (File.Exists(excelPath))
                            {
                                sessionHadRecords = true;
                                var excelEntryName = $"{safeProductName}/{safeOrderName}/{safeSessionName}.xlsx";
                                archive.CreateEntryFromFile(excelPath, excelEntryName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"按项目导出 Session={s.SessionName} 表格失败：{ex.Message}", ex);
                        }

                        // 2) 空 Session 不导出日志：仅当有记录（生成了 Excel）时才导出日志
                        if (!sessionHadRecords || _loggingService == null)
                            continue;

                        var sessionLogPath = _loggingService.GetLogFilePath(s.SessionName);
                        if (string.IsNullOrEmpty(sessionLogPath) || !File.Exists(sessionLogPath))
                            continue;

                        var logEntryName = $"{safeProductName}/{safeOrderName}/{safeSessionName}.log";
                        archive.CreateEntryFromFile(sessionLogPath, logEntryName);
                    }
                }
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }

            _logger?.LogInfo($"按项目导出 ZIP 完成: ProjectId={projectId}, SessionCount={sessions.Count}, Zip={zipFilePath}");
        }

        /// <summary>
        /// 将业务名称转换为文件系统安全的名称：非法字符统一替换为下划线。
        /// </summary>
        private static string ToSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "_";

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalidChars.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// 创建用于 Session 级中间文件的临时目录。
        /// </summary>
        private static string CreateTempDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "SnVerify_Export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// 安全删除临时目录（忽略 IO 异常）。
        /// </summary>
        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // 删除失败不影响主流程
            }
        }
    }
}
