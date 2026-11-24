using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;
using SnVerify.Infrastructure.Yme009;
using SnVerify.Services.Yme009;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009AccessLogTests
    {
        [Test]
        public void Should_EscapeAndTruncateRawText_When_DetailExceedsLimit()
        {
            var raw = "line1\r\n" + new string('A', 500);
            var detail = Yme009AccessLog.Detail(raw);

            Assert.That(detail, Does.StartWith("line1\\r\\n"));
            Assert.That(detail.Length, Is.LessThan(raw.Length));
            Assert.That(detail, Does.EndWith("...(truncated)"));
            Assert.That(detail, Does.Not.Contain("\n"));
            Assert.That(Yme009AccessLog.Detail(null), Is.EqualTo("(empty)"));
        }

        [Test]
        public async Task Should_ClassifyAdbQuery_When_ProcessResultChanges()
        {
            var runner = new ScriptedRunner();
            var lines = new List<string>();
            var query = new Yme009ProcessAdbDeviceQuery("adb.exe", runner, Logger(lines), new Yme009RoundLogScope());

            runner.Next = ProcessExecutionResult.Failure("start failed", "boom", -1);
            var failed = await query.QueryAsync(CancellationToken.None);
            runner.Next = ProcessExecutionResult.Success("List of devices attached\r\n\r\n", 0);
            var empty = await query.QueryAsync(CancellationToken.None);
            runner.Next = ProcessExecutionResult.Success("List of devices attached\r\nSERIAL unauthorized\r\n", 0);
            var other = await query.QueryAsync(CancellationToken.None);
            runner.Next = ProcessExecutionResult.Success("List of devices attached\r\nSERIAL device\r\n", 0);
            var ready = await query.QueryAsync(CancellationToken.None);
            runner.Next = ProcessExecutionResult.Timeout();
            var timedOut = await query.QueryAsync(CancellationToken.None);
            runner.ThrowNext = new InvalidOperationException("runner-down");
            var thrown = await query.QueryAsync(CancellationToken.None);

            Assert.That(failed.DiagnosticClass, Is.EqualTo("A"));
            Assert.That(failed.QuerySucceeded, Is.False);
            Assert.That(empty.DiagnosticClass, Is.EqualTo("B"));
            Assert.That(empty.QuerySucceeded, Is.True);
            Assert.That(other.DiagnosticClass, Is.EqualTo("C"));
            Assert.That(ready.DiagnosticClass, Is.EqualTo("D"));
            Assert.That(ready.QuerySucceeded, Is.True);
            Assert.That(timedOut.DiagnosticClass, Is.EqualTo("E"));
            Assert.That(timedOut.QuerySucceeded, Is.False);
            Assert.That(thrown.DiagnosticClass, Is.EqualTo("E"));

            var text = string.Join("\n", lines);
            Assert.That(text, Does.Contain("分类=A"));
            Assert.That(text, Does.Contain("分类=B"));
            Assert.That(text, Does.Contain("分类=C"));
            Assert.That(text, Does.Contain("状态=unauthorized"));
            Assert.That(text, Does.Contain("分类=D"));
            Assert.That(text, Does.Contain("找到device=True"));
            Assert.That(text, Does.Contain("超时=True"));
            Assert.That(text, Does.Contain("InvalidOperationException runner-down"));
            Assert.That(text, Does.Contain("stdout=List of devices attached\\r\\n"));
        }

        [Test]
        public async Task Should_TruncateAdbOutput_When_StdoutIsVeryLong()
        {
            var runner = new ScriptedRunner
            {
                Next = ProcessExecutionResult.Success(new string('Z', 800) + "\r\n", 0)
            };
            var lines = new List<string>();
            var query = new Yme009ProcessAdbDeviceQuery("adb.exe", runner, Logger(lines));

            await query.QueryAsync(CancellationToken.None);

            var text = string.Join("\n", lines);
            Assert.That(text, Does.Contain("...(truncated)"));
            Assert.That(text, Does.Not.Contain(new string('Z', 500)));
        }

        private static ILoggingService Logger(List<string> lines)
        {
            var logging = new Mock<ILoggingService>();
            logging.Setup(item => item.LogInfo(It.IsAny<string>())).Callback<string>(lines.Add);
            logging.Setup(item => item.LogWarning(It.IsAny<string>())).Callback<string>(lines.Add);
            logging.Setup(item => item.LogError(It.IsAny<string>(), It.IsAny<Exception>()))
                .Callback<string, Exception>((message, _) => lines.Add(message));
            return logging.Object;
        }

        private sealed class ScriptedRunner : IProcessRunner
        {
            public ProcessExecutionResult Next = ProcessExecutionResult.Success(string.Empty, 0);

            public Exception ThrowNext { get; set; }

            public Task<ProcessExecutionResult> RunAsync(
                string fileName,
                string arguments,
                int timeout,
                CancellationToken cancellationToken = default)
            {
                if (ThrowNext != null)
                {
                    var error = ThrowNext;
                    ThrowNext = null;
                    throw error;
                }

                return Task.FromResult(Next);
            }
        }
    }
}
