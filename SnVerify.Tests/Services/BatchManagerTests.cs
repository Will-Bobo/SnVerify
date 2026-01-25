/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Batch;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// BatchManager 单元测试
    /// </summary>
    [TestFixture]
    public class BatchManagerTests
    {
        private Mock<IStorageService> _storageServiceMock;
        private Mock<IFileLogger> _loggerMock;
        private IBatchManager _batchManager;
        private const string TestBatchId = "BATCH001";
        private const string TestBatchName = "TestBatch";

        [SetUp]
        public void SetUp()
        {
            _storageServiceMock = new Mock<IStorageService>();
            _loggerMock = new Mock<IFileLogger>();
            _batchManager = new BatchManager(_storageServiceMock.Object, _loggerMock.Object);
        }

        [Test]
        public void Snapshot_ShouldReturnInitialIdleState()
        {
            // Assert
            Assert.That(_batchManager.Snapshot.IsActive, Is.False);
            Assert.That(_batchManager.Snapshot.BatchId, Is.Null);
            Assert.That(_batchManager.Snapshot.BatchName, Is.Null);
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Null);
        }

        [Test]
        public void CreateBatch_ShouldGenerateTimeBasedBatchId_WhenBatchNameIsNull()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.CreateBatchAsync(It.IsAny<BatchInfo>()))
                .Returns(Task.CompletedTask);

            // Act
            var batch = _batchManager.CreateBatch();

            // Assert
            Assert.That(batch, Is.Not.Null);
            Assert.That(batch.BatchId, Is.Not.Null);
            Assert.That(batch.BatchId, Does.StartWith("batch_"));
            Assert.That(batch.StartTime, Is.LessThanOrEqualTo(DateTime.Now));
            _storageServiceMock.Verify(x => x.CreateBatchAsync(It.IsAny<BatchInfo>()), Times.Once);
        }

        [Test]
        public void CreateBatch_ShouldUseProvidedBatchName_WhenBatchNameIsProvided()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchName))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.CreateBatchAsync(It.Is<BatchInfo>(b => b.BatchId == TestBatchName)))
                .Returns(Task.CompletedTask);

            // Act
            var batch = _batchManager.CreateBatch(TestBatchName);

            // Assert
            Assert.That(batch.BatchId, Is.EqualTo(TestBatchName));
            _storageServiceMock.Verify(x => x.CreateBatchAsync(It.Is<BatchInfo>(b => b.BatchId == TestBatchName)), Times.Once);
        }

        [Test]
        public void CreateBatch_ShouldThrowException_WhenBatchAlreadyExists()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(true);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _batchManager.CreateBatch(TestBatchId));
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("已存在"));
            Assert.That(_batchManager.Snapshot.BatchId, Is.EqualTo(TestBatchId));
        }

        [Test]
        public void StartBatch_ShouldUpdateSnapshot_WhenSuccess()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(true);

            // Act
            _batchManager.StartBatch(TestBatchId);

            // Assert
            Assert.That(_batchManager.Snapshot.IsActive, Is.True);
            Assert.That(_batchManager.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_batchManager.Snapshot.StartTime, Is.Not.Null);
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Null);
        }

        [Test]
        public void StartBatch_ShouldThrowException_WhenBatchDoesNotExist()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(false);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _batchManager.StartBatch(TestBatchId));
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("不存在"));
        }

        [Test]
        public void StartBatch_ShouldThrowException_WhenBatchIdIsEmpty()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _batchManager.StartBatch(null));
            Assert.Throws<ArgumentException>(() => _batchManager.StartBatch(""));
            Assert.Throws<ArgumentException>(() => _batchManager.StartBatch("   "));
        }

        [Test]
        public void StartBatch_ShouldThrowException_WhenAnotherBatchIsActive()
        {
            // Arrange
            var activeBatchId = "ACTIVE_BATCH";
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(activeBatchId))
                .ReturnsAsync(true);

            _batchManager.StartBatch(activeBatchId);

            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(true);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _batchManager.StartBatch(TestBatchId));
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("已有活动批次"));
        }

        [Test]
        public void EndBatch_ShouldUpdateSnapshot_WhenSuccess()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(true);

            _batchManager.StartBatch(TestBatchId);
            var startTime = _batchManager.Snapshot.StartTime;

            // Act
            _batchManager.EndBatch();

            // Assert
            Assert.That(_batchManager.Snapshot.IsActive, Is.False);
            Assert.That(_batchManager.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_batchManager.Snapshot.StartTime, Is.EqualTo(startTime));
            Assert.That(_batchManager.Snapshot.EndTime, Is.Not.Null);
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Null);
        }

        [Test]
        public void EndBatch_ShouldThrowException_WhenNoActiveBatch()
        {
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _batchManager.EndBatch());
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("没有活动批次"));
        }

        [Test]
        public void CreateBatch_ShouldHandleStorageServiceException()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.CreateBatchAsync(It.IsAny<BatchInfo>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act & Assert - 期望 AggregateException，因为同步等待异步操作会包装异常
            var caughtException = Assert.Throws<AggregateException>(() => 
                _batchManager.CreateBatch(TestBatchId));
            
            Assert.That(caughtException.InnerException, Is.InstanceOf<Exception>());
            Assert.That(caughtException.InnerException.Message, Is.EqualTo("Database error"));
            Assert.That(_batchManager.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("创建批次失败"));
        }

        [Test]
        public void BatchId_ShouldFollowTimeFormat()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);

            _storageServiceMock
                .Setup(x => x.CreateBatchAsync(It.IsAny<BatchInfo>()))
                .Returns(Task.CompletedTask);

            // Act
            var batch = _batchManager.CreateBatch();

            // Assert
            Assert.That(batch.BatchId, Does.Match(@"^batch_\d{8}_\d{6}$"));
        }

        [Test]
        public void StartBatch_ShouldPreserveBatchName()
        {
            // Arrange
            _storageServiceMock
                .Setup(x => x.BatchExistsAsync(TestBatchId))
                .ReturnsAsync(true);

            // Act
            _batchManager.StartBatch(TestBatchId);

            // Assert
            Assert.That(_batchManager.Snapshot.BatchName, Is.EqualTo(TestBatchId));
        }
    }
}
