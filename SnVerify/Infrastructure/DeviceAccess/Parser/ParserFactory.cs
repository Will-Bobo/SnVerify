/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：按 ParserKey 提供 Parser。Parser 由构造函数注入，配置中不创建实例。</remarks>

using System;
using System.Collections.Generic;
using SnVerify.Domain.DeviceAccess;

namespace SnVerify.Infrastructure.DeviceAccess.Parser
{
    /// <summary>
    /// Parser 工厂实现：按 Key 返回已注册的 Parser。
    /// </summary>
    public class ParserFactory : IParserFactory
    {
        private readonly IReadOnlyDictionary<string, IDeviceInfoParser> _fieldParsers;
        private readonly IReadOnlyDictionary<string, IAggregateDeviceInfoParser> _aggregateParsers;

        public ParserFactory(
            IReadOnlyDictionary<string, IDeviceInfoParser> fieldParsers = null,
            IReadOnlyDictionary<string, IAggregateDeviceInfoParser> aggregateParsers = null)
        {
            _fieldParsers = fieldParsers ?? new Dictionary<string, IDeviceInfoParser>(StringComparer.OrdinalIgnoreCase);
            _aggregateParsers = aggregateParsers ?? new Dictionary<string, IAggregateDeviceInfoParser>(StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public IDeviceInfoParser Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Parser key 不能为空", nameof(key));
            if (_fieldParsers.TryGetValue(key, out var p))
                return p;
            throw new InvalidOperationException($"未注册的单字段 Parser: {key}");
        }

        /// <inheritdoc />
        public IAggregateDeviceInfoParser GetAggregate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Parser key 不能为空", nameof(key));
            if (_aggregateParsers.TryGetValue(key, out var p))
                return p;
            throw new InvalidOperationException($"未注册的聚合 Parser: {key}");
        }
    }
}
