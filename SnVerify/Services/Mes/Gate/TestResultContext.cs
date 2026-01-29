/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。Post-Report 入参，契约见 MES_Plugin_Gate_Design_Freeze.md §5。
/// </remarks>

using System;

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES Post-Report 调用上下文。本站结果已落库后，仅用于上报，不得被 MES 修改。
    /// </summary>
    public class TestResultContext
    {
        /// <summary>会话 ID（SessionId）</summary>
        public string SessionId { get; set; }

        /// <summary>订单 ID（OrderId）</summary>
        public string OrderId { get; set; }

        /// <summary>贴纸 SN（StickerSN）</summary>
        public string StickerSN { get; set; }

        /// <summary>设备 SN（DeviceSN）</summary>
        public string DeviceSN { get; set; }

        /// <summary>本站结果：PASS / FAIL / TIMEOUT</summary>
        public string Result { get; set; }

        /// <summary>失败原因（若 FAIL/TIMEOUT）</summary>
        public string FailReason { get; set; }

        /// <summary>校验完成时间</summary>
        public DateTime VerifyTime { get; set; }
    }
}
