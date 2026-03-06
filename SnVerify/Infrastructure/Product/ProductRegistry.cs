/// <author>AI Assistant</author>
/// <remarks>
/// Stage3：产品级 Profile 注册表。
/// 作为 Product → Profile 的唯一事实来源，当前采用硬编码方式，
/// 后续可扩展为从 JSON / 数据库加载。
/// </remarks>

using System;
using System.Collections.Generic;
using System.Linq;
using SnVerify.Domain.Product;

namespace SnVerify.Infrastructure.Product
{
    /// <summary>
    /// 产品 Profile 注册表。
    /// </summary>
    public static class ProductRegistry
    {
        private static readonly IReadOnlyDictionary<string, ProductProfile> Profiles;

        static ProductRegistry()
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var dict = new Dictionary<string, ProductProfile>(comparer)
            {
                {
                    "SOLTAG25",
                    new ProductProfile
                    {
                        ProductCode = "SOLTAG25",
                        ProductName = "SOLTAG25",
                        Mode = VerificationMode.Legacy,
                        AdbCommands = new DeviceInfoCommandSet
                        {
                            ReadDeviceSn = "getprop sys.skyroam.osi.sn",
                            ReadAndroidVersion = "getprop ro.build.display.id"
                        },
                        EnableChipIdCheck = false,
                        EnableWifiMacCheck = false,
                        EnableBoardVersionCheck = false,
                        EnableChargeBoardVersionCheck = false
                    }
                },
                {
                    "KM001",
                    new ProductProfile
                    {
                        ProductCode = "KM001",
                        ProductName = "KM001",
                        Mode = VerificationMode.Phase3,
                        AdbCommands = new DeviceInfoCommandSet(),
                        EnableChipIdCheck = true,
                        EnableWifiMacCheck = true,
                        EnableBoardVersionCheck = true,
                        EnableChargeBoardVersionCheck = true
                    }
                }
            };

            Profiles = dict;
        }

        /// <summary>
        /// 获取指定产品代码的 Profile；若不存在则返回 null。
        /// </summary>
        /// <param name="productCode">产品代码。</param>
        public static ProductProfile Get(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                throw new ArgumentException("productCode 不能为空", nameof(productCode));
            }

            Profiles.TryGetValue(productCode, out var profile);
            return profile;
        }

        /// <summary>
        /// 获取指定产品代码的 ProductProfile（语义同 Get；用于 Stage3 Step3 终极定位的显式入口名）。
        /// </summary>
        public static ProductProfile GetProductProfile(string productCode) => Get(productCode);

        /// <summary>
        /// 获取所有已注册的产品代码列表。
        /// </summary>
        public static IReadOnlyList<string> GetProductCodes()
        {
            return Profiles.Keys.ToList();
        }
    }
}

