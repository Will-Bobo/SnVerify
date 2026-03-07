/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using SnVerify.Domain.State;
using SnVerify.Services.Mes.Gate;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 流程编排服务接口，负责协调各个 Service 完成 SN 校验流程
    /// </summary>
    public interface IProcessCoordinator
    {
        /// <summary>
        /// 当前流程状态快照（只读）
        /// </summary>
        VerificationSnapshot Snapshot { get; }

        /// <summary>
        /// 状态快照变化事件
        /// </summary>
        event EventHandler<VerificationSnapshot> SnapshotChanged;

        /// <summary>
        /// MES 事件通知（仅弱提示用途，不得影响 PASS/FAIL）。
        /// </summary>
        event EventHandler<MesEventArgs> MesEventOccurred;

        /// <summary>
        /// 启动 Legacy SN 校验流程（原子化执行，Phase 2.5 冻结逻辑）。
        /// </summary>
        /// <param name="sn">扫码输入的 SN。</param>
        /// <remarks>
        /// 流程步骤：
        /// 1. 检查是否正在处理（原子锁定）
        /// 2. 检查批次内 SN 是否重复
        /// 3. 通过 ADB 读取设备 SN
        /// 4. 校验 SN 一致性
        /// 5. 保存结果到 StorageService
        /// 6. 更新状态快照
        /// 7. 释放锁定
        /// </remarks>
        Task StartVerificationAsync(string sn);

        /// <summary>
        /// Phase 3 SN 校验流程入口（扩展版）。
        /// </summary>
        /// <param name="sn">扫码输入的 SN（StickerSN）。</param>
        /// <param name="projectId">项目 ID / 产品代码（用于参数读取与 ProductProfile 选择）。</param>
        /// <remarks>
        /// 内部按 projectId 从 ProductRegistry 取 ProductProfile，调用 RulePipelineExecutor 执行规则链，并由协调器统一负责结果落库与 Snapshot 更新。
        /// </remarks>
        Task ProcessScanAsync(string sn, string projectId);

        /// <summary>
        /// 重置流程状态，允许下一次扫描
        /// </summary>
        void Reset();
    }
}
