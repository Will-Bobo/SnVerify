using NUnit.Framework;
using SnVerify.Services.Rules;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class FailReasonTextResolverTests
    {
        [Test]
        public void Resolve_WhenKnownCode_ShouldReturnLocalizedText()
        {
            var text = FailReasonTextResolver.Resolve(RuleFailReasonCodes.AdbReadFail);

            Assert.That(text, Is.EqualTo("ADB读取数据错误或者为空"));
        }

        [Test]
        public void Resolve_WhenAndroidVersionMismatch_ShouldReturnLocalizedText()
        {
            var text = FailReasonTextResolver.Resolve(RuleFailReasonCodes.AndroidVersionMismatch);
            Assert.That(text, Is.EqualTo("设备Android版本号与目标值不匹配"));
        }

        [Test]
        public void Resolve_WhenUnknownCode_ShouldFallbackToCode()
        {
            const string code = "UNKNOWN_REASON";
            var text = FailReasonTextResolver.Resolve(code);

            Assert.That(text, Is.EqualTo(code));
        }
    }
}
