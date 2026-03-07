/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 2：Session 生命周期服务，与 Start/End 按钮逻辑挂接。Batch 退场后由本服务替代 IBatchManager 的 Session 语义。
/// </remarks>

using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Session
{
    /// <summary>
    /// Session 生命周期服务：创建 Session、开始 Session、结束 Session、当前 Session 查询。
    /// SessionId 只通过 SessionIdGenerator.Format 生成，禁止手写或拼接。
    /// </summary>
    public interface ISessionLifecycleService
    {
        /// <summary>当前 Session 状态快照</summary>
        SessionSnapshot Snapshot { get; }

        /// <summary>
        /// 创建并开始一个 Session（等价于：创建 Order 若不存在 + 创建 TestSession + StartSession）。
        /// </summary>
        /// <param name="orderId">订单 ID</param>
        /// <param name="orderName">订单名称（可选，用于 Order 创建与 TestSession.OrderName）</param>
        /// <param name="projectId">项目 ID（可选，项目个体名，用于 Product.ProductName 与 Order 关联）</param>
        /// <param name="productCode">项目类型代码（可选，如 KM001，用于 Product.ProductCode；Phase3 传入）</param>
        /// <returns>新 Session 的 SessionId</returns>
        string CreateAndStartSession(string orderId, string orderName = null, string projectId = null, string productCode = null);

        /// <summary>
        /// 开始已存在的 Session（将指定 SessionId 设为当前活动 Session）。
        /// </summary>
        /// <param name="sessionId">会话 ID（必须已存在）</param>
        void StartSession(string sessionId);

        /// <summary>
        /// 结束当前活动 Session。
        /// </summary>
        void EndSession();

        /// <summary>
        /// 当前活动 SessionId；无活动时为 null。
        /// </summary>
        string GetCurrentSessionId();
    }
}
