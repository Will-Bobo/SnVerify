using NUnit.Framework;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Infrastructure.DeviceAccess.Parser;

namespace SnVerify.Tests.Infrastructure.DeviceAccess
{
    [TestFixture]
    public class Km001McuVersionAggregateParserTests
    {
        private Km001McuVersionAggregateParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new Km001McuVersionAggregateParser();
        }

        [Test]
        public void Parse_WhenStandardOutput_ShouldMapAllFields()
        {
            var output = "2025-03-09 15:30:12\n1.0.3,2.1.4,CHIP-A1,13,SN123456,aa:bb:cc:dd:ee:ff";

            var result = _parser.Parse(output);

            Assert.That(result.ChargeBoardVersion, Is.EqualTo("1.0.3"));
            Assert.That(result.BoardVersion, Is.EqualTo("2.1.4"));
            Assert.That(result.ChipId, Is.EqualTo("CHIP-A1"));
            Assert.That(result.AndroidVersion, Is.EqualTo("13"));
            Assert.That(result.DeviceSn, Is.EqualTo("SN123456"));
            Assert.That(result.WifiMac, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
        }

        [Test]
        public void Parse_WhenWindowsLineBreak_ShouldParseSuccessfully()
        {
            var output = "2025-03-09 15:30:12\r\n1.0.3,2.1.4,CHIP-A1,13,SN123456,AA:BB:CC:DD:EE:FF";

            var result = _parser.Parse(output);

            Assert.That(result.DeviceSn, Is.EqualTo("SN123456"));
            Assert.That(result.WifiMac, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
        }

        [Test]
        public void Parse_WhenOnlyOneLine_ShouldThrowAggregateProtocolException()
        {
            var output = "2025-03-09 15:30:12";

            Assert.That(
                () => _parser.Parse(output),
                Throws.TypeOf<AggregateProtocolException>().With.Message.Contains("至少两行"));
        }

        [Test]
        public void Parse_WhenColumnsLessThanSix_ShouldThrowAggregateProtocolException()
        {
            var output = "2025-03-09 15:30:12\n1.0.3,2.1.4,CHIP-A1";

            var ex = Assert.Throws<AggregateProtocolException>(() => _parser.Parse(output));
            Assert.That(ex.Message, Does.Contain("字段数量"));
            Assert.That(ex.Message, Does.Contain("原始输出"));
            Assert.That(ex.Message, Does.Contain("2025-03-09 15:30:12"));
        }

        [Test]
        public void Parse_WhenColumnsMoreThanSix_ShouldUseFirstSixColumns()
        {
            var output = "2025-03-09 15:30:12\n1.0.3,2.1.4,CHIP-A1,13,SN123456,AA:BB:CC:DD:EE:FF,EXTRA1,EXTRA2";

            var result = _parser.Parse(output);

            Assert.That(result.ChargeBoardVersion, Is.EqualTo("1.0.3"));
            Assert.That(result.BoardVersion, Is.EqualTo("2.1.4"));
            Assert.That(result.ChipId, Is.EqualTo("CHIP-A1"));
            Assert.That(result.AndroidVersion, Is.EqualTo("13"));
            Assert.That(result.DeviceSn, Is.EqualTo("SN123456"));
            Assert.That(result.WifiMac, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
        }

        [Test]
        public void Parse_WhenOutputEmpty_ShouldThrowInvalidOperationException()
        {
            Assert.That(
                () => _parser.Parse(string.Empty),
                Throws.TypeOf<System.InvalidOperationException>().With.Message.Contains("ADB 输出为空"));
        }
    }
}
