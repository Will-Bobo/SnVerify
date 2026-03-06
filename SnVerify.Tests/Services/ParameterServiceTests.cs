/// <author>
/// AI Assistant
/// </author>

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Parameter;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ParameterService 单元测试：验证基于 StorageService 的持久化与内存缓存行为。
    /// </summary>
    [TestFixture]
    public class ParameterServiceTests
    {
        private Mock<IStorageService> _storageMock;
        private IParameterService _service;

        [SetUp]
        public void SetUp()
        {
            _storageMock = new Mock<IStorageService>();
            _service = new ParameterService(_storageMock.Object);
        }

        [Test]
        public async Task GetParameterAsync_WhenNotInCache_LoadsFromStorage()
        {
            var projectId = "P1";
            var parameter = new VerificationParameter
            {
                ProjectId = projectId,
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            _storageMock
                .Setup(x => x.GetVerificationParameterAsync(projectId))
                .ReturnsAsync(parameter);

            var result = await _service.GetParameterAsync(projectId);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ProjectId, Is.EqualTo(projectId));
            _storageMock.Verify(x => x.GetVerificationParameterAsync(projectId), Times.Once);
        }

        [Test]
        public async Task GetParameterAsync_WhenInCache_DoesNotHitStorageAgain()
        {
            var projectId = "P2";
            var parameter = new VerificationParameter
            {
                ProjectId = projectId,
                ExpectedAndroidVersion = "A2"
            };

            _storageMock
                .Setup(x => x.GetVerificationParameterAsync(projectId))
                .ReturnsAsync(parameter);

            var first = await _service.GetParameterAsync(projectId);
            var second = await _service.GetParameterAsync(projectId);

            Assert.That(first, Is.SameAs(second));
            _storageMock.Verify(x => x.GetVerificationParameterAsync(projectId), Times.Once);
        }

        [Test]
        public async Task SaveParameterAsync_PersistsAndUpdatesCache()
        {
            var projectId = "P3";
            var parameter = new VerificationParameter
            {
                ProjectId = projectId,
                ExpectedAndroidVersion = "A3"
            };

            _storageMock
                .Setup(x => x.SaveVerificationParameterAsync(parameter))
                .Returns(Task.CompletedTask);

            await _service.SaveParameterAsync(parameter);

            _storageMock.Verify(x => x.SaveVerificationParameterAsync(parameter), Times.Once);

            var fromCache = await _service.GetParameterAsync(projectId);
            Assert.That(fromCache, Is.Not.Null);
            Assert.That(fromCache.ExpectedAndroidVersion, Is.EqualTo("A3"));
            _storageMock.Verify(x => x.GetVerificationParameterAsync(projectId), Times.Never);
        }
    }
}

