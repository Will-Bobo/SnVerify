/// <author>AI Assistant</author>
/// <remarks>
/// Phase3：产品/项目 Profile 工厂接口。
/// 用于根据 ProductId / ProjectId 生成唯一的规则配置对象（ProjectProfile），
/// 作为 ADB 读取与后续规则服务的唯一事实来源入口。
/// </remarks>

using SnVerify.Domain.Models;

namespace SnVerify.Services.ProductProfiles
{
    /// <summary>
    /// Product / Project Profile 工厂。
    /// </summary>
    public interface IProductProfileFactory
    {
        /// <summary>
        /// 根据产品（项目）标识创建 Profile。
        /// </summary>
        /// <param name="productId">产品或项目标识（与 VerificationParameter.ProjectId 对齐）。</param>
        /// <returns>对应的 ProjectProfile 规则对象。</returns>
        ProjectProfile Create(string productId);
    }
}

