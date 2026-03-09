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
        private readonly ConcurrentDictionary<int, VerificationParameter> _cache =
            new ConcurrentDictionary<int, VerificationParameter>();

        /// <summary>
        /// 初始化版本参数服务。
        /// </summary>
        /// <param name="storageService">统一存储服务</param>
        public ParameterService(IStorageService storageService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        }

        /// <inheritdoc />
        public async Task<VerificationParameter> GetParameterAsync(int sessionId)
        {
            if (sessionId <= 0)
            {
                throw new ArgumentException("SessionId 必须大于 0", nameof(sessionId));
            }

            // 先查内存缓存
            if (_cache.TryGetValue(sessionId, out var cached))
            {
                return cached;
            }

            // 惰性从存储加载
            var parameter = await _storageService.GetVerificationParameterAsync(sessionId).ConfigureAwait(false);
            if (parameter != null)
            {
                _cache[sessionId] = parameter;
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

            if (parameter.SessionId <= 0)
            {
                throw new ArgumentException("SessionId 必须大于 0", nameof(parameter));
            }

            await _storageService.SaveVerificationParameterAsync(parameter).ConfigureAwait(false);

            // 更新缓存
            _cache[parameter.SessionId] = parameter;
        }
    }
}

