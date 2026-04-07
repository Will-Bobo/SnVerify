using Moq;
using NUnit.Framework;
using SnVerify.Infrastructure.Export;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Storage;
using SnVerify.Services.Storage.Export;

namespace SnVerify.Tests.Services.Storage
{
    [TestFixture]
    public sealed class SessionExporterFactoryTests
    {
        [Test]
        public void GetExporter_When_Km001_ShouldReturn_Phase3Exporter()
        {
            var storage = new Mock<IStorageService>(MockBehavior.Loose);
            var f = new SessionExporterFactory(storage.Object, new ProductExportRegistry(), new DefaultExportValueResolver(), new ProductRegistryAdapter());
            Assert.That(f.GetExporter("KM001"), Is.InstanceOf<Km001SessionExporter>());
        }

        [Test]
        public void GetExporter_When_Km008_ShouldReturn_Phase3Exporter()
        {
            var storage = new Mock<IStorageService>(MockBehavior.Loose);
            var f = new SessionExporterFactory(storage.Object, new ProductExportRegistry(), new DefaultExportValueResolver(), new ProductRegistryAdapter());
            Assert.That(f.GetExporter("KM008"), Is.InstanceOf<Km001SessionExporter>());
        }

        [Test]
        public void GetExporter_When_Soltag25_ShouldReturn_Legacy()
        {
            var storage = new Mock<IStorageService>(MockBehavior.Loose);
            var f = new SessionExporterFactory(storage.Object, new ProductExportRegistry(), new DefaultExportValueResolver(), new ProductRegistryAdapter());
            Assert.That(f.GetExporter("SOLTAG25"), Is.InstanceOf<LegacySessionExporter>());
        }

        [Test]
        public void GetExporter_When_NullProductCode_ShouldReturn_Legacy()
        {
            var storage = new Mock<IStorageService>(MockBehavior.Loose);
            var f = new SessionExporterFactory(storage.Object, new ProductExportRegistry(), new DefaultExportValueResolver(), new ProductRegistryAdapter());
            Assert.That(f.GetExporter(null), Is.InstanceOf<LegacySessionExporter>());
        }
    }
}
