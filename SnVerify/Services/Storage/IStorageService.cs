/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System.Collections.Generic;
using System.Threading.Tasks;
using SnVerify.Domain.Export;
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
        /// 检查给定 SN 是否在历史 PASS 绑定中（跨批次查询）。PASS 时 StickerSN = DeviceSN，故仅需传入一个 SN；上层在两者相等时才调用。
        /// </summary>
        /// <param name="sn">贴纸/设备 SN（StickerSN 与 DeviceSN 相等时的统一值）</param>
        /// <returns>是否存在该 SN 的 PASS 记录</returns>
        Task<bool> IsBindingInPassHistoryAsync(string sn);

        /// <summary>
        /// 检查给定贴纸 SN 是否在指定订单内已产生 PASS 记录（Order 维度唯一性检查）。
        /// </summary>
        /// <param name="orderId">订单业务标识（等同于 OrderName）</param>
        /// <param name="sn">贴纸 SN（StickerSN）</param>
        /// <returns>若该订单内存在 Result='PASS' 的记录则返回 true，否则 false。</returns>
        Task<bool> IsStickerSnPassedInOrderAsync(string orderId, string sn);

        /// <summary>
        /// 检查给定 ChipId 是否在指定订单内已产生 PASS 记录（Order 维度唯一性检查）。
        /// </summary>
        /// <param name="orderId">订单业务标识（等同于 OrderName）</param>
        /// <param name="chipId">芯片 ID（ChipId）</param>
        /// <returns>若该订单内存在 Result='PASS' 的记录则返回 true，否则 false。</returns>
        Task<bool> IsChipIdPassedInOrderAsync(string orderId, string chipId);

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
        /// 按产品名称获取产品 Id，不存在则返回 null。
        /// </summary>
        Task<int?> GetProductIdByProductNameAsync(string productName);

        /// <summary>
        /// 根据当前 Session 名称解析所属项目个体名（Session → Order → Product → ProductName）。
        /// </summary>
        Task<string> GetProductNameBySessionNameAsync(string sessionName);

        /// <summary>
        /// 根据 Session 内部 Id 解析所属 Product 的 ProductCode（Session → Order → Product → ProductCode）；无匹配或为空时返回 null。
        /// </summary>
        Task<string> GetProductCodeBySessionIdAsync(int sessionId);

        /// <summary>
        /// 创建订单记录，返回自增 Id。
        /// </summary>
        /// <param name="order">订单实体</param>
        Task<int> CreateOrderAsync(Order order);

        /// <summary>
        /// 按订单名称更新订单的 ProductId（用于修正历史订单的 ProductId 为 0 的情况）。
        /// </summary>
        Task SetOrderProductIdAsync(string orderName, int productId);

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
        /// 按订单名称与项目（ProductId）联合判断该项目下是否已存在该订单（OrderName + ProductId 唯一）。
        /// </summary>
        Task<bool> OrderExistsByOrderNameAndProductAsync(string orderName, int productId);

        /// <summary>
        /// 获取所有订单列表。
        /// </summary>
        Task<IReadOnlyList<Order>> GetAllOrdersAsync();

        /// <summary>
        /// 获取所有 ProjectId 列表（用于“按项目导出”等 UI 选择）。
        /// </summary>
        Task<IReadOnlyList<string>> GetAllProjectIdsAsync();

        /// <summary>
        /// 判断项目名（Product.ProductName）是否已存在；比较忽略大小写。Phase3 UI Guard 用。
        /// </summary>
        /// <param name="projectName">项目名（与 ProductName 语义一致）</param>
        /// <returns>存在为 true，不存在或空参数为 false</returns>
        Task<bool> ProjectNameExistsAsync(string projectName);

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
        /// 根据业务 SessionName 获取完整 TestSession；若不存在则返回 null。
        /// </summary>
        /// <param name="sessionName">业务会话名（通常为 OrderName_yyyyMMdd_HHmmss）</param>
        Task<TestSession> GetSessionBySessionNameAsync(string sessionName);

        /// <summary>
        /// 按 SessionId + StickerSN 获取最近一条测试记录；若不存在则返回 null。
        /// </summary>
        Task<TestRecord> GetTestRecordBySessionAndStickerSnAsync(int sessionId, string stickerSN);

        /// <summary>
        /// 更新已有测试记录（必须包含有效 Id）。
        /// </summary>
        Task UpdateTestRecordAsync(TestRecord record);

        /// <summary>
        /// 获取指定 SessionId 下配置的版本校验参数；不存在时返回 null。
        /// </summary>
        /// <param name="sessionId">会话内部 Id（TestSession.Id）</param>
        Task<VerificationParameter> GetVerificationParameterAsync(int sessionId);

        /// <summary>
        /// 保存或更新指定 SessionId 的版本校验参数。
        /// </summary>
        /// <param name="parameter">版本参数实体，SessionId 为业务唯一键。</param>
        Task SaveVerificationParameterAsync(VerificationParameter parameter);

        /// <summary>
        /// 按 Session 导出：单 Session → xlsx 双 Sheet（PASS 原样、FAIL 按 (StickerSN, DeviceSN) 去重保留第一条）+ txt。
        /// </summary>
        Task ExportBySessionAsync(int sessionId, string outputDirectory);

        /// <summary>
        /// 按 Session 导出（带过滤）：根据 ExportRecordFilter 过滤 TestRecord 后导出。
        /// </summary>
        /// <param name="sessionId">会话内部 Id</param>
        /// <param name="outputDirectory">输出目录</param>
        /// <param name="filter">记录过滤（SnOnly/VersionOnly/All），null 等价于 All</param>
        Task ExportBySessionAsync(int sessionId, string outputDirectory, ExportRecordFilter filter);

        void Dispose();
    }
}
