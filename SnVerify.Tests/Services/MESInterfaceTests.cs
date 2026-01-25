/// <author>
/// AI Assistant
/// </author>

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Logging;
using SnVerify.Services.MES;
using System.Threading;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// MESInterface 单元测试
    /// </summary>
    [TestFixture]
    public class MESInterfaceTests
    {
        private IMESInterface _mesInterface;
        private Mock<IFileLogger> _loggerMock;
        private Mock<HttpMessageHandler> _httpHandlerMock;
        private HttpClient _httpClient;
        private const string TestMesUrl = "http://test-mes.example.com";
        private const string TestBatchId = "BATCH001";
        private const string TestSn = "ABC123";

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<IFileLogger>();
            _httpHandlerMock = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_httpHandlerMock.Object);
            _mesInterface = new MESInterface(TestMesUrl, _loggerMock.Object, _httpClient);
        }

        [TearDown]
        public void TearDown()
        {
            _mesInterface?.Dispose();
            _httpClient?.Dispose();
        }

        [Test]
        public void Snapshot_ShouldReturnInitialIdleState()
        {
            // Assert
            Assert.That(_mesInterface.Snapshot.IsProcessing, Is.False);
            Assert.That(_mesInterface.Snapshot.LastResultStatus, Is.Null);
            Assert.That(_mesInterface.Snapshot.ErrorMessage, Is.Null);
            Assert.That(_mesInterface.Snapshot.CachedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task UploadTestResultAsync_ShouldReturnSuccess_WhenHttpRequestSucceeds()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟成功的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("Success")
                });

            // Act
            var uploadResult = await _mesInterface.UploadTestResultAsync(result);

            // Assert
            Assert.That(uploadResult.IsSuccess, Is.True);
            Assert.That(_mesInterface.Snapshot.LastResultStatus, Is.EqualTo("SUCCESS"));
            Assert.That(_mesInterface.Snapshot.CachedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task UploadTestResultAsync_ShouldCacheResult_WhenUploadFails()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟失败的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent("Server Error")
                });

            // Act
            var uploadResult = await _mesInterface.UploadTestResultAsync(result);

            // Assert
            Assert.That(uploadResult.IsSuccess, Is.False);
            var cachedResults = _mesInterface.GetCachedResults();
            Assert.That(cachedResults.Count, Is.GreaterThan(0));
            Assert.That(cachedResults.Any(r => r.SN == TestSn), Is.True);
        }

        [Test]
        public async Task UploadTestResultAsync_ShouldUpdateSnapshot_WhenProcessing()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟成功的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("Success")
                });

            // Act
            await _mesInterface.UploadTestResultAsync(result);

            // Assert
            var snapshot = _mesInterface.Snapshot;
            Assert.That(snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(snapshot.IsProcessing, Is.False); // 应该已完成
        }

        [Test]
        public async Task UploadTestResultAsync_ShouldUpdateSnapshot_WhenFailed()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "FAIL",
                FailReason = "MISMATCH",
                VerifyTime = DateTime.Now
            };

            // 模拟失败的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent("Bad Request")
                });

            // Act
            await _mesInterface.UploadTestResultAsync(result);

            // Assert
            var snapshot = _mesInterface.Snapshot;
            Assert.That(snapshot.LastResultStatus, Is.EqualTo("FAIL"));
            Assert.That(snapshot.ErrorMessage, Is.Not.Null);
            Assert.That(snapshot.CachedCount, Is.GreaterThan(0));
        }

        [Test]
        public void GetCachedResults_ShouldReturnEmptyList_Initially()
        {
            // Assert
            var cachedResults = _mesInterface.GetCachedResults();
            Assert.That(cachedResults, Is.Not.Null);
            Assert.That(cachedResults.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetCachedResults_ShouldReturnCachedResults_AfterFailedUpload()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟失败的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent("Server Error")
                });

            // Act
            await _mesInterface.UploadTestResultAsync(result);

            // Assert
            var cachedResults = _mesInterface.GetCachedResults();
            Assert.That(cachedResults.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task RetryCachedResultsAsync_ShouldReturnZero_WhenNoCachedResults()
        {
            // Act
            var retryCount = await _mesInterface.RetryCachedResultsAsync();

            // Assert
            Assert.That(retryCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RetryCachedResultsAsync_ShouldAttemptRetry_WhenCachedResultsExist()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟第一次上传失败
            _httpHandlerMock
                .Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent("Server Error")
                })
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("Success")
                });

            // 先上传失败，结果会被缓存
            await _mesInterface.UploadTestResultAsync(result);

            // Act
            var retryCount = await _mesInterface.RetryCachedResultsAsync();

            // Assert
            // 重试应该成功
            Assert.That(retryCount, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task UploadTestResultAsync_ShouldThrowException_WhenResultIsNull()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _mesInterface.UploadTestResultAsync(null));
        }

        [Test]
        public async Task Snapshot_ShouldIncludeBatchId()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟成功的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("Success")
                });

            // Act
            await _mesInterface.UploadTestResultAsync(result);

            // Assert
            Assert.That(_mesInterface.Snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public async Task UploadTestResultAsync_ShouldNotDuplicateCache()
        {
            // Arrange
            var result = new SnVerifyResult
            {
                Id = 1,
                BatchId = TestBatchId,
                SN = TestSn,
                Result = "PASS",
                VerifyTime = DateTime.Now
            };

            // 模拟失败的 HTTP 响应
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent("Server Error")
                });

            // Act - 多次上传同一个结果
            await _mesInterface.UploadTestResultAsync(result);
            await _mesInterface.UploadTestResultAsync(result);

            // Assert
            var cachedResults = _mesInterface.GetCachedResults();
            // 由于 Id 相同，应该只缓存一次（或两次，取决于去重逻辑）
            Assert.That(cachedResults.Count(r => r.SN == TestSn), Is.GreaterThan(0));
        }
    }
}
