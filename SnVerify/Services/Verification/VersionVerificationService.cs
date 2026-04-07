/// <author>AI Assistant</author>
/// <remarks>
/// Phase3：三版本强校验服务实现。
/// 仅负责版本字段比较与 FailReason 生成，不包含流程控制与持久化逻辑。
/// </remarks>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Rules;

namespace SnVerify.Services.Verification
{
    /// <summary>
    /// 默认的三版本强校验服务实现。
    /// </summary>
    public class VersionVerificationService : IVersionVerificationService
    {
        /// <inheritdoc />
        public Task<(bool success, string failReason)> VerifyAsync(
            DeviceInfo deviceInfo,
            VerificationParameter parameter,
            ProductProfile profile = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (parameter == null)
            {
                return Task.FromResult((false, RuleFailReasonCodes.ParameterNotConfigured));
            }

            var androidExpected = parameter.ExpectedAndroidVersion?.Trim();
            var boardExpected = parameter.ExpectedBoardVersion?.Trim();
            var chargeExpected = parameter.ExpectedChargeBoardVersion?.Trim();

            var androidActual = (deviceInfo?.AndroidVersion ?? string.Empty).Trim();
            var boardActual = (deviceInfo?.BoardVersion ?? string.Empty).Trim();
            var chargeActual = (deviceInfo?.ChargeBoardVersion ?? string.Empty).Trim();

            var checkBoard = profile == null || profile.EnableBoardVersionCheck;
            var checkCharge = profile == null || profile.EnableChargeBoardVersionCheck;

            // 校验顺序：Android → Board → ChargeBoard，任一失败即终止。

            if (!string.IsNullOrWhiteSpace(androidExpected) &&
                !string.Equals(androidExpected, androidActual, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult((false, RuleFailReasonCodes.AndroidVersionMismatch));
            }

            if (checkBoard &&
                !string.IsNullOrWhiteSpace(boardExpected) &&
                !string.Equals(boardExpected, boardActual, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult((false, RuleFailReasonCodes.BoardVersionMismatch));
            }

            if (checkCharge &&
                !string.IsNullOrWhiteSpace(chargeExpected) &&
                !string.Equals(chargeExpected, chargeActual, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult((false, RuleFailReasonCodes.ChargeBoardVersionMismatch));
            }

            // 所有已配置字段均匹配（或均未配置）→ PASS。
            return Task.FromResult((true, (string)null));
        }
    }
}

