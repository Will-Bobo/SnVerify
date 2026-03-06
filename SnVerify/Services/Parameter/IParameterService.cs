/// <author>
/// AI Assistant
/// </author>

using System.Threading.Tasks;
using SnVerify.Domain.Models;

namespace SnVerify.Services.Parameter
{
    /// <summary>
    /// 版本参数服务接口，负责从存储中读取/保存项目级版本参数，并提供内存缓存。
    /// </summary>
    public interface IParameterService
    {
        /// <summary>
        /// 获取指定 ProjectId 的版本校验参数；不存在时返回 null。
        /// 默认使用惰性加载 + 内存缓存策略，避免每次流程都访问数据库。
        /// </summary>
        /// <param name="projectId">项目 ID</param>
        Task<VerificationParameter> GetParameterAsync(string projectId);

        /// <summary>
        /// 保存或更新指定 ProjectId 的版本参数，并刷新内存缓存。
        /// </summary>
        /// <param name="parameter">版本参数实体</param>
        Task SaveParameterAsync(VerificationParameter parameter);
    }
}

