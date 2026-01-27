/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Batch;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.MES;
using SnVerify.Services.Storage;
using SnVerify.Properties;

namespace SnVerify.ViewModels
{
    /// <summary>
    /// 检验 UI 状态枚举（用于 UI 显示）
    /// </summary>
    public enum VerificationUiState
    {
        Idle,        // 空闲：等待扫码
        Processing,  // 检验中
        Pass,        // 通过
        Fail         // 失败
    }

    /// <summary>
    /// 主窗口 ViewModel，负责绑定 Snapshot 状态和命令（Phase2 闭环）
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IBatchManager _batchManager;
        private readonly IVerificationFlowServiceFactory _flowServiceFactory;
        private readonly ILoggingService _loggingService;
        private readonly IMESInterface _mesInterface;
        private readonly IStorageService _storageService;
        private readonly IAdbAccessService _adbAccessService;
        private readonly string _logDirectory;

        private IVerificationFlowService _verificationFlowService;
        private BatchSnapshot _batchSnapshot;
        private VerificationSnapshot _verificationSnapshot;
        private LoggingSnapshot _loggingSnapshot;
        private MESSnapshot _mesSnapshot;
        private string _scanInputText;
        private string _batchNameInput;
        private string _lastEndedBatchId;
        private bool _isSelfChecking;
        private Timer _snapshotUpdateTimer;
        private string _lastExportFolder; // 上次选择的导出文件夹路径

        /// <summary>
        /// 批次状态快照
        /// </summary>
        public BatchSnapshot BatchSnapshot
        {
            get => _batchSnapshot;
            internal set
            {
                if (_batchSnapshot != value)
                {
                    _batchSnapshot = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentBatchId));
                    OnPropertyChanged(nameof(IsBatchActive));
                    StartBatchCommand?.RaiseCanExecuteChanged();
                    EndBatchCommand?.RaiseCanExecuteChanged();
                    ExportCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 校验流程状态快照
        /// </summary>
        public VerificationSnapshot VerificationSnapshot
        {
            get => _verificationSnapshot;
            internal set
            {
                if (_verificationSnapshot != value)
                {
                    _verificationSnapshot = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSn));
                    OnPropertyChanged(nameof(DeviceSN));
                    OnPropertyChanged(nameof(IsProcessing));
                    OnPropertyChanged(nameof(LastResult));
                    OnPropertyChanged(nameof(FailReason));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ShowFailReason));
                    OnPropertyChanged(nameof(UiState));
                    StartVerifyCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 日志状态快照
        /// </summary>
        public LoggingSnapshot LoggingSnapshot
        {
            get => _loggingSnapshot;
            private set
            {
                if (_loggingSnapshot != value)
                {
                    _loggingSnapshot = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UiLogs));
                }
            }
        }

        /// <summary>
        /// MES 接口状态快照
        /// </summary>
        public MESSnapshot MESSnapshot
        {
            get => _mesSnapshot;
            private set
            {
                if (_mesSnapshot != value)
                {
                    _mesSnapshot = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 扫码输入文本。若包含 \r 或 \n，则提取首段为 SN 并自动触发检验（兼容扫码枪不发 Enter 的情况）。
        /// </summary>
        public string ScanInputText
        {
            get => _scanInputText;
            set
            {
                if (_scanInputText == value) return;
                if (value != null && (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0))
                {
                    var sn = (value.Split('\r', '\n')[0] ?? "").Trim();
                    if (!string.IsNullOrEmpty(sn))
                    {
                        _scanInputText = sn;
                        OnPropertyChanged(nameof(ScanInputText));
                        _ = HandleScanInputAsync(sn);
                        return;
                    }
                }
                _scanInputText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 当前批次 ID（用于显示）
        /// </summary>
        public string CurrentBatchId => BatchSnapshot?.BatchId ?? "未开始";

        /// <summary>
        /// 批次号输入：默认显示可编辑的批次名，开始批次后用于创建；有活动批次时禁用，结束后重置为新的默认名。
        /// </summary>
        public string BatchNameInput
        {
            get => _batchNameInput;
            set
            {
                if (_batchNameInput != value)
                {
                    _batchNameInput = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否批次活动（用于按钮状态）
        /// </summary>
        public bool IsBatchActive => BatchSnapshot?.IsActive ?? false;

        /// <summary>
        /// 当前 SN（用于显示）
        /// </summary>
        public string CurrentSn => VerificationSnapshot?.CurrentSn ?? "";

        /// <summary>
        /// 设备SN（用于显示）
        /// </summary>
        public string DeviceSN => VerificationSnapshot?.DeviceSN ?? "";

        /// <summary>
        /// 是否正在处理（用于显示）
        /// </summary>
        public bool IsProcessing => VerificationSnapshot?.IsProcessing ?? false;

        /// <summary>
        /// 是否正在自检（用于禁用自检按钮）
        /// </summary>
        public bool IsSelfChecking
        {
            get => _isSelfChecking;
            private set
            {
                if (_isSelfChecking != value)
                {
                    _isSelfChecking = value;
                    OnPropertyChanged();
                    SelfCheckCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 最后结果（用于显示）
        /// </summary>
        public string LastResult => VerificationSnapshot?.LastResult ?? "";

        /// <summary>
        /// 批次错误提示（临时显示，优先级高于 VerificationSnapshot.FailReason）
        /// </summary>
        private string _batchError;

        /// <summary>
        /// 失败原因（用于显示）
        /// </summary>
        public string FailReason
        {
            get
            {
                // 优先显示批次错误（如批次未激活）
                if (!string.IsNullOrEmpty(_batchError))
                    return _batchError;
                return VerificationSnapshot?.FailReason ?? "";
            }
        }

        /// <summary>
        /// 当前状态文本（用于显示：等待检验 / 正在检验... / PASS / FAIL）
        /// </summary>
        public string StatusText
        {
            get
            {
                if (VerificationSnapshot == null)
                    return "等待检验";
                if (VerificationSnapshot.IsProcessing)
                    return "正在检验...";
                if (!string.IsNullOrEmpty(VerificationSnapshot.LastResult))
                    return VerificationSnapshot.LastResult == "PASS" ? "PASS" : "FAIL";
                return "等待检验";
            }
        }

        /// <summary>
        /// 是否显示失败原因（用于 UI 绑定）
        /// </summary>
        public bool ShowFailReason => !string.IsNullOrEmpty(FailReason);

        /// <summary>
        /// 设置批次错误提示（用于显示批次未激活等错误）
        /// </summary>
        public void SetBatchError(string errorMessage)
        {
            _batchError = errorMessage;
            OnPropertyChanged(nameof(FailReason));
            OnPropertyChanged(nameof(ShowFailReason));
        }

        /// <summary>
        /// 清除批次错误提示
        /// </summary>
        public void ClearBatchError()
        {
            _batchError = null;
            OnPropertyChanged(nameof(FailReason));
            OnPropertyChanged(nameof(ShowFailReason));
        }

        /// <summary>
        /// UI 状态（用于立体结果卡片显示）
        /// </summary>
        public VerificationUiState UiState
        {
            get
            {
                if (VerificationSnapshot == null)
                    return VerificationUiState.Idle;
                if (VerificationSnapshot.IsProcessing)
                    return VerificationUiState.Processing;
                if (!string.IsNullOrEmpty(VerificationSnapshot.LastResult))
                    return VerificationSnapshot.LastResult == "PASS" ? VerificationUiState.Pass : VerificationUiState.Fail;
                return VerificationUiState.Idle;
            }
        }

        /// <summary>
        /// UI 日志列表（用于调试日志显示）
        /// </summary>
        public IReadOnlyList<string> UiLogs => LoggingSnapshot?.RecentMessages ?? new List<string>().AsReadOnly();

        /// <summary>
        /// 开始批次命令
        /// </summary>
        public RelayCommand StartBatchCommand { get; }

        /// <summary>
        /// 结束批次命令
        /// </summary>
        public RelayCommand EndBatchCommand { get; }

        /// <summary>
        /// 开始检验命令（手动触发，当扫码不含 \r\n 时使用）
        /// </summary>
        public RelayCommand StartVerifyCommand { get; }

        /// <summary>
        /// 自检命令（ADB / MES 可用性检测，不写校验表，结果输出到日志）
        /// </summary>
        public RelayCommand SelfCheckCommand { get; }

        /// <summary>
        /// 导出命令（导出当前结束批次的校验结果与日志）
        /// </summary>
        public RelayCommand ExportCommand { get; }

        /// <summary>
        /// 初始化主窗口 ViewModel
        /// </summary>
        public MainViewModel(
            IBatchManager batchManager,
            IVerificationFlowServiceFactory flowServiceFactory,
            ILoggingService loggingService,
            IMESInterface mesInterface,
            IStorageService storageService,
            IAdbAccessService adbAccessService,
            string logDirectory)
        {
            _batchManager = batchManager ?? throw new ArgumentNullException(nameof(batchManager));
            _flowServiceFactory = flowServiceFactory ?? throw new ArgumentNullException(nameof(flowServiceFactory));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _mesInterface = mesInterface ?? throw new ArgumentNullException(nameof(mesInterface));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
            _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));

            _verificationFlowService = _flowServiceFactory.Create("batch_idle");

            BatchSnapshot = _batchManager.Snapshot;
            VerificationSnapshot = _verificationFlowService.Snapshot;
            LoggingSnapshot = _loggingService.Snapshot;
            MESSnapshot = _mesInterface.Snapshot;

            _batchNameInput = "batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 从设置中读取上次选择的导出文件夹路径
            _lastExportFolder = Settings.Default.LastExportFolder;
            // 验证路径是否存在，如果不存在则清空
            if (!string.IsNullOrEmpty(_lastExportFolder) && !Directory.Exists(_lastExportFolder))
            {
                _lastExportFolder = null;
            }

            StartBatchCommand = new RelayCommand(async () => await StartBatchAsync(), () => !IsBatchActive);
            EndBatchCommand = new RelayCommand(async () => await EndBatchAsync(), () => IsBatchActive);
            ExportCommand = new RelayCommand(async () => await ExportAsync(), () => !IsBatchActive && !string.IsNullOrEmpty(_lastEndedBatchId));
            StartVerifyCommand = new RelayCommand(async () => await StartVerifyAsync(), () => !IsProcessing);
            SelfCheckCommand = new RelayCommand(async () => await SelfCheckAsync(), () => !IsSelfChecking);

            _snapshotUpdateTimer = new Timer(UpdateSnapshots, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// 更新所有快照（定时器回调）
        /// </summary>
        private void UpdateSnapshots(object state)
        {
            // 确保在 UI 线程上更新属性（WPF 数据绑定要求）
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                // 不在 UI 线程，封送到 UI 线程
                dispatcher.BeginInvoke(new System.Action(() =>
                {
                    UpdateSnapshotsInternal();
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                // 已在 UI 线程，直接更新
                UpdateSnapshotsInternal();
            }
        }

        /// <summary>
        /// 内部方法：实际更新快照（在 UI 线程上执行）
        /// </summary>
        private void UpdateSnapshotsInternal()
        {
            // 更新日志快照（最重要，用于 UI 日志显示）
            var newLoggingSnapshot = _loggingService.Snapshot;
            if (newLoggingSnapshot != _loggingSnapshot)
            {
                LoggingSnapshot = newLoggingSnapshot;
            }

            // 更新其他快照（可选，根据需要）
            var newVerificationSnapshot = _verificationFlowService.Snapshot;
            if (newVerificationSnapshot != _verificationSnapshot)
            {
                VerificationSnapshot = newVerificationSnapshot;
            }

            var newBatchSnapshot = _batchManager.Snapshot;
            if (newBatchSnapshot != _batchSnapshot)
            {
                BatchSnapshot = newBatchSnapshot;
            }
        }

        /// <summary>
        /// 开始批次：创建批次、按 batchId 创建校验流程服务、启动批次与日志；清零 _lastEndedBatchId。
        /// </summary>
        private async System.Threading.Tasks.Task StartBatchAsync()
        {
            try
            {
                var nameToUse = string.IsNullOrWhiteSpace(BatchNameInput) ? null : BatchNameInput.Trim();
                var batch = await System.Threading.Tasks.Task.Run(() =>
                {
                    var b = _batchManager.CreateBatch(nameToUse);
                    _batchManager.StartBatch(b.BatchId);
                    _loggingService.StartBatch(b.BatchId);
                    return b;
                });
                _verificationFlowService = _flowServiceFactory.Create(batch.BatchId);
                BatchNameInput = batch.BatchId;
                BatchSnapshot = _batchManager.Snapshot;
                VerificationSnapshot = _verificationFlowService.Snapshot;
                LoggingSnapshot = _loggingService.Snapshot;
                
                // 清除批次错误提示（批次已开始）
                ClearBatchError();
            }
            catch (Exception)
            {
                // 错误已由 Service 写日志；可在此补充 UI 提示
            }
        }

        /// <summary>
        /// 结束批次：记录 _lastEndedBatchId 供导出，结束批次与日志，重置批次名默认值。
        /// </summary>
        private async System.Threading.Tasks.Task EndBatchAsync()
        {
            try
            {
                _lastEndedBatchId = BatchSnapshot?.BatchId;
                _batchManager.EndBatch();
                _loggingService.EndBatch();
                BatchNameInput = "batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                BatchSnapshot = _batchManager.Snapshot;
                LoggingSnapshot = _loggingService.Snapshot;
                ExportCommand?.RaiseCanExecuteChanged();
            }
            catch (Exception)
            {
                // 错误已由 Service 写日志
            }
        }

        /// <summary>
        /// 导出：导出 _lastEndedBatchId 的校验结果与对应日志到用户选择的文件夹，并在日志区提示。
        /// </summary>
        private async System.Threading.Tasks.Task ExportAsync()
        {
            if (string.IsNullOrEmpty(_lastEndedBatchId)) return;
            
            // 让用户选择导出文件夹
            string selectedFolder = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "请选择导出文件夹";
                    dialog.ShowNewFolderButton = true;
                    
                    // 优先使用上次选择的路径，否则使用默认路径
                    if (!string.IsNullOrEmpty(_lastExportFolder) && Directory.Exists(_lastExportFolder))
                    {
                        dialog.SelectedPath = _lastExportFolder;
                    }
                    else
                    {
                        // 设置默认路径为日志目录下的 Export 子目录
                        var defaultPath = Path.Combine(_logDirectory, "Export", _lastEndedBatchId);
                        if (Directory.Exists(defaultPath))
                        {
                            dialog.SelectedPath = defaultPath;
                        }
                        else if (Directory.Exists(_logDirectory))
                        {
                            dialog.SelectedPath = _logDirectory;
                        }
                    }
                    
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        selectedFolder = dialog.SelectedPath;
                        // 保存用户选择的路径，供下次使用
                        _lastExportFolder = selectedFolder;
                        // 持久化到设置
                        Settings.Default.LastExportFolder = selectedFolder;
                        Settings.Default.Save();
                    }
                }
            });
            
            // 用户取消了文件夹选择，不执行导出
            if (string.IsNullOrEmpty(selectedFolder))
            {
                _loggingService.LogInfo("导出已取消");
                LoggingSnapshot = _loggingService.Snapshot;
                return;
            }
            
            try
            {
                // 创建导出目录（如果不存在）
                Directory.CreateDirectory(selectedFolder);
                
                // 复制日志文件到导出目录
                if (Directory.Exists(_logDirectory))
                {
                    foreach (var f in Directory.GetFiles(_logDirectory, "log_*"))
                    {
                        var name = Path.GetFileName(f);
                        if (name.StartsWith("log_" + _lastEndedBatchId + "_", StringComparison.OrdinalIgnoreCase))
                        {
                            try 
                            { 
                                File.Copy(f, Path.Combine(selectedFolder, name), true); 
                            } 
                            catch 
                            { 
                                // 忽略复制失败
                            }
                        }
                    }
                }
                
                // 导出批次结果 Excel 文件
                await _storageService.ExportBatchResultAsync(_lastEndedBatchId, selectedFolder);
                _loggingService.LogInfo("导出成功: " + selectedFolder);
                LoggingSnapshot = _loggingService.Snapshot;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("导出失败: " + ex.Message, ex);
                LoggingSnapshot = _loggingService.Snapshot;
            }
        }

        /// <summary>
        /// 开始检验：以当前扫码框内容触发校验（当扫码不含 \r\n 时须手动点击）。
        /// </summary>
        private async System.Threading.Tasks.Task StartVerifyAsync()
        {
            var sn = ScanInputText?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(sn))
            {
                _loggingService.LogWarning("扫码内容为空，请扫入 SN 后再执行检验");
                LoggingSnapshot = _loggingService.Snapshot;
                return;
            }
            await HandleScanInputAsync(sn);
        }

        /// <summary>
        /// 自检：ADB 设备/多机检测、MES 占位；结果只写日志，不写校验表、不改变批次。
        /// </summary>
        /// <remarks>
        /// 将所有 ADB 相关操作（CheckMultipleDevices、ReadDeviceSnAsync）都放到后台线程执行，避免阻塞 UI 线程。
        /// 自检期间禁用自检按钮，防止重复点击。
        /// </remarks>
        private async System.Threading.Tasks.Task SelfCheckAsync()
        {
            if (IsSelfChecking)
                return; // 防止重复调用

            try
            {
                IsSelfChecking = true; // 开始自检，禁用按钮
                _loggingService.LogInfo("自检开始");
                LoggingSnapshot = _loggingService.Snapshot;

                // 所有 ADB 操作都在后台线程执行，避免阻塞 UI
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    // 多设备检测（可能调用同步阻塞的 GetAwaiter().GetResult()）
                    if (_adbAccessService.CheckMultipleDevices(out var deviceIds) && deviceIds != null && deviceIds.Count > 1)
                    {
                        // 回到 UI 线程更新日志
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            _loggingService.LogWarning("自检: 检测到多台 ADB 设备: " + string.Join(", ", deviceIds));
                            LoggingSnapshot = _loggingService.Snapshot;
                        });
                    }

                    // ADB SN 读取（耗时操作）
                    var adbResult = await _adbAccessService.ReadDeviceSnAsync().ConfigureAwait(false);

                    // 回到 UI 线程更新日志
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (adbResult.IsSuccess)
                            _loggingService.LogInfo("自检: ADB 设备 SN 读取正常");
                        else
                            _loggingService.LogWarning("自检: ADB 无法读取 SN: " + (adbResult.ErrorReason ?? "未知"));
                        LoggingSnapshot = _loggingService.Snapshot;
                    });
                }).ConfigureAwait(true);

                // 回到 UI 线程后继续
                _loggingService.LogInfo("自检: MES 连通性检测未实现，请根据实际情况配置");
                _loggingService.LogInfo("自检结束");
                LoggingSnapshot = _loggingService.Snapshot;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("自检异常: " + ex.Message, ex);
                LoggingSnapshot = _loggingService.Snapshot;
            }
            finally
            {
                IsSelfChecking = false; // 自检结束，恢复按钮可用
            }
        }

        /// <summary>
        /// 处理扫码输入
        /// </summary>
        /// <param name="sn">扫码输入的 SN</param>
        /// <remarks>
        /// 触发机制：
        /// - Enter / \r / \n 视为一次扫码完成
        /// - Enter 之前的全部内容 = SN
        /// 
        /// 防御逻辑：
        /// - IsProcessing == true → 忽略输入
        /// - 空字符串 → 忽略
        /// - 批次未激活 → 拒绝（需要先开始批次）
        /// 
        /// 如果未来扫码枪不发 Enter，可以扩展为：
        /// - 监听 TextChanged 事件，检测特定字符（如 Tab）
        /// - 或使用定时器，检测输入停顿
        /// </remarks>
        public async System.Threading.Tasks.Task HandleScanInputAsync(string sn)
        {
            // 防御逻辑：空字符串或正在处理中，忽略输入
            if (string.IsNullOrWhiteSpace(sn))
                return;

            if (IsProcessing)
            {
                // 正在处理中，忽略新的扫码输入
                return;
            }

            // 防御逻辑：批次未激活时，拒绝扫码（需要先开始批次）
            if (!IsBatchActive)
            {
                // 可以在这里显示提示，或自动开始批次
                // 当前选择：拒绝扫码，要求先开始批次
                return;
            }

            try
            {
                // 触发校验流程
                await _verificationFlowService.StartVerificationAsync(sn.Trim());
                
                // 更新 Snapshot（轮询方式，实际应该通过事件）
                VerificationSnapshot = _verificationFlowService.Snapshot;
                
                // 等待校验完成（轮询 Snapshot）
                while (_verificationFlowService.Snapshot.IsProcessing)
                {
                    await System.Threading.Tasks.Task.Delay(100);
                    VerificationSnapshot = _verificationFlowService.Snapshot;
                    LoggingSnapshot = _loggingService.Snapshot; // 更新日志快照
                }
                
                // 校验完成，再次更新 Snapshot
                VerificationSnapshot = _verificationFlowService.Snapshot;
                LoggingSnapshot = _loggingService.Snapshot; // 更新日志快照
                
                // 清空输入框，准备下一次扫码
                ScanInputText = "";
            }
            catch (Exception ex)
            {
                // 异常处理：更新 Snapshot 显示错误
                // 注意：这里应该通过 Service 层的异常处理机制来更新 Snapshot
                // 当前简化处理，仅清空输入框
                ScanInputText = "";
            }
        }

        /// <summary>
        /// 属性变化事件
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 触发属性变化通知
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 简单的 RelayCommand 实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<System.Threading.Tasks.Task> _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Func<System.Threading.Tasks.Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public async void Execute(object parameter)
        {
            await _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            // 确保在 UI 线程上触发事件
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                // 不在 UI 线程，封送到 UI 线程
                dispatcher.BeginInvoke(new System.Action(() =>
                {
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                // 已在 UI 线程，直接调用
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
