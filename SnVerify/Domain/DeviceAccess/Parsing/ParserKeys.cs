/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：Parser Key 常量定义，Parsing 子域契约。</remarks>

namespace SnVerify.Domain.DeviceAccess
{
    /// <summary>
    /// Parser Key 常量定义。
    ///
    /// 设计约束：
    /// - Key 为 ParserFactory 的注册契约
    /// - DeviceInfoCommand.ParserKey 必须引用此处常量
    /// - 不允许在其它位置直接使用字符串
    /// </summary>
    public static class ParserKeys
    {
        /// <summary>
        /// 单字段 Parser Key。
        /// </summary>
        public static class Field
        {
            /// <summary>
            /// 去除字符串首尾空白。
            /// </summary>
            public const string Trim = "Trim";
        }

        /// <summary>
        /// 聚合 Parser Key。
        /// </summary>
        public static class Aggregate
        {
            /// <summary>
            /// KM001 MCU 版本聚合输出解析器。
            /// </summary>
            public const string Km001McuVersion = "Km001McuVersion";

            /// <summary>
            /// KM008 聚合输出解析（第二行：android, sn, wifiMac）。
            /// </summary>
            public const string Km008AndroidVersion = "Km008AndroidVersion";
        }
    }
}
