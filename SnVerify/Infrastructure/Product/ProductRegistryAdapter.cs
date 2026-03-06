/// <author>AI Assistant</author>
/// <remarks>
/// Stage3 Step2：ProductRegistry 适配器，用于通过依赖注入把静态 ProductRegistry 引入 ViewModel。
/// 该适配器不存储/修改规则，仅转发读取操作，保证 ProductRegistry 仍是唯一事实来源。
/// </remarks>

using System.Collections.Generic;
using SnVerify.Domain.Product;

namespace SnVerify.Infrastructure.Product
{
    /// <summary>
    /// ProductRegistry 的只读适配器实现。
    /// </summary>
    public class ProductRegistryAdapter : IProductRegistry
    {
        /// <inheritdoc />
        public ProductProfile Get(string productCode) => ProductRegistry.Get(productCode);

        /// <inheritdoc />
        public ProductProfile GetProductProfile(string productCode) => ProductRegistry.GetProductProfile(productCode);

        /// <inheritdoc />
        public IReadOnlyList<string> GetProductCodes() => ProductRegistry.GetProductCodes();
    }
}

