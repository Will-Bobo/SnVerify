/// <author>
/// AI Assistant
/// </author>

using System;
using System.Text;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Coordination;

namespace SnVerify.Services.Input
{
    /// <summary>
    /// 扫码输入服务实现，负责接收字符流并识别完整 SN（Phase2 扩展）
    /// </summary>
    public class ScanInputService : IScanInputService
    {
        private readonly StringBuilder _buffer;
        private readonly object _lockObject = new object();
        private readonly IProcessCoordinator _processCoordinator;
        private readonly string _batchId;
        private ScanSnapshot _snapshot;

        /// <summary>
        /// 当前扫码状态快照（只读）
        /// </summary>
        public ScanSnapshot Snapshot
        {
            get
            {
                lock (_lockObject)
                {
                    return _snapshot ?? ScanSnapshot.Idle(_batchId);
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _snapshot = value;
                }
            }
        }

        /// <summary>
        /// SN 捕获事件
        /// </summary>
        public event EventHandler<SnCapturedEventArgs> SnCaptured;

        /// <summary>
        /// 初始化扫码输入服务（Phase1 兼容构造函数）
        /// </summary>
        public ScanInputService()
        {
            _buffer = new StringBuilder();
            _snapshot = ScanSnapshot.Idle();
        }

        /// <summary>
        /// 初始化扫码输入服务（Phase2 构造函数，集成 ProcessCoordinator）
        /// </summary>
        /// <param name="processCoordinator">流程编排服务</param>
        /// <param name="batchId">当前批次 ID</param>
        public ScanInputService(IProcessCoordinator processCoordinator, string batchId = null)
        {
            _processCoordinator = processCoordinator ?? throw new ArgumentNullException(nameof(processCoordinator));
            _batchId = batchId;
            _buffer = new StringBuilder();
            _snapshot = ScanSnapshot.Idle(_batchId);

            // 订阅 ProcessCoordinator 的状态变化
            if (_processCoordinator != null)
            {
                _processCoordinator.SnapshotChanged += OnProcessCoordinatorSnapshotChanged;
            }
        }

        /// <summary>
        /// 接收单个字符输入（Phase1 兼容方法）
        /// </summary>
        public void OnCharReceived(char inputChar)
        {
            lock (_lockObject)
            {
                // 检测 \r\n 序列
                if (inputChar == '\n')
                {
                    // 检查缓冲区末尾是否有 \r
                    if (_buffer.Length > 0 && _buffer[_buffer.Length - 1] == '\r')
                    {
                        // 移除最后的 \r（可能不止一个）
                        while (_buffer.Length > 0 && _buffer[_buffer.Length - 1] == '\r')
                        {
                            _buffer.Length--;
                        }
                        
                        // 提取 SN 并处理
                        var rawSn = _buffer.ToString();
                        var processedSn = ProcessSn(rawSn);
                        
                        // 清空缓冲区
                        _buffer.Clear();
                        
                        // 触发事件（Phase1 兼容）
                        OnSnCaptured(processedSn);
                        return;
                    }
                }
                
                // 处理 \r：如果缓冲区末尾已经是 \r，就不重复添加
                if (inputChar == '\r')
                {
                    if (_buffer.Length == 0 || _buffer[_buffer.Length - 1] != '\r')
                    {
                        _buffer.Append(inputChar);
                    }
                }
                else
                {
                    // 累积其他字符到缓冲区
                    _buffer.Append(inputChar);
                }
            }
        }

        /// <summary>
        /// 接收完整 SN 输入（Phase2 新增方法）
        /// </summary>
        public async Task OnScanInputAsync(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
            {
                UpdateSnapshot(ScanSnapshot.Error(null, "SN cannot be empty", _batchId));
                return;
            }

            // 处理 SN（转大写、去空格）
            var processedSn = ProcessSn(sn);
            if (string.IsNullOrWhiteSpace(processedSn))
            {
                UpdateSnapshot(ScanSnapshot.Error(sn, "SN is empty after processing", _batchId));
                return;
            }

            // 原子锁定检查
            bool shouldProcess = false;
            lock (_lockObject)
            {
                // 检查本地状态和 ProcessCoordinator 状态
                bool coordinatorProcessing = _processCoordinator != null && _processCoordinator.Snapshot.IsProcessing;
                if (!_snapshot.IsProcessing && !coordinatorProcessing)
                {
                    shouldProcess = true;
                    UpdateSnapshot(ScanSnapshot.Processing(processedSn, _batchId));
                }
            }

            if (!shouldProcess)
            {
                // 正在处理中，忽略本次输入
                return;
            }

            try
            {
                // 触发 ProcessCoordinator 流程
                if (_processCoordinator != null)
                {
                    await _processCoordinator.StartVerificationAsync(processedSn);
                }
                else
                {
                    // 如果没有 ProcessCoordinator，只触发事件（Phase1 兼容）
                    OnSnCaptured(processedSn);
                }
            }
            catch (Exception ex)
            {
                UpdateSnapshot(ScanSnapshot.Error(processedSn, $"Error: {ex.Message}", _batchId));
            }
        }

        /// <summary>
        /// 重置服务状态，清空当前缓存的字符
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                _buffer.Clear();
                UpdateSnapshot(ScanSnapshot.Idle(_batchId));
            }

            // 重置 ProcessCoordinator
            if (_processCoordinator != null)
            {
                _processCoordinator.Reset();
            }
        }

        /// <summary>
        /// 处理 SN：转大写并去除首尾空格
        /// </summary>
        private string ProcessSn(string rawSn)
        {
            if (string.IsNullOrEmpty(rawSn))
            {
                return string.Empty;
            }

            return rawSn.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// 触发 SN 捕获事件（Phase1 兼容）
        /// </summary>
        protected virtual void OnSnCaptured(string sn)
        {
            var handler = SnCaptured;
            if (handler != null)
            {
                var args = new SnCapturedEventArgs(sn);
                handler(this, args);
            }
        }

        /// <summary>
        /// 处理 ProcessCoordinator 状态变化
        /// </summary>
        private void OnProcessCoordinatorSnapshotChanged(object sender, VerificationSnapshot coordinatorSnapshot)
        {
            lock (_lockObject)
            {
                // 同步 ProcessCoordinator 的状态到 ScanSnapshot
                if (coordinatorSnapshot.IsProcessing)
                {
                    // 保持 Processing 状态
                    if (_snapshot == null || !_snapshot.IsProcessing || _snapshot.LastScanSN != coordinatorSnapshot.CurrentSn)
                    {
                        UpdateSnapshot(ScanSnapshot.Processing(coordinatorSnapshot.CurrentSn, _batchId));
                    }
                }
                else
                {
                    // 流程完成，更新快照
                    if (_snapshot != null && _snapshot.IsProcessing)
                    {
                        var errorMessage = coordinatorSnapshot.LastResult == "FAIL" || coordinatorSnapshot.LastResult == "TIMEOUT"
                            ? coordinatorSnapshot.FailReason
                            : null;
                        UpdateSnapshot(ScanSnapshot.Error(_snapshot.LastScanSN, errorMessage, _batchId));
                    }
                }
            }
        }

        /// <summary>
        /// 更新快照
        /// </summary>
        private void UpdateSnapshot(ScanSnapshot newSnapshot)
        {
            lock (_lockObject)
            {
                _snapshot = newSnapshot;
            }
        }
    }
}
