/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;

namespace SnVerify.Services.MES
{
    /// <summary>
    /// MES 接口服务实现，负责与 MES 系统交互（Phase2 新增）
    /// </summary>
    public class MESInterface : IMESInterface, IDisposable
    {
        private readonly string _mesBaseUrl;
        private readonly IFileLogger _logger;
        private readonly List<SnVerifyResult> _cachedResults;
        private readonly object _lockObject = new object();
        private readonly object _snapshotLock = new object();
        private MESSnapshot _snapshot;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        /// <summary>
        /// 当前 MES 接口状态快照
        /// </summary>
        public MESSnapshot Snapshot
        {
            get
            {
                lock (_snapshotLock)
                {
                    return _snapshot ?? MESSnapshot.Idle();
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
        /// 初始化 MES 接口服务
        /// </summary>
        /// <param name="mesBaseUrl">MES 系统基础 URL</param>
        /// <param name="logger">日志记录器（可选）</param>
        /// <param name="httpClient">HTTP 客户端（可选，用于测试）</param>
        public MESInterface(string mesBaseUrl, IFileLogger logger = null, HttpClient httpClient = null)
        {
            _mesBaseUrl = mesBaseUrl ?? throw new ArgumentNullException(nameof(mesBaseUrl));
            _logger = logger ?? new NullFileLogger();
            _cachedResults = new List<SnVerifyResult>();
            _snapshot = MESSnapshot.Idle();
            
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient();
                _ownsHttpClient = true;
            }
        }

        /// <summary>
        /// 异步上传校验结果到 MES 系统
        /// </summary>
        public async Task<MESResult> UploadTestResultAsync(SnVerifyResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            try
            {
                Snapshot = MESSnapshot.Processing(result.BatchId);

                // 构建上传数据（模拟 MES 接口格式）
                var postData = BuildPostData(result);
                var url = $"{_mesBaseUrl}/postTestDataStr.php";
                var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");

                // 发送 HTTP POST 请求
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    Snapshot = MESSnapshot.Success(result.BatchId);
                    _logger?.LogInfo($"MES 上传成功: BatchId={result.BatchId}, SN={result.SN}, Result={result.Result}");
                    return MESResult.Success();
                }
                else
                {
                    var errorMessage = $"MES 接口返回错误: {response.StatusCode}";
                    CacheResult(result);
                    Snapshot = MESSnapshot.Failed(errorMessage, result.BatchId, _cachedResults.Count);
                    _logger?.LogError($"MES 上传失败: {errorMessage}");
                    return MESResult.Failure(errorMessage);
                }
            }
            catch (HttpRequestException ex)
            {
                // 网络异常，缓存结果
                CacheResult(result);
                var errorMessage = $"网络异常: {ex.Message}";
                Snapshot = MESSnapshot.Failed(errorMessage, result.BatchId, _cachedResults.Count);
                _logger?.LogError($"MES 上传失败（网络异常）: {errorMessage}", ex);
                return MESResult.Failure(errorMessage);
            }
            catch (TaskCanceledException ex)
            {
                // 超时异常，缓存结果
                CacheResult(result);
                var errorMessage = "请求超时";
                Snapshot = MESSnapshot.Failed(errorMessage, result.BatchId, _cachedResults.Count);
                _logger?.LogError($"MES 上传失败（超时）: {errorMessage}", ex);
                return MESResult.Failure(errorMessage);
            }
            catch (Exception ex)
            {
                // 其他异常，缓存结果
                CacheResult(result);
                var errorMessage = $"上传异常: {ex.Message}";
                Snapshot = MESSnapshot.Failed(errorMessage, result.BatchId, _cachedResults.Count);
                _logger?.LogError($"MES 上传失败: {errorMessage}", ex);
                return MESResult.Failure(errorMessage);
            }
        }

        /// <summary>
        /// 获取缓存的结果列表
        /// </summary>
        public IReadOnlyList<SnVerifyResult> GetCachedResults()
        {
            lock (_lockObject)
            {
                return _cachedResults.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// 重试上传所有缓存的结果
        /// </summary>
        public async Task<int> RetryCachedResultsAsync()
        {
            List<SnVerifyResult> resultsToRetry;
            lock (_lockObject)
            {
                resultsToRetry = _cachedResults.ToList();
            }

            if (resultsToRetry.Count == 0)
            {
                return 0;
            }

            int successCount = 0;
            var failedResults = new List<SnVerifyResult>();

            foreach (var result in resultsToRetry)
            {
                var uploadResult = await UploadTestResultAsync(result);
                if (uploadResult.IsSuccess)
                {
                    successCount++;
                    lock (_lockObject)
                    {
                        _cachedResults.Remove(result);
                    }
                }
                else
                {
                    failedResults.Add(result);
                }
            }

            // 更新 Snapshot
            lock (_lockObject)
            {
                if (_cachedResults.Count > 0)
                {
                    Snapshot = MESSnapshot.Cached(
                        resultsToRetry.FirstOrDefault()?.BatchId,
                        _cachedResults.Count);
                }
                else
                {
                    Snapshot = MESSnapshot.Success(resultsToRetry.FirstOrDefault()?.BatchId);
                }
            }

            return successCount;
        }

        /// <summary>
        /// 缓存结果
        /// </summary>
        private void CacheResult(SnVerifyResult result)
        {
            lock (_lockObject)
            {
                // 避免重复缓存
                if (!_cachedResults.Any(r => r.Id == result.Id && r.Id != 0))
                {
                    _cachedResults.Add(result);
                }
            }
        }

        /// <summary>
        /// 构建 POST 数据（模拟 MES 接口格式）
        /// </summary>
        private string BuildPostData(SnVerifyResult result)
        {
            // 模拟 MES 接口的数据格式
            // 实际格式需要根据 MES 系统文档调整
            var data = new StringBuilder();
            data.Append($"batch_id={Uri.EscapeDataString(result.BatchId ?? "")}");
            data.Append($"&sn={Uri.EscapeDataString(result.SN ?? "")}");
            data.Append($"&result={Uri.EscapeDataString(result.Result ?? "")}");
            if (!string.IsNullOrEmpty(result.FailReason))
            {
                data.Append($"&fail_reason={Uri.EscapeDataString(result.FailReason)}");
            }
            data.Append($"&verify_time={Uri.EscapeDataString(result.VerifyTime.ToString("yyyy-MM-dd HH:mm:ss"))}");
            return data.ToString();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient?.Dispose();
            }
        }
    }
}
