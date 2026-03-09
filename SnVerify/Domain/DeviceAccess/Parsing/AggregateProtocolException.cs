using System;

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// 聚合命令输出协议异常：命令执行成功但输出不符合约定格式。
    /// </summary>
    public class AggregateProtocolException : Exception
    {
        public AggregateProtocolException(string message)
            : base(message)
        {
        }

        public AggregateProtocolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
