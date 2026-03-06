/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Parameter
{
    /// <summary>
    /// 版本参数服务实现，基于 StorageService 提供持久化，并采用惰性加载 + 内存缓存策略。
    /// </summary>
    public class ParameterService : IParameterService
    {
        private readonly IStorageService _storageService;
        private readonly ConcurrentDictionary<string, VerificationParameter> _cache =
            new ConcurrentDictionary<string, VerificationParameter>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 初始化版本参数服务。
        /// </summary>
        /// <param name="storageService">统一存储服务</param>
        public ParameterService(IStorageService storageService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        }

        /// <inheritdoc />
        public async Task<VerificationParameter> GetParameterAsync(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            }

            // 先查内存缓存
            if (_cache.TryGetValue(projectId, out var cached))
            {
                return cached;
            }

            // 惰性从存储加载
            var parameter = await _storageService.GetVerificationParameterAsync(projectId).ConfigureAwait(false);
            if (parameter != null)
            {
                _cache[projectId] = parameter;
            }

            return parameter;
        }

        /// <inheritdoc />
        public async Task SaveParameterAsync(VerificationParameter parameter)
        {
            if (parameter == null)
            {
                throw new ArgumentNullException(nameof(parameter));
            }

            if (string.IsNullOrWhiteSpace(parameter.ProjectId))
            {
                throw new ArgumentException("ProjectId 不能为空", nameof(parameter));
            }

            await _storageService.SaveVerificationParameterAsync(parameter).ConfigureAwait(false);

            // 更新缓存
            _cache[parameter.ProjectId] = parameter;
        }
    }
}

