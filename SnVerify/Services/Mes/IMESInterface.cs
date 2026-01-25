/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.MES
{
    /// <summary>
    /// MES 接口调用结果
    /// </summary>
    public class MESResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 错误消息（如果失败）
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static MESResult Success()
        {
            return new MESResult(true, null);
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static MESResult Failure(string errorMessage)
        {
            return new MESResult(false, errorMessage);
        }

        /// <summary>
        /// 私有构造函数
        /// </summary>
        private MESResult(bool isSuccess, string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// MES 接口服务接口，负责与 MES 系统交互（Phase2 新增）
    /// </summary>
    public interface IMESInterface : IDisposable
    {
        /// <summary>
        /// 当前 MES 接口状态快照
        /// </summary>
        MESSnapshot Snapshot { get; }

        /// <summary>
        /// 异步上传校验结果到 MES 系统
        /// </summary>
        /// <param name="result">校验结果</param>
        /// <returns>上传结果</returns>
        /// <remarks>
        /// 如果上传失败，结果会被缓存，等待后续重试或人工干预
        /// </remarks>
        Task<MESResult> UploadTestResultAsync(SnVerifyResult result);

        /// <summary>
        /// 获取缓存的结果列表
        /// </summary>
        /// <returns>缓存的结果列表（只读）</returns>
        IReadOnlyList<SnVerifyResult> GetCachedResults();

        /// <summary>
        /// 重试上传所有缓存的结果
        /// </summary>
        /// <returns>成功上传的数量</returns>
        Task<int> RetryCachedResultsAsync();
    }
}
