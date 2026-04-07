using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Infrastructure.DeviceAccess.Parser;

namespace SnVerify.Tests.Infrastructure.DeviceAccess
{
    [TestFixture]
    public sealed class Km008AndroidVersionAggregateParserTests
    {
        private readonly Km008AndroidVersionAggregateParser _parser = new Km008AndroidVersionAggregateParser();

        [Test]
        public void Parse_Should_Map_ThreeColumns()
        {
            var raw = "ignored-time\n1.0.0,SN123,aa:bb:cc:dd:ee:ff";
            var info = _parser.Parse(raw);
            Assert.That(info.AndroidVersion, Is.EqualTo("1.0.0"));
            Assert.That(info.DeviceSn, Is.EqualTo("SN123"));
            Assert.That(info.WifiMac, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
            Assert.That(info.ChipId, Is.Null);
            Assert.That(info.BoardVersion, Is.Null);
            Assert.That(info.ChargeBoardVersion, Is.Null);
        }

        [Test]
        public void Parse_When_OutputTooFewLines_ShouldThrow_AggregateProtocolException()
        {
            Assert.Throws<AggregateProtocolException>(() => _parser.Parse("onlyone"));
        }

        [Test]
        public void Parse_When_ColumnsTooFew_ShouldThrow_AggregateProtocolException()
        {
            Assert.Throws<AggregateProtocolException>(() => _parser.Parse("t\n1.0,SN1"));
        }

        [Test]
        public void Parse_When_DeviceSnEmpty_ShouldThrow_AggregateProtocolException()
        {
            Assert.Throws<AggregateProtocolException>(() => _parser.Parse("t\n1.0.0, ,aa:bb"));
        }

        [Test]
        public void Parse_When_AndroidEmpty_ShouldThrow_AggregateProtocolException()
        {
            Assert.Throws<AggregateProtocolException>(() => _parser.Parse("t\n ,SN1,A1:A1"));
        }
    }
}
