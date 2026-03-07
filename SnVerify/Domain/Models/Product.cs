/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 产品型号级别的维度，一个 Product 可以对应多个 Order。
    /// </summary>
    public class Product
    {
        /// <summary>
        /// 产品主键，自增 Id。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 产品名称（项目个体名），全局唯一，与 UI「项目名」一致。
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// 项目类型代码（如 KM001、SOLTAG25），与 ProductRegistry 的 key 一致；可空以兼容 Legacy。
        /// </summary>
        public string ProductCode { get; set; }

        /// <summary>
        /// 可选的产品描述。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }
}

