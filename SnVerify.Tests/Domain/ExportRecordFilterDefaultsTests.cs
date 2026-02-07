/// <summary>
/// ExportRecordFilterDefaults 默认勾选逻辑单元测试（TDD）。
/// 测试 GetDefaultCheckState 与 ToFilter，无 WPF 依赖。
/// </summary>
using System.Collections.Generic;
using NUnit.Framework;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Export;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class ExportRecordFilterDefaultsTests
    {
        [Test]
        public void GetDefaultCheckState_WhenTypesNull_ReturnsBothChecked()
        {
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(null);
            Assert.That(snChecked, Is.True);
            Assert.That(verChecked, Is.True);
        }

        [Test]
        public void GetDefaultCheckState_WhenTypesEmpty_ReturnsBothChecked()
        {
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(new List<VerificationType>());
            Assert.That(snChecked, Is.True);
            Assert.That(verChecked, Is.True);
        }

        [Test]
        public void GetDefaultCheckState_WhenOnlySnMatch_ReturnsSnCheckedVersionUnchecked()
        {
            var types = new[] { VerificationType.SnMatch };
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(types);
            Assert.That(snChecked, Is.True);
            Assert.That(verChecked, Is.False);
        }

        [Test]
        public void GetDefaultCheckState_WhenOnlyVersionMatch_ReturnsVersionCheckedSnUnchecked()
        {
            var types = new[] { VerificationType.VersionMatch };
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(types);
            Assert.That(snChecked, Is.False);
            Assert.That(verChecked, Is.True);
        }

        [Test]
        public void GetDefaultCheckState_WhenMixedTypes_ReturnsBothChecked()
        {
            var types = new[] { VerificationType.SnMatch, VerificationType.VersionMatch };
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(types);
            Assert.That(snChecked, Is.True);
            Assert.That(verChecked, Is.True);
        }

        [Test]
        public void GetDefaultCheckState_WhenMultipleSnMatch_ReturnsSnCheckedVersionUnchecked()
        {
            var types = new[] { VerificationType.SnMatch, VerificationType.SnMatch };
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(types);
            Assert.That(snChecked, Is.True);
            Assert.That(verChecked, Is.False);
        }

        [Test]
        public void GetDefaultCheckState_WhenMultipleVersionMatch_ReturnsVersionCheckedSnUnchecked()
        {
            var types = new[] { VerificationType.VersionMatch, VerificationType.VersionMatch };
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(types);
            Assert.That(snChecked, Is.False);
            Assert.That(verChecked, Is.True);
        }

        [Test]
        public void ToFilter_WhenBothChecked_ReturnsAll()
        {
            var result = ExportRecordFilterDefaults.ToFilter(true, true);
            Assert.That(result, Is.SameAs(ExportRecordFilter.All));
        }

        [Test]
        public void ToFilter_WhenOnlySnChecked_ReturnsSnOnly()
        {
            var result = ExportRecordFilterDefaults.ToFilter(true, false);
            Assert.That(result, Is.SameAs(ExportRecordFilter.SnOnly));
        }

        [Test]
        public void ToFilter_WhenOnlyVersionChecked_ReturnsVersionOnly()
        {
            var result = ExportRecordFilterDefaults.ToFilter(false, true);
            Assert.That(result, Is.SameAs(ExportRecordFilter.VersionOnly));
        }

        [Test]
        public void ToFilter_WhenNeitherChecked_ReturnsNull()
        {
            var result = ExportRecordFilterDefaults.ToFilter(false, false);
            Assert.That(result, Is.Null);
        }
    }
}
