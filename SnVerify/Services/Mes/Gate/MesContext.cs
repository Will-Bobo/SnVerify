/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。Pre-Gate 入参，契约见 MES_Plugin_Gate_Design_Freeze.md。
/// </remarks>

using System;

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES Pre-Gate 调用上下文。仅传递「能不能开始」所需信息，不包含业务判定结果。
    /// </summary>
    public class MesContext
    {
        /// <summary>当前会话 ID（SessionId）</summary>
        public string SessionId { get; set; }

        /// <summary>当前订单 ID（OrderId）</summary>
        public string OrderId { get; set; }

        /// <summary>扫码输入的 SN（本条待检）</summary>
        public string StickerSN { get; set; }

        /// <summary>当前时间戳（可选）</summary>
        public DateTime? At { get; set; }
    }
}
