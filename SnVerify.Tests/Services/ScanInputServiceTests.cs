/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Services.Input;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// ScanInputService 单元测试
    /// </summary>
    [TestFixture]
    public class ScanInputServiceTests
    {
        private IScanInputService _scanInputService;
        private List<SnCapturedEventArgs> _capturedEvents;

        [SetUp]
        public void SetUp()
        {
            _scanInputService = new ScanInputService();
            _capturedEvents = new List<SnCapturedEventArgs>();
            _scanInputService.SnCaptured += (sender, e) => _capturedEvents.Add(e);
        }

        [TearDown]
        public void TearDown()
        {
            _scanInputService.SnCaptured -= (sender, e) => _capturedEvents.Add(e);
        }

        [Test]
        public void OnCharReceived_ShouldTriggerEvent_WhenReceivingCompleteSn()
        {
            // Arrange
            var inputChars = "ABC123\r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123"));
        }

        [Test]
        public void OnCharReceived_ShouldConvertToUpperCase()
        {
            // Arrange
            var inputChars = "abc123\r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123"));
        }

        [Test]
        public void OnCharReceived_ShouldTrimLeadingAndTrailingSpaces()
        {
            // Arrange
            var inputChars = "  ABC123  \r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123"));
        }

        [Test]
        public void OnCharReceived_ShouldNotTriggerEvent_WhenNoNewline()
        {
            // Arrange
            var inputChars = "ABC123".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(0));
        }

        [Test]
        public void OnCharReceived_ShouldNotTriggerEvent_WhenOnlyCarriageReturn()
        {
            // Arrange
            var inputChars = "ABC123\r".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(0));
        }

        [Test]
        public void OnCharReceived_ShouldTriggerMultipleEvents_WhenReceivingMultipleSns()
        {
            // Arrange
            var input1 = "ABC123\r\n".ToCharArray();
            var input2 = "XYZ789\r\n".ToCharArray();

            // Act
            foreach (var ch in input1)
            {
                _scanInputService.OnCharReceived(ch);
            }
            foreach (var ch in input2)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(2));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123"));
            Assert.That(_capturedEvents[1].Sn, Is.EqualTo("XYZ789"));
        }

        [Test]
        public void OnCharReceived_ShouldHandleMixedCaseAndSpaces()
        {
            // Arrange
            var inputChars = "  aBc123XyZ  \r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123XYZ"));
        }

        [Test]
        public void Reset_ShouldClearBuffer()
        {
            // Arrange
            var partialInput = "ABC123".ToCharArray();
            foreach (var ch in partialInput)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Act
            _scanInputService.Reset();
            var completeInput = "XYZ789\r\n".ToCharArray();
            foreach (var ch in completeInput)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("XYZ789"));
        }

        [Test]
        public void Reset_ShouldNotTriggerEvent_ForPartialInput()
        {
            // Arrange
            var partialInput = "ABC123".ToCharArray();
            foreach (var ch in partialInput)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Act
            _scanInputService.Reset();

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(0));
        }

        [Test]
        public void OnCharReceived_ShouldHandleEmptySn()
        {
            // Arrange
            var inputChars = "\r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo(string.Empty));
        }

        [Test]
        public void OnCharReceived_ShouldHandleOnlySpaces()
        {
            // Arrange
            var inputChars = "   \r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo(string.Empty));
        }

        [Test]
        public void OnCharReceived_ShouldHandleNewlineAfterCarriageReturn()
        {
            // Arrange
            // 测试 \r 后直接跟 \n（中间没有其他字符）
            var inputChars = "ABC123\r\nXYZ".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123"));
        }

        [Test]
        public void OnCharReceived_ShouldHandleMultipleCarriageReturnsBeforeNewline()
        {
            // Arrange
            // 多个 \r 后跟 \n，应该只触发一次
            var inputChars = "ABC123\r\r\n".ToCharArray();

            // Act
            foreach (var ch in inputChars)
            {
                _scanInputService.OnCharReceived(ch);
            }

            // Assert
            Assert.That(_capturedEvents.Count, Is.EqualTo(1));
            Assert.That(_capturedEvents[0].Sn, Is.EqualTo("ABC123"));
        }
    }
}
