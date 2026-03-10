/// <author>AI Assistant</author>
/// <remarks>导出配置注册表接口，按产品代码返回 ProductExportProfile。</remarks>

using SnVerify.Domain.Export;

namespace SnVerify.Infrastructure.Export
{
    /// <summary>
    /// 按产品代码获取导出列配置；未注册产品返回 null。
    /// </summary>
    public interface IProductExportRegistry
    {
        /// <summary>
        /// 获取指定产品代码的导出配置；不存在则返回 null。
        /// </summary>
        ProductExportProfile GetProfile(string productCode);
    }
}
