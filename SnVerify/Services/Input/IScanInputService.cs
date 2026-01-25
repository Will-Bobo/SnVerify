/// <author>
/// AI Assistant
/// </author>

using System;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;

namespace SnVerify.Services.Input
{
    /// <summary>
    /// 扫码输入服务接口，负责接收字符流并识别完整 SN（Phase2 扩展）
    /// </summary>
    public interface IScanInputService
    {
        /// <summary>
        /// 当前扫码状态快照（只读）
        /// </summary>
        ScanSnapshot Snapshot { get; }

        /// <summary>
        /// SN 捕获事件，当检测到完整 SN（以 \r\n 结尾）时触发
        /// </summary>
        event EventHandler<SnCapturedEventArgs> SnCaptured;

        /// <summary>
        /// 接收单个字符输入（Phase1 兼容）
        /// </summary>
        /// <param name="inputChar">输入的字符</param>
        /// <remarks>
        /// 当检测到 \r\n 序列时，会触发 SnCaptured 事件
        /// SN 会自动转换为大写并去除首尾空格
        /// </remarks>
        void OnCharReceived(char inputChar);

        /// <summary>
        /// 接收完整 SN 输入（Phase2 新增）
        /// </summary>
        /// <param name="sn">扫码输入的完整 SN</param>
        /// <remarks>
        /// 原子触发机制：如果正在处理中，将忽略本次输入
        /// SN 会自动处理（转大写、去空格）
        /// 会触发 ProcessCoordinator 启动校验流程
        /// </remarks>
        Task OnScanInputAsync(string sn);

        /// <summary>
        /// 重置服务状态，清空当前缓存的字符
        /// </summary>
        /// <remarks>
        /// 调用后，之前未完成的输入将被丢弃，不会触发事件
        /// 同时会重置 ProcessCoordinator 状态
        /// </remarks>
        void Reset();
    }
}
