using System;
using System.Collections.Generic;

namespace SnVerify.Domain.Product
{
    public enum Yme009FieldKind
    {
        DeviceSn = 0,
        SoftwareVersion = 1,
        Imei1 = 2,
        Imei2 = 3,
        WifiMac = 4,
        ProductionTest = 5,
        AgingReboot = 6,
        AgingReset = 7
    }

    public enum Yme009FieldRole
    {
        RequiredText = 0,
        RequiredBinary = 1,
        Information = 2
    }

    /// <summary>
    /// 命令成功之后的字段分类，以及命令未成功时的读取状态。每个字段只有一个状态。
    /// </summary>
    public enum Yme009ReadStatus
    {
        Ok = 0,
        Missing = 1,
        ProtocolInvalid = 2,
        CommandFailed = 3,
        TimedOut = 4,
        Disconnected = 5
    }

    public enum Yme009ItemJudgment
    {
        Pass = 0,
        Fail = 1
    }

    public sealed class Yme009AttributeDefinition
    {
        public Yme009AttributeDefinition(Yme009FieldKind kind, string propertyKey, Yme009FieldRole role, string displayName)
        {
            Kind = kind;
            PropertyKey = propertyKey ?? throw new ArgumentNullException(nameof(propertyKey));
            Role = role;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        }

        public Yme009FieldKind Kind { get; }

        public string PropertyKey { get; }

        public Yme009FieldRole Role { get; }

        public string DisplayName { get; }

        public bool ParticipatesInJudgment
        {
            get { return Role != Yme009FieldRole.Information; }
        }
    }

    public static class Yme009AttributeCatalog
    {
        public const string DeviceSnKey = "sys.skyroam.osi.sn";
        public const string SoftwareVersionKey = "ro.build.display.id";
        public const string Imei1Key = "sys.skyroam.imei1";
        public const string Imei2Key = "sys.skyroam.imei2";
        public const string WifiMacKey = "persist.sys.wifi.mac";
        public const string ProductionTestKey = "persist.sys.yl.cit";
        public const string AgingRebootKey = "persist.sys.yl.aging.reboot";
        public const string AgingResetKey = "persist.sys.yl.aging.reset";

        private static readonly Yme009AttributeDefinition[] Definitions =
        {
            new Yme009AttributeDefinition(Yme009FieldKind.DeviceSn, DeviceSnKey, Yme009FieldRole.RequiredText, "设备SN"),
            new Yme009AttributeDefinition(Yme009FieldKind.SoftwareVersion, SoftwareVersionKey, Yme009FieldRole.RequiredText, "软件版本"),
            new Yme009AttributeDefinition(Yme009FieldKind.Imei1, Imei1Key, Yme009FieldRole.Information, "IMEI1"),
            new Yme009AttributeDefinition(Yme009FieldKind.Imei2, Imei2Key, Yme009FieldRole.Information, "IMEI2"),
            new Yme009AttributeDefinition(Yme009FieldKind.WifiMac, WifiMacKey, Yme009FieldRole.Information, "WiFi MAC"),
            new Yme009AttributeDefinition(Yme009FieldKind.ProductionTest, ProductionTestKey, Yme009FieldRole.RequiredBinary, "产测结果"),
            new Yme009AttributeDefinition(Yme009FieldKind.AgingReboot, AgingRebootKey, Yme009FieldRole.RequiredBinary, "重启老化"),
            new Yme009AttributeDefinition(Yme009FieldKind.AgingReset, AgingResetKey, Yme009FieldRole.RequiredBinary, "恢复出厂老化")
        };

        public static IReadOnlyList<Yme009AttributeDefinition> All
        {
            get { return Definitions; }
        }

        public static Yme009AttributeDefinition Get(Yme009FieldKind kind)
        {
            for (var i = 0; i < Definitions.Length; i++)
            {
                if (Definitions[i].Kind == kind)
                    return Definitions[i];
            }

            throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>
    /// 单个属性的原始读取结果。命令未成功时不保留输出，避免从失败输出里解释出 0 或 1。
    /// </summary>
    public sealed class Yme009FieldRaw
    {
        private Yme009FieldRaw(bool commandSucceeded, Yme009ReadStatus failureStatus, string output)
        {
            CommandSucceeded = commandSucceeded;
            FailureStatus = failureStatus;
            Output = output;
        }

        public bool CommandSucceeded { get; }

        public Yme009ReadStatus FailureStatus { get; }

        public string Output { get; }

        public static Yme009FieldRaw Succeeded(string output)
        {
            return new Yme009FieldRaw(true, Yme009ReadStatus.Ok, output ?? string.Empty);
        }

        public static Yme009FieldRaw CommandFailed()
        {
            return new Yme009FieldRaw(false, Yme009ReadStatus.CommandFailed, null);
        }

        public static Yme009FieldRaw TimedOut()
        {
            return new Yme009FieldRaw(false, Yme009ReadStatus.TimedOut, null);
        }

        public static Yme009FieldRaw Disconnected()
        {
            return new Yme009FieldRaw(false, Yme009ReadStatus.Disconnected, null);
        }
    }

    public sealed class Yme009RawAttributeSet
    {
        public Yme009RawAttributeSet(
            Yme009FieldRaw deviceSn,
            Yme009FieldRaw softwareVersion,
            Yme009FieldRaw imei1,
            Yme009FieldRaw imei2,
            Yme009FieldRaw wifiMac,
            Yme009FieldRaw productionTest,
            Yme009FieldRaw agingReboot,
            Yme009FieldRaw agingReset)
        {
            DeviceSn = deviceSn ?? throw new ArgumentNullException(nameof(deviceSn));
            SoftwareVersion = softwareVersion ?? throw new ArgumentNullException(nameof(softwareVersion));
            Imei1 = imei1 ?? throw new ArgumentNullException(nameof(imei1));
            Imei2 = imei2 ?? throw new ArgumentNullException(nameof(imei2));
            WifiMac = wifiMac ?? throw new ArgumentNullException(nameof(wifiMac));
            ProductionTest = productionTest ?? throw new ArgumentNullException(nameof(productionTest));
            AgingReboot = agingReboot ?? throw new ArgumentNullException(nameof(agingReboot));
            AgingReset = agingReset ?? throw new ArgumentNullException(nameof(agingReset));
        }

        public Yme009FieldRaw DeviceSn { get; }

        public Yme009FieldRaw SoftwareVersion { get; }

        public Yme009FieldRaw Imei1 { get; }

        public Yme009FieldRaw Imei2 { get; }

        public Yme009FieldRaw WifiMac { get; }

        public Yme009FieldRaw ProductionTest { get; }

        public Yme009FieldRaw AgingReboot { get; }

        public Yme009FieldRaw AgingReset { get; }

        public Yme009FieldRaw Get(Yme009FieldKind kind)
        {
            switch (kind)
            {
                case Yme009FieldKind.DeviceSn:
                    return DeviceSn;
                case Yme009FieldKind.SoftwareVersion:
                    return SoftwareVersion;
                case Yme009FieldKind.Imei1:
                    return Imei1;
                case Yme009FieldKind.Imei2:
                    return Imei2;
                case Yme009FieldKind.WifiMac:
                    return WifiMac;
                case Yme009FieldKind.ProductionTest:
                    return ProductionTest;
                case Yme009FieldKind.AgingReboot:
                    return AgingReboot;
                case Yme009FieldKind.AgingReset:
                    return AgingReset;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public sealed class Yme009ParsedField
    {
        public Yme009ParsedField(Yme009FieldKind kind, Yme009ReadStatus status, string text)
        {
            Kind = kind;
            Role = Yme009AttributeCatalog.Get(kind).Role;
            Status = status;
            Text = text;
        }

        public Yme009FieldKind Kind { get; }

        public Yme009FieldRole Role { get; }

        public Yme009ReadStatus Status { get; }

        /// <summary>
        /// 命令成功时为去掉末尾连续换行后的文本。命令未成功时为空。
        /// </summary>
        public string Text { get; }
    }

    public sealed class Yme009ParsedAttributes
    {
        public Yme009ParsedAttributes(
            Yme009ParsedField deviceSn,
            Yme009ParsedField softwareVersion,
            Yme009ParsedField imei1,
            Yme009ParsedField imei2,
            Yme009ParsedField wifiMac,
            Yme009ParsedField productionTest,
            Yme009ParsedField agingReboot,
            Yme009ParsedField agingReset)
        {
            DeviceSn = deviceSn ?? throw new ArgumentNullException(nameof(deviceSn));
            SoftwareVersion = softwareVersion ?? throw new ArgumentNullException(nameof(softwareVersion));
            Imei1 = imei1 ?? throw new ArgumentNullException(nameof(imei1));
            Imei2 = imei2 ?? throw new ArgumentNullException(nameof(imei2));
            WifiMac = wifiMac ?? throw new ArgumentNullException(nameof(wifiMac));
            ProductionTest = productionTest ?? throw new ArgumentNullException(nameof(productionTest));
            AgingReboot = agingReboot ?? throw new ArgumentNullException(nameof(agingReboot));
            AgingReset = agingReset ?? throw new ArgumentNullException(nameof(agingReset));
        }

        public Yme009ParsedField DeviceSn { get; }

        public Yme009ParsedField SoftwareVersion { get; }

        public Yme009ParsedField Imei1 { get; }

        public Yme009ParsedField Imei2 { get; }

        public Yme009ParsedField WifiMac { get; }

        public Yme009ParsedField ProductionTest { get; }

        public Yme009ParsedField AgingReboot { get; }

        public Yme009ParsedField AgingReset { get; }

        public Yme009ParsedField Get(Yme009FieldKind kind)
        {
            switch (kind)
            {
                case Yme009FieldKind.DeviceSn:
                    return DeviceSn;
                case Yme009FieldKind.SoftwareVersion:
                    return SoftwareVersion;
                case Yme009FieldKind.Imei1:
                    return Imei1;
                case Yme009FieldKind.Imei2:
                    return Imei2;
                case Yme009FieldKind.WifiMac:
                    return WifiMac;
                case Yme009FieldKind.ProductionTest:
                    return ProductionTest;
                case Yme009FieldKind.AgingReboot:
                    return AgingReboot;
                case Yme009FieldKind.AgingReset:
                    return AgingReset;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
