/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：Parser 工厂接口。Domain 层，实现位于 Infrastructure。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// Parser 工厂：按 Key 提供 Parser 实例。Parser 由 DI 注册，配置中仅存 ParserKey。
    /// </summary>
    public interface IParserFactory
    {
        /// <summary>获取单字段解析器。</summary>
        IDeviceInfoParser Get(string key);

        /// <summary>获取聚合解析器。</summary>
        IAggregateDeviceInfoParser GetAggregate(string key);
    }
}
