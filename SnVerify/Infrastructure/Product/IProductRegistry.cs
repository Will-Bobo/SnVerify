/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step2：为 UI/ViewModel 提供可注入的 ProductRegistry 抽象，以支持单元测试 Mock。
/// 注意：真实规则唯一事实来源仍为 ProductRegistry（静态注册表）；本接口仅用于读取。
/// </remarks>

using System.Collections.Generic;
using SnVerify.Domain.Product;

namespace SnVerify.Infrastructure.Product
{
    /// <summary>
    /// 产品 Profile 注册表读取接口（只读）。
    /// </summary>
    public interface IProductRegistry
    {
        /// <summary>
        /// 获取指定产品代码的 Profile；不存在返回 null。
        /// </summary>
        ProductProfile Get(string productCode);

        /// <summary>
        /// 获取指定产品代码的 ProductProfile（显式入口名，语义同 Get）。
        /// </summary>
        ProductProfile GetProductProfile(string productCode);

        /// <summary>
        /// 获取所有已注册产品代码。
        /// </summary>
        IReadOnlyList<string> GetProductCodes();
    }
}

