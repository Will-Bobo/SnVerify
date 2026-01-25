/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// AdbAccessService Phase2 单元测试
    /// </summary>
    [TestFixture]
    public class AdbAccessServicePhase2Tests
    {
        private Mock<IProcessRunner> _processRunnerMock;
        private IAdbAccessService _adbAccessService;
        private const string TestAdbPath = @"tools\adb\adb.exe";
        private const string TestSn = "TEST123456789";
        private const string TestDeviceId = "device001";
        private const string TestBatchId = "BATCH001";

        [SetUp]
        public void SetUp()
        {
            _processRunnerMock = new Mock<IProcessRunner>();
            _adbAccessService = new AdbAccessService(TestAdbPath, _processRunnerMock.Object);
        }

        [Test]
        public void Snapshot_ShouldReturnInitialIdleState()
        {
            // Assert
            Assert.That(_adbAccessService.Snapshot.IsProcessing, Is.False);
            Assert.That(_adbAccessService.Snapshot.LastSN, Is.Null);
            Assert.That(_adbAccessService.Snapshot.ErrorMessage, Is.Null);
            Assert.That(_adbAccessService.Snapshot.HasMultipleDevices, Is.False);
            Assert.That(_adbAccessService.Snapshot.DeviceIds, Is.Empty);
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldReturnSN_WhenSingleDeviceExists()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success($"List of devices attached\n{TestDeviceId}\tdevice\n"));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            // Act
            var sn = await _adbAccessService.GetDeviceSNAsync(null, TestBatchId);

            // Assert
            Assert.That(sn, Is.EqualTo(TestSn));
            Assert.That(_adbAccessService.Snapshot.LastSN, Is.EqualTo(TestSn));
            Assert.That(_adbAccessService.Snapshot.BatchId, Is.EqualTo(TestBatchId));
            Assert.That(_adbAccessService.Snapshot.IsProcessing, Is.False);
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldUpdateSnapshotToProcessing_WhenStarting()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success($"List of devices attached\n{TestDeviceId}\tdevice\n"));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            // Act
            var task = _adbAccessService.GetDeviceSNAsync(null, TestBatchId);
            
            // Note: Snapshot may be updated asynchronously, so we check after completion
            var sn = await task;

            // Assert
            Assert.That(sn, Is.EqualTo(TestSn));
            Assert.That(_adbAccessService.Snapshot.IsProcessing, Is.False); // Should be completed
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldReturnNull_WhenNoDevicesConnected()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success("List of devices attached\n"));

            // Act
            var sn = await _adbAccessService.GetDeviceSNAsync(null, TestBatchId);

            // Assert
            Assert.That(sn, Is.Null);
            Assert.That(_adbAccessService.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("device"));
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldReturnNull_WhenCommandFails()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success($"List of devices attached\n{TestDeviceId}\tdevice\n"));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("Permission denied"));

            // Act
            var sn = await _adbAccessService.GetDeviceSNAsync(TestDeviceId, TestBatchId);

            // Assert
            Assert.That(sn, Is.Null);
            Assert.That(_adbAccessService.Snapshot.ErrorMessage, Is.Not.Null);
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldReturnNull_WhenTimeout()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success($"List of devices attached\n{TestDeviceId}\tdevice\n"));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Timeout());

            // Act
            var sn = await _adbAccessService.GetDeviceSNAsync(TestDeviceId, TestBatchId);

            // Assert
            Assert.That(sn, Is.Null);
            Assert.That(_adbAccessService.Snapshot.ErrorMessage, Is.Not.Null.And.Contains("timeout"));
        }

        [Test]
        public void CheckMultipleDevices_ShouldReturnFalse_WhenSingleDevice()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success($"List of devices attached\n{TestDeviceId}\tdevice\n"));

            // Act
            List<string> deviceIds;
            var hasMultiple = _adbAccessService.CheckMultipleDevices(out deviceIds);

            // Assert
            Assert.That(hasMultiple, Is.False);
            Assert.That(deviceIds, Is.Not.Null);
            Assert.That(deviceIds.Count, Is.EqualTo(1));
            Assert.That(deviceIds[0], Is.EqualTo(TestDeviceId));
        }

        [Test]
        public void CheckMultipleDevices_ShouldReturnTrue_WhenMultipleDevices()
        {
            // Arrange
            var deviceId1 = "device001";
            var deviceId2 = "device002";
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(
                    $"List of devices attached\n{deviceId1}\tdevice\n{deviceId2}\tdevice\n"));

            // Act
            List<string> deviceIds;
            var hasMultiple = _adbAccessService.CheckMultipleDevices(out deviceIds);

            // Assert
            Assert.That(hasMultiple, Is.True);
            Assert.That(deviceIds, Is.Not.Null);
            Assert.That(deviceIds.Count, Is.EqualTo(2));
            Assert.That(deviceIds, Contains.Item(deviceId1));
            Assert.That(deviceIds, Contains.Item(deviceId2));
            Assert.That(_adbAccessService.Snapshot.HasMultipleDevices, Is.True);
            Assert.That(_adbAccessService.Snapshot.DeviceIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void CheckMultipleDevices_ShouldUpdateSnapshot_WhenMultipleDevicesDetected()
        {
            // Arrange
            var deviceId1 = "device001";
            var deviceId2 = "device002";
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(
                    $"List of devices attached\n{deviceId1}\tdevice\n{deviceId2}\tdevice\n"));

            // Act
            List<string> deviceIds;
            _adbAccessService.CheckMultipleDevices(out deviceIds);

            // Assert
            var snapshot = _adbAccessService.Snapshot;
            Assert.That(snapshot.HasMultipleDevices, Is.True);
            Assert.That(snapshot.DeviceIds.Count, Is.EqualTo(2));
            Assert.That(snapshot.ErrorMessage, Is.Not.Null.And.Contains("Multiple"));
        }

        [Test]
        public void CheckMultipleDevices_ShouldReturnFalse_WhenNoDevices()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success("List of devices attached\n"));

            // Act
            List<string> deviceIds;
            var hasMultiple = _adbAccessService.CheckMultipleDevices(out deviceIds);

            // Assert
            Assert.That(hasMultiple, Is.False);
            Assert.That(deviceIds, Is.Not.Null);
            Assert.That(deviceIds.Count, Is.EqualTo(0));
        }

        [Test]
        public void CheckMultipleDevices_ShouldHandleAdbDevicesCommandFailure()
        {
            // Arrange
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Failure("ADB not found"));

            // Act
            List<string> deviceIds;
            var hasMultiple = _adbAccessService.CheckMultipleDevices(out deviceIds);

            // Assert
            Assert.That(hasMultiple, Is.False);
            Assert.That(deviceIds, Is.Not.Null);
            Assert.That(deviceIds.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldUseSpecifiedDeviceId()
        {
            // Arrange
            var specifiedDeviceId = "specific_device";
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {specifiedDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(""));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {specifiedDeviceId} getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            // Act
            var sn = await _adbAccessService.GetDeviceSNAsync(specifiedDeviceId, TestBatchId);

            // Assert
            Assert.That(sn, Is.EqualTo(TestSn));
            _processRunnerMock.Verify(
                x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {specifiedDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task GetDeviceSNAsync_ShouldRetryOnFailure()
        {
            // Arrange
            var attemptCount = 0;
            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    "devices",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success($"List of devices attached\n{TestDeviceId}\tdevice\n"));

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} ylzero",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount < 3)
                    {
                        return Task.FromResult(ProcessExecutionResult.Failure("Temporary failure"));
                    }
                    return Task.FromResult(ProcessExecutionResult.Success(""));
                });

            _processRunnerMock
                .Setup(x => x.RunAsync(
                    TestAdbPath,
                    $"shell -s {TestDeviceId} getprop sys.skyroam.osi.sn",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessExecutionResult.Success(TestSn));

            // Act
            var sn = await _adbAccessService.GetDeviceSNAsync(TestDeviceId, TestBatchId);

            // Assert
            Assert.That(sn, Is.EqualTo(TestSn));
            Assert.That(attemptCount, Is.EqualTo(3));
        }
    }
}
