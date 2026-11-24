using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Product;

namespace SnVerify.Services.Yme009
{
    public interface IYme009CdcDiscovery
    {
        Task<Yme009CdcScan> DiscoverAsync(CancellationToken cancellationToken);
    }

    public interface IYme009SerialPortSession : IDisposable
    {
        void Write(string text);

        string ReadExisting();
    }

    public interface IYme009SerialPortFactory
    {
        IYme009SerialPortSession Open(string portName);
    }

    public sealed class Yme009AdbQueryResult
    {
        public Yme009AdbQueryResult(bool querySucceeded, IReadOnlyList<Yme009AdbDevice> devices)
        {
            QuerySucceeded = querySucceeded;
            Devices = devices ?? Array.Empty<Yme009AdbDevice>();
        }

        /// <summary>
        /// A 执行失败，B 无设备，C 有设备但不是 device，D 已找到 device，E 异常或超时。
        /// </summary>
        public string DiagnosticClass { get; set; }

        public bool QuerySucceeded { get; }

        public IReadOnlyList<Yme009AdbDevice> Devices { get; }
    }

    public interface IYme009AdbDeviceQuery
    {
        Task<Yme009AdbQueryResult> QueryAsync(CancellationToken cancellationToken);
    }

    public interface IYme009IdentityAssociator
    {
        Task<Yme009AssociationResult> AssociateAsync(
            string cdcIdentityKey,
            IReadOnlyList<Yme009AdbDevice> communicableDevices,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 保留类型，避免扩大删除范围。生产接入不再调用它，也不再把它当作单设备选择的依据。
    /// </summary>
    public sealed class Yme009UnprovenIdentityAssociator : IYme009IdentityAssociator
    {
        public Task<Yme009AssociationResult> AssociateAsync(
            string cdcIdentityKey,
            IReadOnlyList<Yme009AdbDevice> communicableDevices,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Yme009AssociationResult.Unproven());
        }
    }
}
