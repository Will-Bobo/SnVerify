/// <author>
/// AI Assistant
/// </author>

namespace SnVerify.Domain.Models
{
    /// <summary>
    /// 项目配置概要，用于描述不同 Project 在 ADB 读取设备信息时的命令与映射规则。
    /// Phase3 先以最小字段落地，后续可根据文档扩展。
    /// </summary>
    public class ProjectProfile
    {
        /// <summary>
        /// 项目 ID（与 VerificationParameter.ProjectId 对齐）
        /// </summary>
        public string ProjectId { get; set; }

        /// <summary>
        /// 一次性读取所有设备信息的 ADB 命令（可选）。
        /// 为空时由具体 Service 采用内置默认命令或分字段命令。
        /// </summary>
        public string AggregateDeviceInfoCommand { get; set; }
    }
}

