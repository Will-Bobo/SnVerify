
/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// Phase 2.5 Step 6：TestRecord 模型，使用 INT SessionId 关联 TestSession。
/// Phase 3：在保持 TestSession 作为归属事实的前提下，增加 ChipId / WifiMac / 多版本字段以支撑扩展校验规则。
/// </remarks>

using System;

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// SN 粒度测试记录。通过 SessionId 关联 TestSession，Order 维度通过会话推导。
    /// Phase 3 起引入 ChipId / WifiMac / 多版本字段，用于扩展校验与追溯。
    /// </summary>
    public class TestRecord
    {
        /// <summary>
        /// 自增主键 Id。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 所属会话 Id（FK -> TestSession.Id）。
        /// </summary>
        public int SessionId { get; set; }

        /// <summary>
        /// 贴纸 SN（扫码输入）。
        /// </summary>
        public string StickerSN { get; set; }

        /// <summary>
        /// 设备 SN（从设备读取），允许为 null（例如 ADB 失败）。
        /// </summary>
        public string DeviceSN { get; set; }

        /// <summary>
        /// 设备 WiFi MAC 地址（来自 ADB 读取，Phase 3 引入）。
        /// </summary>
        public string WifiMac { get; set; }

        /// <summary>
        /// 芯片 ID（ChipId，来自 ADB 读取，Phase 3 引入）。
        /// </summary>
        public string ChipId { get; set; }

        /// <summary>
        /// 主板版本号（BoardVersion，来自 ADB 读取，Phase 3 引入）。
        /// </summary>
        public string BoardVersion { get; set; }

        /// <summary>
        /// 充电小板版本号（ChargeBoardVersion，来自 ADB 读取，Phase 3 引入）。
        /// </summary>
        public string ChargeBoardVersion { get; set; }

        /// <summary>
        /// 期望主板版本号（Phase3 引入；写录时从 VerificationParameter 固化，便于审计与导出）。
        /// </summary>
        public string ExpectedBoardVersion { get; set; }

        /// <summary>
        /// 期望充电板版本号（Phase3 引入；写录时从 VerificationParameter 固化，便于审计与导出）。
        /// </summary>
        public string ExpectedChargeBoardVersion { get; set; }

        /// <summary>
        /// 校验结果：PASS / FAIL / TIMEOUT。
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// 失败原因（可选）。
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// 校验完成时间。
        /// </summary>
        public DateTime VerifyTime { get; set; }

        /// <summary>
        /// 期望 Android 版本号（VersionMatch 或 SN+版本联合流程使用；允许为 null）。
        /// </summary>
        public string ExpectedVersion { get; set; }

        /// <summary>
        /// 实际 Android 版本号（VersionMatch 或 SN+版本联合流程使用；允许为 null）。
        /// </summary>
        public string ActualVersion { get; set; }
    }
}
