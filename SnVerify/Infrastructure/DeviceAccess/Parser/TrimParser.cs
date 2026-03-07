/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：基线单字段解析器，Trim 输出。</remarks>

using SnVerify.Domain.DeviceAccess;

namespace SnVerify.Infrastructure.DeviceAccess.Parser
{
    /// <summary>
    /// 单字段解析器：对输出做 Trim。注册 Key：ParserKeys.Field.Trim。
    /// </summary>
    public class TrimParser : IDeviceInfoParser
    {
        /// <inheritdoc />
        public string Parse(string output)
        {
            return output?.Trim() ?? string.Empty;
        }
    }
}
