/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：导出聚合由 B 做。覆盖确认逻辑可由调用方（C）或本服务参数控制，本阶段仅做聚合与逐 Session 导出。
/// </remarks>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SnVerify.Services.Logging;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 导出聚合服务实现：按 OrderId/ProjectId 查 Session 列表，逐 Session 调用 IStorageService.ExportBySessionAsync。
    /// </summary>
    public class ExportAggregationService : IExportAggregationService
    {
        private readonly IStorageService _storage;
        private readonly IFileLogger _logger;

        /// <summary>
        /// 初始化导出聚合服务
        /// </summary>
        /// <param name="storage">存储服务（需提供 GetSessionsByOrderIdAsync、GetSessionsByProjectIdAsync、ExportBySessionAsync）</param>
        /// <param name="logger">日志（可选）</param>
        public ExportAggregationService(IStorageService storage, IFileLogger logger = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? new NullFileLogger();
        }

        /// <inheritdoc />
        public async Task ExportByOrderIdAsync(string orderId, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("OrderId 不能为空", nameof(orderId));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            var sessions = await _storage.GetSessionsByOrderIdAsync(orderId);
            foreach (var s in sessions)
            {
                // 使用内部 INT Id 作为导出 SessionId
                await _storage.ExportBySessionAsync(s.Id, outputDirectory);
            }
            _logger?.LogInfo($"按订单导出完成: OrderId={orderId}, SessionCount={sessions.Count}");
        }

        /// <inheritdoc />
        public async Task ExportByProjectIdAsync(string projectId, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空", nameof(outputDirectory));

            var sessions = await _storage.GetSessionsByProjectIdAsync(projectId);
            foreach (var s in sessions)
            {
                await _storage.ExportBySessionAsync(s.Id, outputDirectory);
            }
            _logger?.LogInfo($"按项目导出完成: ProjectId={projectId}, SessionCount={sessions.Count}");
        }
    }
}
