/// <author>AI Assistant</author>
/// <remarks>
/// ExportRecordFilter 领域模型单元测试（TDD）。
/// </remarks>

using NUnit.Framework;
using SnVerify.Domain.Export;

namespace SnVerify.Tests.Domain
{
    [TestFixture]
    public class ExportRecordFilterTests
    {
        [Test]
        public void All_IncludesBothSnMatchAndVersionMatch()
        {
            Assert.That(ExportRecordFilter.All.IncludeSnMatch, Is.True);
            Assert.That(ExportRecordFilter.All.IncludeVersionMatch, Is.True);
        }

        [Test]
        public void SnOnly_IncludesSnMatchOnly()
        {
            Assert.That(ExportRecordFilter.SnOnly.IncludeSnMatch, Is.True);
            Assert.That(ExportRecordFilter.SnOnly.IncludeVersionMatch, Is.False);
        }

        [Test]
        public void VersionOnly_IncludesVersionMatchOnly()
        {
            Assert.That(ExportRecordFilter.VersionOnly.IncludeSnMatch, Is.False);
            Assert.That(ExportRecordFilter.VersionOnly.IncludeVersionMatch, Is.True);
        }

        [Test]
        public void All_SnOnly_VersionOnly_AreDistinct()
        {
            Assert.That(ExportRecordFilter.All, Is.Not.SameAs(ExportRecordFilter.SnOnly));
            Assert.That(ExportRecordFilter.All, Is.Not.SameAs(ExportRecordFilter.VersionOnly));
            Assert.That(ExportRecordFilter.SnOnly, Is.Not.SameAs(ExportRecordFilter.VersionOnly));
        }
    }
}
