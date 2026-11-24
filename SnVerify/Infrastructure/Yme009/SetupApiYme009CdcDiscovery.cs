using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;
using SnVerify.Services.Logging;
using SnVerify.Services.Yme009;

namespace SnVerify.Infrastructure.Yme009
{
    /// <summary>
    /// 用 SetupAPI 枚举当前在线的 Ports 设备，再按 CDC ACM 规则筛选。不使用 WMI，也不使用 VID_0E8D。
    /// </summary>
    public sealed class SetupApiYme009CdcDiscovery : IYme009CdcDiscovery
    {
        private static readonly Guid PortsClass = new Guid("4d36e978-e325-11ce-bfc1-08002be10318");
        private readonly ILoggingService _logging;
        private readonly Yme009RoundLogScope _scope;

        public SetupApiYme009CdcDiscovery(ILoggingService logging = null, Yme009RoundLogScope scope = null)
        {
            _logging = logging;
            _scope = scope;
        }

        public Task<Yme009CdcScan> DiscoverAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var portsClass = PortsClass;
            var handle = SetupDiGetClassDevsW(ref portsClass, null, IntPtr.Zero, DigcfPresent);
            if (handle == InvalidHandle)
            {
                Log("CDC发现失败：SetupDiGetClassDevs 返回无效句柄");
                return Task.FromResult(Yme009CdcScan.Failed());
            }

            try
            {
                var found = new List<Yme009CdcCandidate>();
                var info = NewInfo();
                for (uint index = 0; SetupDiEnumDeviceInfo(handle, index, ref info); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryRead(handle, info, out var instanceId, out var portName, out var compatible)
                        && Yme009CdcAcmFilter.TryMatch(instanceId, portName, compatible, out var candidate))
                    {
                        found.Add(candidate);
                    }

                    info = NewInfo();
                }

                return Task.FromResult(Yme009CdcScan.Found(found));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("CDC发现失败：" + ex.GetType().Name + " " + ex.Message);
                return Task.FromResult(Yme009CdcScan.Failed());
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(handle);
            }
        }

        private void Log(string message)
        {
            if (_logging == null)
                return;

            _logging.LogWarning(Yme009AccessLog.Line(_scope, message));
        }

        private static SpDevInfoData NewInfo()
        {
            return new SpDevInfoData { CbSize = Marshal.SizeOf(typeof(SpDevInfoData)) };
        }

        private static bool TryRead(
            IntPtr handle,
            SpDevInfoData info,
            out string instanceId,
            out string portName,
            out IReadOnlyList<string> compatible)
        {
            instanceId = ReadInstanceId(handle, ref info);
            compatible = ReadRegistryMulti(handle, ref info, SpdrpCompatibleIds);
            portName = ReadPortName(handle, ref info);
            return true;
        }

        private static string ReadInstanceId(IntPtr handle, ref SpDevInfoData info)
        {
            int size;
            SetupDiGetDeviceInstanceIdW(handle, ref info, null, 0, out size);
            if (size <= 1)
                return string.Empty;

            var buffer = new char[size];
            if (!SetupDiGetDeviceInstanceIdW(handle, ref info, buffer, size, out size))
                return string.Empty;

            return new string(buffer).TrimEnd('\0');
        }

        private static IReadOnlyList<string> ReadRegistryMulti(IntPtr handle, ref SpDevInfoData info, uint property)
        {
            var size = 0;
            SetupDiGetDeviceRegistryPropertyW(handle, ref info, property, out _, null, 0, ref size);
            if (size <= 2)
                return Array.Empty<string>();

            var buffer = new byte[size];
            if (!SetupDiGetDeviceRegistryPropertyW(handle, ref info, property, out _, buffer, size, ref size))
                return Array.Empty<string>();

            var text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            return text.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ReadPortName(IntPtr handle, ref SpDevInfoData info)
        {
            var key = SetupDiOpenDevRegKey(handle, ref info, DicsFlagGlobal, 0, DiRegDev, KeyQueryValue);
            if (key == InvalidHandle)
                return string.Empty;

            try
            {
                var size = 256;
                var buffer = new byte[size];
                var result = RegQueryValueExW(key, "PortName", IntPtr.Zero, out _, buffer, ref size);
                if (result != 0 || size < 2)
                    return string.Empty;

                return Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        private const int DigcfPresent = 0x2;
        private const uint SpdrpCompatibleIds = 2;
        private const uint DicsFlagGlobal = 0x1;
        private const uint DiRegDev = 0x1;
        private const uint KeyQueryValue = 0x1;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDevInfoData
        {
            public int CbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string enumerator, IntPtr parent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevInfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData,
            char[] deviceInstanceId,
            int deviceInstanceIdSize,
            out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            int propertyBufferSize,
            ref int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiOpenDevRegKey(
            IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData,
            uint scope,
            uint hwProfile,
            uint keyType,
            uint samDesired);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueExW(
            IntPtr hKey,
            string lpValueName,
            IntPtr lpReserved,
            out int lpType,
            byte[] lpData,
            ref int lpcbData);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);
    }
}
