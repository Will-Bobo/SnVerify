using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;
using SnVerify.Services.Yme009;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009ProcessPropertyReaderTests
    {
        [Test]
        public async Task Should_ReadEachPropertyWithTransport_When_ContextIsFormal()
        {
            var runner = new ScriptRunner();
            foreach (var definition in Yme009AttributeCatalog.All)
                runner.Results.Enqueue(ProcessExecutionResult.Success(definition.PropertyKey + "\r\n"));
            var reader = CreateReader(runner, new ScriptQuery());
            var connection = Formal();

            var raw = await reader.ReadAsync(connection, CancellationToken.None);

            Assert.That(runner.Arguments, Has.Count.EqualTo(8));
            for (var i = 0; i < Yme009AttributeCatalog.All.Count; i++)
            {
                var key = Yme009AttributeCatalog.All[i].PropertyKey;
                Assert.That(runner.Arguments[i], Is.EqualTo("-t 9 shell getprop " + key));
                Assert.That(runner.Arguments[i], Does.Not.Contain("-s"));
                Assert.That(runner.Timeouts[i], Is.EqualTo(Yme009ProcessPropertyReader.CommandTimeoutMilliseconds));
            }

            Assert.That(raw.DeviceSn.Output, Is.EqualTo(Yme009AttributeCatalog.DeviceSnKey));
            Assert.That(raw.AgingReset.CommandSucceeded, Is.True);
        }

        [Test]
        public async Task Should_ContinueAfterOneFailure_When_TransportRemains()
        {
            var runner = new ScriptRunner();
            runner.Results.Enqueue(ProcessExecutionResult.Failure("boom"));
            runner.Results.Enqueue(ProcessExecutionResult.Timeout());
            for (var i = 0; i < 6; i++)
                runner.Results.Enqueue(ProcessExecutionResult.Success("1\r\n"));
            var query = new ScriptQuery { Next = new Yme009AdbQueryResult(true, new[] { new Yme009AdbDevice("SER", "device", "9") }) };
            var reader = CreateReader(runner, query);

            var raw = await reader.ReadAsync(Formal(), CancellationToken.None);

            Assert.That(runner.Arguments, Has.Count.EqualTo(8));
            Assert.That(query.Calls, Is.EqualTo(1));
            Assert.That(raw.DeviceSn.CommandSucceeded, Is.False);
            Assert.That(raw.DeviceSn.FailureStatus, Is.EqualTo(Yme009ReadStatus.CommandFailed));
            Assert.That(raw.DeviceSn.Output, Is.Null);
            Assert.That(raw.SoftwareVersion.FailureStatus, Is.EqualTo(Yme009ReadStatus.TimedOut));
            Assert.That(raw.Imei1.Output, Is.EqualTo("1"));
        }

        [Test]
        public async Task Should_StopRemainingFields_When_TransportIsReportedMissing()
        {
            var runner = new ScriptRunner();
            runner.Results.Enqueue(ProcessExecutionResult.Failure("error: device '9' not found"));
            runner.Results.Enqueue(ProcessExecutionResult.Success("should-not-run"));
            var query = new ScriptQuery();
            var reader = CreateReader(runner, query);

            var raw = await reader.ReadAsync(Formal(), CancellationToken.None);

            Assert.That(runner.Arguments, Has.Count.EqualTo(1));
            Assert.That(query.Calls, Is.EqualTo(0));
            Assert.That(raw.DeviceSn.FailureStatus, Is.EqualTo(Yme009ReadStatus.Disconnected));
            Assert.That(raw.SoftwareVersion.FailureStatus, Is.EqualTo(Yme009ReadStatus.Disconnected));
            Assert.That(raw.AgingReset.FailureStatus, Is.EqualTo(Yme009ReadStatus.Disconnected));
        }

        [Test]
        public async Task Should_MarkDisconnected_When_SuccessfulDeviceListLosesTransport()
        {
            var runner = new ScriptRunner();
            runner.Results.Enqueue(ProcessExecutionResult.Failure("closed"));
            var query = new ScriptQuery { Next = new Yme009AdbQueryResult(true, Array.Empty<Yme009AdbDevice>()) };
            var reader = CreateReader(runner, query);

            var raw = await reader.ReadAsync(Formal(), CancellationToken.None);

            Assert.That(query.Calls, Is.EqualTo(1));
            Assert.That(raw.DeviceSn.FailureStatus, Is.EqualTo(Yme009ReadStatus.Disconnected));
            Assert.That(runner.Arguments, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Should_NotIssueCommand_When_FormalContextIsUnavailable()
        {
            var runner = new ScriptRunner();
            var reader = CreateReader(runner, new ScriptQuery());
            var temporary = Yme009DeviceConnection.Temporary("g", "A");
            var revoked = Formal();
            revoked.Revoke();

            Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(temporary, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(revoked, CancellationToken.None));
            Assert.That(runner.Arguments, Is.Empty);
        }

        private static Yme009ProcessPropertyReader CreateReader(ScriptRunner runner, ScriptQuery query)
        {
            return new Yme009ProcessPropertyReader("adb", runner, query, Mock.Of<ILoggingService>());
        }

        private static Yme009DeviceConnection Formal()
        {
            var connection = Yme009DeviceConnection.Temporary("g", "A");
            Assert.That(connection.TryAcceptUniqueStationTransport("9", "SER"), Is.True);
            return connection;
        }

        private sealed class ScriptRunner : IProcessRunner
        {
            public List<string> Arguments { get; } = new List<string>();

            public List<int> Timeouts { get; } = new List<int>();

            public Queue<ProcessExecutionResult> Results { get; } = new Queue<ProcessExecutionResult>();

            public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, int timeout, CancellationToken cancellationToken = default)
            {
                Arguments.Add(arguments);
                Timeouts.Add(timeout);
                return Task.FromResult(Results.Dequeue());
            }
        }

        private sealed class ScriptQuery : IYme009AdbDeviceQuery
        {
            public Yme009AdbQueryResult Next { get; set; }

            public int Calls { get; private set; }

            public Task<Yme009AdbQueryResult> QueryAsync(CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Next);
            }
        }
    }
}
