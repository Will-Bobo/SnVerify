using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Infrastructure.DeviceAccess.Parser;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Rules;
using SnVerify.Services.Storage;
using SnVerify.Services.Verification;

namespace SnVerify.Tests.Integration
{
    /// <summary>
    /// Phase4：KM008 主链路集成级验证（真实 Parser + 真实 VersionVerificationService + 真实 ProductRegistry）。
    /// </summary>
    [TestFixture]
    public sealed class Phase4Km008MainFlowIntegrationTests
    {
        [Test]
        public void Km008Parser_Output_Should_Feed_RulePipeline_And_Pass_With_RealVersionService()
        {
            var raw = "1764000000\n1.0-build,DEVICE_SN_01,aa:bb:cc:dd:ee:ff";
            var parser = new Km008AndroidVersionAggregateParser();
            var di = parser.Parse(raw);

            var profile = ProductRegistry.Get("KM008");
            Assert.That(profile, Is.Not.Null);

            var storage = new Mock<IStorageService>(MockBehavior.Strict);
            storage.Setup(s => s.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var deviceAccess = new Mock<IDeviceAccessService>(MockBehavior.Strict);
            var versionService = new VersionVerificationService();
            var executor = new RulePipelineExecutor(storage.Object, deviceAccess.Object, versionService, logger: null);

            var parameter = new VerificationParameter
            {
                SessionId = 1,
                ExpectedAndroidVersion = "1.0-build",
                ExpectedBoardVersion = null,
                ExpectedChargeBoardVersion = null
            };

            var result = executor.ExecuteAsync(profile, di, parameter, "DEVICE_SN_01", "ORDER-X", "PROJECT-X").GetAwaiter().GetResult();

            Assert.That(result.Result, Is.EqualTo("PASS"));
            Assert.That(result.DeviceInfo.ChipId, Is.Null);
            storage.Verify(s => s.IsChipIdPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
