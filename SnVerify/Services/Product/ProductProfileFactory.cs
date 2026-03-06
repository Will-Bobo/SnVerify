/// <author>AI Assistant</author>
/// <remarks>
/// Phase3：默认 ProductProfileFactory 实现。
/// 当前阶段采用最小硬编码策略，仅按 ProductId / ProjectId 构造 ProjectProfile，
/// 后续可扩展为从 JSON / 数据库加载详细规则。
/// </remarks>

using System;
using SnVerify.Domain.Models;

namespace SnVerify.Services.ProductProfiles
{
    /// <summary>
    /// 默认的 Product / Project Profile 工厂实现。
    /// </summary>
    public class ProductProfileFactory : IProductProfileFactory
    {
        /// <inheritdoc />
        public ProjectProfile Create(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("productId 不能为空", nameof(productId));
            }

            // Phase3：先以最小实现落地，仅保证 Profile 作为规则唯一入口存在。
            // 后续可按以下优先级扩展：
            // 1. JSON / DB 中按 productId 读取 Profile；
            // 2. 若未配置，则回退到代码内置默认 Profile。
            return new ProjectProfile
            {
                ProjectId = productId,
                // Phase3 允许不配置聚合命令，AdbAccessService 将自动回退到分字段读取。
                AggregateDeviceInfoCommand = null
            };
        }
    }
}

