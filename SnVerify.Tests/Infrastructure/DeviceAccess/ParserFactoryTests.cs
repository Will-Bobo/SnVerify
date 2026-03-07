/// <author>AI Assistant</author>
/// <remarks>DeviceAccess 子系统：ParserFactory 单元测试。</remarks>

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Infrastructure.DeviceAccess.Parser;

namespace SnVerify.Tests.Infrastructure.DeviceAccess
{
    [TestFixture]
    public class ParserFactoryTests
    {
        [Test]
        public void Get_WhenKeyRegistered_ReturnsParser()
        {
            var parser = new Mock<IDeviceInfoParser>().Object;
            var dict = new Dictionary<string, IDeviceInfoParser>(System.StringComparer.OrdinalIgnoreCase) { { ParserKeys.Field.Trim, parser } };
            var factory = new ParserFactory(dict, null);

            var result = factory.Get(ParserKeys.Field.Trim);

            Assert.That(result, Is.SameAs(parser));
        }

        [Test]
        public void Get_WhenKeyNotRegistered_Throws()
        {
            var factory = new ParserFactory();

            Assert.That(() => factory.Get("Unknown"), Throws.InvalidOperationException.With.Message.Contains("未注册"));
        }

        [Test]
        public void Get_WhenKeyNullOrEmpty_Throws()
        {
            var factory = new ParserFactory();

            Assert.That(() => factory.Get(null), Throws.ArgumentException);
            Assert.That(() => factory.Get(""), Throws.ArgumentException);
        }

        [Test]
        public void GetAggregate_WhenKeyRegistered_ReturnsParser()
        {
            var parser = new Mock<IAggregateDeviceInfoParser>().Object;
            var dict = new Dictionary<string, IAggregateDeviceInfoParser>(System.StringComparer.OrdinalIgnoreCase) { { "Soltag", parser } };
            var factory = new ParserFactory(null, dict);

            var result = factory.GetAggregate("Soltag");

            Assert.That(result, Is.SameAs(parser));
        }

        [Test]
        public void GetAggregate_WhenKeyNotRegistered_Throws()
        {
            var factory = new ParserFactory();

            Assert.That(() => factory.GetAggregate("Unknown"), Throws.InvalidOperationException.With.Message.Contains("未注册"));
        }
    }
}
