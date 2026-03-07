/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：TrimParser 单元测试。</remarks>

using NUnit.Framework;
using SnVerify.Infrastructure.DeviceAccess.Parser;

namespace SnVerify.Tests.Infrastructure.DeviceAccess
{
    [TestFixture]
    public class TrimParserTests
    {
        [Test]
        public void Parse_TrimsOutput()
        {
            var parser = new TrimParser();

            Assert.That(parser.Parse("  abc  "), Is.EqualTo("abc"));
            Assert.That(parser.Parse("x"), Is.EqualTo("x"));
            Assert.That(parser.Parse(null), Is.EqualTo(""));
        }
    }
}
