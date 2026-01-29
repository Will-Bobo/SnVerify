/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.5 阶段 1 TDD：ProjectName/OrderName 命名校验单元测试。先写测试，再实现校验器。
/// </remarks>

using NUnit.Framework;
using SnVerify.Domain.Validation;

namespace SnVerify.Tests.Domain
{
    /// <summary>
    /// OrderName / ProjectName 命名校验单元测试。契约：禁止文件系统特殊字符，长度上限 64，不允许中文。
    /// </summary>
    [TestFixture]
    public class OrderNameValidatorTests
    {
        private IOrderNameValidator _validator;

        [SetUp]
        public void SetUp()
        {
            _validator = new OrderNameValidator();
        }

        [TestCase("Order_ABC_123")]
        [TestCase("PROJ001")]
        [TestCase("a")]
        [TestCase("A1b2C3")]
        public void Validate_ValidName_ReturnsTrue(string name)
        {
            var result = _validator.Validate(name, out var message);

            Assert.That(result, Is.True);
            Assert.That(message, Is.Null.Or.Empty);
        }

        [TestCase("订单")]
        [TestCase("订单名")]
        [TestCase("中")]
        public void Validate_ContainsChinese_ReturnsFalse(string name)
        {
            var result = _validator.Validate(name, out var message);

            Assert.That(result, Is.False);
            Assert.That(message, Is.Not.Null.And.Not.Empty);
        }

        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("a*b")]
        [TestCase("a?b")]
        [TestCase("a:b")]
        [TestCase("a<b")]
        [TestCase("a>b")]
        [TestCase("a|b")]
        [TestCase("a\"b")]
        public void Validate_ContainsFileSystemSpecialChars_ReturnsFalse(string name)
        {
            var result = _validator.Validate(name, out var message);

            Assert.That(result, Is.False);
            Assert.That(message, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Validate_LengthGreaterThan64_ReturnsFalse()
        {
            var name = new string('a', 65);

            var result = _validator.Validate(name, out var message);

            Assert.That(result, Is.False);
            Assert.That(message, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Validate_LengthExactly64_ReturnsTrue()
        {
            var name = new string('a', 64);

            var result = _validator.Validate(name, out var message);

            Assert.That(result, Is.True);
        }

        [Test]
        public void Validate_NullOrWhiteSpace_ReturnsFalse()
        {
            Assert.That(_validator.Validate(null, out _), Is.False);
            Assert.That(_validator.Validate("", out _), Is.False);
            Assert.That(_validator.Validate("   ", out _), Is.False);
        }
    }
}
