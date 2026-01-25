/// <author>
/// AI Assistant
/// </author>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// SN 捕获事件参数（不可变对象）
    /// </summary>
    public class SnCapturedEventArgs : EventArgs
    {
        /// <summary>
        /// 捕获到的 SN（已处理：转大写、去首尾空格）
        /// </summary>
        public string Sn { get; }

        /// <summary>
        /// 创建 SN 捕获事件参数
        /// </summary>
        /// <param name="sn">捕获到的 SN</param>
        public SnCapturedEventArgs(string sn)
        {
            Sn = sn ?? throw new ArgumentNullException(nameof(sn));
        }
    }
}
