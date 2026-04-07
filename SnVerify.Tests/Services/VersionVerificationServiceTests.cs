/// <author>AI Assistant</author>
/// <remarks>
/// Phase3：VersionVerificationService 三版本强校验单元测试。
/// </remarks>

using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;
using SnVerify.Services.Verification;

namespace SnVerify.Tests.Services
{
    /// <summary>
    /// VersionVerificationService 行为验证：
    /// - 三版本全部匹配 → PASS；
    /// - 任意版本不匹配 → FAIL；
    /// - Parameter 未配置 → FAIL。
    /// </summary>
    [TestFixture]
    public class VersionVerificationServiceTests
    {
        private IVersionVerificationService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new VersionVerificationService();
        }

        [Test]
        public async Task VerifyAsync_AllVersionsMatch_ReturnsPass()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter);

            Assert.That(success, Is.True);
            Assert.That(failReason, Is.Null);
        }

        [Test]
        public async Task VerifyAsync_AndroidVersionMismatch_ReturnsAndroidMismatch()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "AX",
                BoardVersion = "B1",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter);

            Assert.That(success, Is.False);
            Assert.That(failReason, Is.EqualTo(RuleFailReasonCodes.AndroidVersionMismatch));
        }

        [Test]
        public async Task VerifyAsync_BoardVersionMismatch_ReturnsBoardMismatch()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "BX",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter);

            Assert.That(success, Is.False);
            Assert.That(failReason, Is.EqualTo(RuleFailReasonCodes.BoardVersionMismatch));
        }

        [Test]
        public async Task VerifyAsync_ChargeBoardVersionMismatch_ReturnsChargeBoardMismatch()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "CX"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter);

            Assert.That(success, Is.False);
            Assert.That(failReason, Is.EqualTo(RuleFailReasonCodes.ChargeBoardVersionMismatch));
        }

        [Test]
        public async Task VerifyAsync_ParameterNotConfigured_ReturnsParameterNotConfigured()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "B1",
                ChargeBoardVersion = "C1"
            };

            var (success, failReason) = await _service.VerifyAsync(device, null);

            Assert.That(success, Is.False);
            Assert.That(failReason, Is.EqualTo(RuleFailReasonCodes.ParameterNotConfigured));
        }

        [Test]
        public async Task Should_Pass_When_Profile_Disables_Board_And_Charge_And_Stale_Expected_In_Parameter()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "",
                ChargeBoardVersion = ""
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B_STALE",
                ExpectedChargeBoardVersion = "C_STALE"
            };

            var profile = new ProductProfile
            {
                EnableBoardVersionCheck = false,
                EnableChargeBoardVersionCheck = false
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter, profile);

            Assert.That(success, Is.True);
            Assert.That(failReason, Is.Null);
        }

        [Test]
        public async Task Should_Fail_When_Profile_Disables_Board_But_Charge_Enabled_And_ChargeMismatch()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "ANY",
                ChargeBoardVersion = "CX"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var profile = new ProductProfile
            {
                EnableBoardVersionCheck = false,
                EnableChargeBoardVersionCheck = true
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter, profile);

            Assert.That(success, Is.False);
            Assert.That(failReason, Is.EqualTo(RuleFailReasonCodes.ChargeBoardVersionMismatch));
        }

        [Test]
        public async Task Should_Pass_When_Profile_Disables_Board_But_Charge_Enabled_And_BoardMismatch()
        {
            var device = new DeviceInfo
            {
                AndroidVersion = "A1",
                BoardVersion = "BX",
                ChargeBoardVersion = "C1"
            };

            var parameter = new VerificationParameter
            {
                ExpectedAndroidVersion = "A1",
                ExpectedBoardVersion = "B1",
                ExpectedChargeBoardVersion = "C1"
            };

            var profile = new ProductProfile
            {
                EnableBoardVersionCheck = false,
                EnableChargeBoardVersionCheck = true
            };

            var (success, failReason) = await _service.VerifyAsync(device, parameter, profile);

            Assert.That(success, Is.True);
            Assert.That(failReason, Is.Null);
        }
    }
}

