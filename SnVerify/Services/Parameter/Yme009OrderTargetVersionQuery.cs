using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Services.Storage;

namespace SnVerify.Services.Parameter
{
    /// <summary>
    /// 读取同一项目名、同一订单号下已经保存的目标软件版本快照。
    /// 只服务 YME009 开始测试校验，不改变其他产品的参数读写。
    /// </summary>
    public static class Yme009OrderTargetVersionQuery
    {
        public static async Task<IReadOnlyList<string>> LoadExpectedAndroidVersionsAsync(
            IStorageService storage,
            string projectName,
            string orderName)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));

            var matchedProduct = FindProduct(await storage.GetAllProductsAsync().ConfigureAwait(false), projectName);
            if (matchedProduct == null)
                return Array.Empty<string>();

            var matchedOrder = FindOrder(await storage.GetAllOrdersAsync().ConfigureAwait(false), matchedProduct.Id, orderName);
            if (matchedOrder == null)
                return Array.Empty<string>();

            var sessions = await storage.GetSessionsByOrderIdAsync(matchedOrder.Id).ConfigureAwait(false);
            return await CollectExpectedAndroidVersionsAsync(storage, sessions).ConfigureAwait(false);
        }

        private static Product FindProduct(IReadOnlyList<Product> products, string projectName)
        {
            if (products == null)
                return null;

            foreach (var product in products)
            {
                if (product != null &&
                    string.Equals(product.ProductName, projectName, StringComparison.OrdinalIgnoreCase))
                {
                    return product;
                }
            }

            return null;
        }

        private static Order FindOrder(IReadOnlyList<Order> orders, int productId, string orderName)
        {
            if (orders == null)
                return null;

            foreach (var order in orders)
            {
                if (order != null &&
                    order.ProductId == productId &&
                    string.Equals(order.OrderName, orderName, StringComparison.Ordinal))
                {
                    return order;
                }
            }

            return null;
        }

        private static async Task<IReadOnlyList<string>> CollectExpectedAndroidVersionsAsync(
            IStorageService storage,
            IReadOnlyList<TestSession> sessions)
        {
            var versions = new List<string>();
            if (sessions == null)
                return versions;

            foreach (var session in sessions)
            {
                if (session == null)
                    continue;

                var parameter = await storage.GetVerificationParameterAsync(session.Id).ConfigureAwait(false);
                if (parameter != null && !string.IsNullOrWhiteSpace(parameter.ExpectedAndroidVersion))
                    versions.Add(parameter.ExpectedAndroidVersion);
            }

            return versions;
        }
    }
}
