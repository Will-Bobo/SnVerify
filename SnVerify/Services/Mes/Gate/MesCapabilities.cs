/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：MES 预留。契约见 MES_Plugin_Gate_Design_Freeze.md §6.1。
/// </remarks>

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 能力声明。用于启动期校验，防止半接入进入生产。
    /// </summary>
    public class MesCapabilities
    {
        /// <summary>是否支持 PreCheck</summary>
        public bool SupportsPreCheck { get; set; }

        /// <summary>是否要求必须支持 PreCheck（若 true 且 SupportsPreCheck 为 false 则应阻止启动）</summary>
        public bool RequiresPreCheck { get; set; }

        /// <summary>是否支持结果上报（Post-Report）</summary>
        public bool SupportsResultReport { get; set; }
    }
}
