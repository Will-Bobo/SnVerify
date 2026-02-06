/// <author>AI Assistant</author>
/// <remarks>
/// Phase 3：MES 插件标记接口，用于挂载到 ProcessCoordinator。
/// 契约见 MES_Plugin_Gate_Design_Freeze.md §6；插件需同时实现 IMesPreCheck / IMesResultReporter 并按需暴露 Capabilities。
/// </remarks>

namespace SnVerify.Services.Mes.Gate
{
    /// <summary>
    /// MES 插件统一入口。实现类需提供能力声明，并实现 Pre-Gate / Post-Report 之一或全部。
    /// </summary>
    public interface IMesPlugin : IMesPreCheck, IMesResultReporter
    {
        /// <summary>
        /// 能力声明，用于启动期校验（RequiresPreCheck 等）。
        /// </summary>
        MesCapabilities Capabilities { get; }
    }
}
