/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Adb
{
    /// <summary>
    /// ADB 访问服务接口，负责通过 ADB 命令读取设备 SN（Phase2 扩展）
    /// </summary>
    public interface IAdbAccessService
    {
        /// <summary>
        /// 当前 ADB 访问状态快照
        /// </summary>
        AdbSnapshot Snapshot { get; }

        /// <summary>
        /// 读取设备 SN（Phase1 兼容方法）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>SN 读取结果</returns>
        /// <remarks>
        /// 执行流程：
        /// 1. 执行 adb shell ylzero（打开访问权限）
        /// 2. 执行 adb shell getprop sys.skyroam.osi.sn（读取 SN）
        /// 3. 失败时最多重试 3 次，每次间隔 1 秒
        /// 4. 整个流程最大超时 10 秒
        /// </remarks>
        Task<AdbSnReadResult> ReadDeviceSnAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 读取设备信息（SN + Version），仅用于 UI「设备信息」按钮的临时调试接口。
        /// 不参与任何 SN 检验 / 自检 / MES 流程，可整体删除。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>设备信息读取结果</returns>
        Task<AdbDeviceInfoResult> ReadDeviceInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 按项目配置读取设备信息（DeviceInfo），用于 Phase3 SN 校验流程。
        /// 尝试一次性从 ADB 读取全部字段；若设备不支持，则允许分字段读取。
        /// 默认超时 10 秒，最多重试 3 次，每次间隔 1 秒。
        /// </summary>
        /// <param name="profile">项目配置概要（包含 ADB 读取命令等信息）</param>
        /// <returns>设备信息结构；读取失败时部分字段可能为 null。</returns>
        Task<DeviceInfo> ReadDeviceInfoAsync(ProjectProfile profile);

        /// <summary>
        /// 获取指定设备的 SN（Phase2 新增）
        /// </summary>
        /// <param name="deviceId">设备 ID（如果为 null，则使用第一个可用设备）</param>
        /// <param name="batchId">批次 ID（可选）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>设备 SN，失败时返回 null</returns>
        Task<string> GetDeviceSNAsync(string deviceId = null, string batchId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否存在多个设备（Phase2 新增）
        /// </summary>
        /// <param name="deviceIds">输出参数：检测到的设备 ID 列表</param>
        /// <returns>如果存在多个设备，返回 true；否则返回 false</returns>
        bool CheckMultipleDevices(out List<string> deviceIds);
    }
}

