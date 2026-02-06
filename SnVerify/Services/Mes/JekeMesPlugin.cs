/// <author>AI Assistant</author>
/// <remarks>
/// Phase 3：杰科 MES 协议插件骨架。挂载到 SN 流程的 Pre-Gate / Post-Report。
/// - Phase 2.5 冻结约束：MES 不参与 Verify 逻辑、不改写 TestRecord、不直接访问 View/ViewModel。
/// - 杰科真实协议接入为 TODO，当前为 Stub 实现。
/// </remarks>

using System.Threading.Tasks;

namespace SnVerify.Services.Mes
{
    /// <summary>
    /// 杰科 MES 插件骨架。实现 Pre-Gate（Stub 返回 Allow）与 Post-Report（NoOp）。
    /// </summary>
    public sealed class JekeMesPlugin : Gate.IMesPlugin
    {
        private static readonly Gate.MesCapabilities DefaultCapabilities = new Gate.MesCapabilities
        {
            SupportsPreCheck = true,
            RequiresPreCheck = false,
            SupportsResultReport = true,
        };

        /// <inheritdoc />
        public Gate.MesCapabilities Capabilities => DefaultCapabilities;

        /// <inheritdoc />
        public Task<Gate.MesPreCheckResult> CheckAsync(Gate.MesContext context)
        {
            // TODO: Phase 3 接入杰科 MES 前置校验接口（如 getDutTestFlowResult / getDutStationInfo）
            return Task.FromResult(new Gate.MesPreCheckResult
            {
                Decision = Gate.MesPreCheckDecision.Allow,
                Reason = "Stub / 骨架实现",
            });
        }

        /// <inheritdoc />
        public Task ReportTestResultAsync(Gate.TestResultContext context)
        {
            // TODO: Phase 3 接入杰科 MES 上报接口（如 postTestDataStr）
            return Task.CompletedTask;
        }
    }
}
