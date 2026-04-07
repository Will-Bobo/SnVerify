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
using SnVerify.Domain.DeviceAccess;
using SnVerify.Properties;

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
                        ProductDisplayName = "SOLTAG25",
                        Mode = VerificationMode.Legacy,
                        AdbConfig = new DeviceAdbConfig
                        {
                            BootstrapCommandSpecs = new List<BootstrapCommandSpec>
                            {
                                new BootstrapCommandSpec
                                {
                                    Command = "shell ylzero",
                                    AcceptableExitCodes = new[] { 127, 255 },
                                    TimeoutBehavior = BootstrapTimeoutBehavior.Fail
                                }
                            },
                            AggregateCommand = null,
                            Commands = new List<DeviceInfoCommand>
                            {
                                new DeviceInfoCommand
                                {
                                    Field = DeviceInfoField.DeviceSn,
                                    Command = "shell getprop sys.skyroam.osi.sn",
                                    ParserKey = ParserKeys.Field.Trim
                                },
                                new DeviceInfoCommand
                                {
                                    Field = DeviceInfoField.AndroidVersion,
                                    Command = "shell getprop ro.build.display.id",
                                    ParserKey = ParserKeys.Field.Trim
                                }
                            }
                        },
                        FieldLabels = null,
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
                        ProductDisplayName = "KM001",
                        Mode = VerificationMode.Phase3,
                        AdbConfig = new DeviceAdbConfig
                        {
                            BootstrapCommandSpecs = null,
                            AggregateCommand = new AggregateDeviceInfoCommand
                            {
                                Command = "shell dumpsys window getmcuversion",
                                ParserKey = ParserKeys.Aggregate.Km001McuVersion
                            },
                            Commands = null
                        },
                        FieldLabels = new Dictionary<DeviceInfoField, string>
                        {
                            { DeviceInfoField.DeviceSn, GetResource("Label_DeviceSn", "设备SN") },
                            { DeviceInfoField.AndroidVersion, GetResource("Label_AndroidVersionNo", "Android版本号") },
                            { DeviceInfoField.BoardVersion, GetResource("Label_ChipVersion", "芯片版本号") },
                            { DeviceInfoField.ChargeBoardVersion, GetResource("Label_ChargeBoardVersion", "充电板版本号") },
                            { DeviceInfoField.ChipId, GetResource("Label_ChipId", "芯片ID") },
                            { DeviceInfoField.WifiMac, GetResource("Label_MacAddress", "MAC地址") }
                        },
                        EnableChipIdCheck = true,
                        EnableWifiMacCheck = true,
                        EnableBoardVersionCheck = true,
                        EnableChargeBoardVersionCheck = true
                    }
                },
                {
                    "KM008",
                    new ProductProfile
                    {
                        ProductCode = "KM008",
                        ProductDisplayName = "KM008",
                        Mode = VerificationMode.Phase3,
                        AdbConfig = new DeviceAdbConfig
                        {
                            BootstrapCommandSpecs = null,
                            AggregateCommand = new AggregateDeviceInfoCommand
                            {
                                Command = "shell dumpsys window getmcuversion",
                                ParserKey = ParserKeys.Aggregate.Km008AndroidVersion
                            },
                            Commands = null
                        },
                        FieldLabels = new Dictionary<DeviceInfoField, string>
                        {
                            { DeviceInfoField.DeviceSn, GetResource("Label_DeviceSn", "设备SN") },
                            { DeviceInfoField.AndroidVersion, GetResource("Label_AndroidVersionNo", "Android版本号") },
                            { DeviceInfoField.WifiMac, GetResource("Label_MacAddress", "MAC地址") }
                        },
                        EnableChipIdCheck = false,
                        EnableWifiMacCheck = true,
                        EnableBoardVersionCheck = false,
                        EnableChargeBoardVersionCheck = false
                    }
                }
            };

            Profiles = dict;
        }

        private static string GetResource(string key, string fallback)
        {
            var value = Resources.ResourceManager.GetString(key, Resources.Culture);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
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

