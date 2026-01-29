/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System.Collections.Generic;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Storage
{
    /// <summary>
    /// 存储服务接口，负责 SQLite 数据持久化和 Excel 导出（Phase2 扩展）
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// 当前存储服务状态快照
        /// </summary>
        StorageSnapshot Snapshot { get; }

        /// <summary>
        /// 初始化 SQLite 数据库和表结构
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 检查指定批次内 SN 是否重复（仅在该批次内判断，不跨批次）。
        /// </summary>
        /// <param name="batchId">批次 ID</param>
        /// <param name="sn">待检查的 SN</param>
        /// <returns>若在同一批次中已存在相同 SN，则返回 true；否则返回 false。</returns>
        Task<bool> IsSnDuplicateAsync(string batchId, string sn);

        /// <summary>
        /// 检查 StickerSN 是否存在于历史 PASS 绑定中（跨批次查询）
        /// </summary>
        /// <param name="stickerSN">贴纸 SN（扫码枪输入的 SN）</param>
        /// <returns>是否存在</returns>
        Task<bool> IsStickerSnInPassHistoryAsync(string stickerSN);

        /// <summary>
        /// 检查 DeviceSN 是否存在于历史 PASS 绑定中（跨批次查询）
        /// </summary>
        /// <param name="deviceSN">设备 SN（从设备内部读取的 SN）</param>
        /// <returns>是否存在</returns>
        Task<bool> IsDeviceSnInPassHistoryAsync(string deviceSN);

        /// <summary>
        /// 检查绑定关系（StickerSN <-> DeviceSN）是否存在于历史 PASS 绑定中（跨批次查询）
        /// </summary>
        /// <param name="stickerSN">贴纸 SN</param>
        /// <param name="deviceSN">设备 SN</param>
        /// <returns>绑定关系是否存在</returns>
        Task<bool> IsBindingInPassHistoryAsync(string stickerSN, string deviceSN);

        // ---------- Phase 2.5 Step 6：Product / Order / TestSession / TestRecord ----------

        /// <summary>
        /// 创建产品记录，返回自增 Id。
        /// </summary>
        /// <param name="product">产品实体</param>
        Task<int> CreateProductAsync(Product product);

        /// <summary>
        /// 获取所有产品列表。
        /// </summary>
        Task<IReadOnlyList<Product>> GetAllProductsAsync();

        /// <summary>
        /// 创建订单记录，返回自增 Id。
        /// </summary>
        /// <param name="order">订单实体</param>
        Task<int> CreateOrderAsync(Order order);

        /// <summary>
        /// 判断给定订单是否已存在（兼容旧接口名，语义等同于按订单名称检查）。
        /// </summary>
        /// <param name="orderId">订单业务标识（等同于 OrderName）</param>
        Task<bool> OrderExistsAsync(string orderId);

        /// <summary>
        /// 判断给定订单名称是否已存在（全局唯一）。
        /// </summary>
        Task<bool> OrderNameExistsAsync(string orderName);

        /// <summary>
        /// 获取所有订单列表。
        /// </summary>
        Task<IReadOnlyList<Order>> GetAllOrdersAsync();

        /// <summary>
        /// 获取所有 ProjectId 列表（用于“按项目导出”等 UI 选择）。
        /// </summary>
        Task<IReadOnlyList<string>> GetAllProjectIdsAsync();

        /// <summary>
        /// 创建测试会话记录，返回自增 Id。
        /// </summary>
        /// <param name="session">会话实体</param>
        Task<int> CreateSessionAsync(TestSession session);

        /// <summary>
        /// 按订单 Id 获取该订单下所有会话。
        /// </summary>
        Task<IReadOnlyList<TestSession>> GetSessionsByOrderIdAsync(int orderId);

        /// <summary>
        /// 按业务 OrderId（字符串）获取该订单下所有会话。
        /// </summary>
        Task<IReadOnlyList<TestSession>> GetSessionsByOrderIdAsync(string orderId);

        /// <summary>
        /// 按 ProjectId 获取该项目下所有会话。
        /// </summary>
        Task<IReadOnlyList<TestSession>> GetSessionsByProjectIdAsync(string projectId);

        /// <summary>
        /// 判断业务会话名是否已存在。
        /// </summary>
        Task<bool> SessionNameExistsAsync(string sessionName);

        /// <summary>
        /// 判断给定 SessionId 是否已存在。
        /// </summary>
        /// <param name="sessionId">会话业务标识（SessionId）</param>
        Task<bool> SessionExistsAsync(string sessionId);

        /// <summary>
        /// 保存一条测试记录。
        /// </summary>
        Task SaveTestRecordAsync(TestRecord record);

        /// <summary>
        /// 按内部 SessionId（INT 主键）获取所有测试记录。
        /// </summary>
        Task<IReadOnlyList<TestRecord>> GetTestRecordsBySessionAsync(int sessionId);

        /// <summary>
        /// 按业务 SessionId（字符串，如 OrderId_yyyyMMdd_HHmmss）获取所有测试记录。
        /// </summary>
        Task<IReadOnlyList<TestRecord>> GetTestRecordsBySessionAsync(string sessionId);

        /// <summary>
        /// 根据业务 SessionName 查找内部自增 Session Id（TestSession.Id）；若不存在则返回 null。
        /// </summary>
        /// <param name="sessionName">业务会话名（通常为 OrderName_yyyyMMdd_HHmmss）</param>
        Task<int?> GetInternalSessionIdBySessionNameAsync(string sessionName);

        /// <summary>
        /// 按 SessionId + StickerSN 获取最近一条测试记录；若不存在则返回 null。
        /// </summary>
        Task<TestRecord> GetTestRecordBySessionAndStickerSnAsync(int sessionId, string stickerSN);

        /// <summary>
        /// 更新已有测试记录（必须包含有效 Id）。
        /// </summary>
        Task UpdateTestRecordAsync(TestRecord record);

        /// <summary>
        /// 按 Session 导出：单 Session → xlsx 双 Sheet（PASS 原样、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条）+ txt。
        /// </summary>
        Task ExportBySessionAsync(int sessionId, string outputDirectory);

        void Dispose();
    }
}
