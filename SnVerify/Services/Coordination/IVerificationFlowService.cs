/// <author>
/// AI Assistant
/// </author>

using System.Threading.Tasks;
using SnVerify.Domain.State;
using SnVerify.Services.Mes.Gate;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 校验流程服务接口，对外提供统一接口给 UI / ViewModel
    /// </summary>
    public interface IVerificationFlowService
    {
        /// <summary>
        /// 当前流程状态快照（只读）
        /// </summary>
        VerificationSnapshot Snapshot { get; }

        /// <summary>
        /// MES 事件通知（仅弱提示用途，不得影响 PASS/FAIL）。
        /// </summary>
        event System.EventHandler<MesEventArgs> MesEventOccurred;

        /// <summary>
        /// 启动校验流程
        /// </summary>
        /// <param name="sn">扫码输入的 SN</param>
        /// <remarks>
        /// 内部委托给 ProcessCoordinator 执行原子化流程
        /// </remarks>
        Task StartVerificationAsync(string sn);

        /// <summary>
        /// 重置流程状态，允许下一次扫描
        /// </summary>
        /// <remarks>
        /// 内部委托给 ProcessCoordinator.Reset()
        /// </remarks>
        void Reset();
    }
}
