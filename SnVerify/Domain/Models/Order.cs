/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// Phase 2.5 Step 6：Order 模型，使用 INT 主键并关联 Product。
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 订单级模型。每个订单绑定一个 Product，OrderName 全局唯一。
    /// </summary>
    public class Order
    {
        /// <summary>
        /// 订单主键，自增 Id。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 订单名称（业务 ID），全局唯一。
        /// </summary>
        public string OrderName { get; set; }

        /// <summary>
        /// 关联的产品 Id（FK -> Product.Id）。
        /// </summary>
        public int ProductId { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }
}

