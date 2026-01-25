/// <author>
/// AI Assistant
/// </author>

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SnVerify.Domain.State;
using SnVerify.Services.Batch;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.MES;

namespace SnVerify.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel，负责绑定 Snapshot 状态和命令（Phase2 新增）
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IBatchManager _batchManager;
        private readonly IVerificationFlowService _verificationFlowService;
        private readonly ILoggingService _loggingService;
        private readonly IMESInterface _mesInterface;

        private BatchSnapshot _batchSnapshot;
        private VerificationSnapshot _verificationSnapshot;
        private LoggingSnapshot _loggingSnapshot;
        private MESSnapshot _mesSnapshot;
        private string _scanInputText;

        /// <summary>
        /// 批次状态快照
        /// </summary>
        public BatchSnapshot BatchSnapshot
        {
            get => _batchSnapshot;
            private set
            {
                if (_batchSnapshot != value)
                {
                    _batchSnapshot = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentBatchId));
                    OnPropertyChanged(nameof(IsBatchActive));
                    StartBatchCommand?.RaiseCanExecuteChanged();
                    EndBatchCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 校验流程状态快照
        /// </summary>
        public VerificationSnapshot VerificationSnapshot
        {
            get => _verificationSnapshot;
            private set
            {
                if (_verificationSnapshot != value)
                {
                    _verificationSnapshot = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSn));
                    OnPropertyChanged(nameof(IsProcessing));
                    OnPropertyChanged(nameof(LastResult));
                    OnPropertyChanged(nameof(FailReason));
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
        /// 扫码输入文本
        /// </summary>
        public string ScanInputText
        {
            get => _scanInputText;
            set
            {
                if (_scanInputText != value)
                {
                    _scanInputText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 当前批次 ID（用于显示）
        /// </summary>
        public string CurrentBatchId => BatchSnapshot?.BatchId ?? "未开始";

        /// <summary>
        /// 是否批次活动（用于按钮状态）
        /// </summary>
        public bool IsBatchActive => BatchSnapshot?.IsActive ?? false;

        /// <summary>
        /// 当前 SN（用于显示）
        /// </summary>
        public string CurrentSn => VerificationSnapshot?.CurrentSn ?? "";

        /// <summary>
        /// 是否正在处理（用于显示）
        /// </summary>
        public bool IsProcessing => VerificationSnapshot?.IsProcessing ?? false;

        /// <summary>
        /// 最后结果（用于显示）
        /// </summary>
        public string LastResult => VerificationSnapshot?.LastResult ?? "";

        /// <summary>
        /// 失败原因（用于显示）
        /// </summary>
        public string FailReason => VerificationSnapshot?.FailReason ?? "";

        /// <summary>
        /// 开始批次命令
        /// </summary>
        public RelayCommand StartBatchCommand { get; }

        /// <summary>
        /// 结束批次命令
        /// </summary>
        public RelayCommand EndBatchCommand { get; }

        /// <summary>
        /// 自动检验命令
        /// </summary>
        public RelayCommand AutoCheckCommand { get; }

        /// <summary>
        /// 初始化主窗口 ViewModel
        /// </summary>
        public MainViewModel(
            IBatchManager batchManager,
            IVerificationFlowService verificationFlowService,
            ILoggingService loggingService,
            IMESInterface mesInterface)
        {
            _batchManager = batchManager ?? throw new ArgumentNullException(nameof(batchManager));
            _verificationFlowService = verificationFlowService ?? throw new ArgumentNullException(nameof(verificationFlowService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _mesInterface = mesInterface ?? throw new ArgumentNullException(nameof(mesInterface));

            // 初始化快照
            BatchSnapshot = _batchManager.Snapshot;
            VerificationSnapshot = _verificationFlowService.Snapshot;
            LoggingSnapshot = _loggingService.Snapshot;
            MESSnapshot = _mesInterface.Snapshot;

            // 订阅快照变化事件
            // 注意：由于 Snapshot 是属性，我们需要定期轮询或使用事件
            // 这里简化处理，实际应该通过事件机制更新

            // 创建命令
            StartBatchCommand = new RelayCommand(
                async () => await StartBatchAsync(),
                () => !IsBatchActive);

            EndBatchCommand = new RelayCommand(
                async () => await EndBatchAsync(),
                () => IsBatchActive);

            AutoCheckCommand = new RelayCommand(
                async () => await AutoCheckAsync(),
                () => !IsProcessing);
        }

        /// <summary>
        /// 开始批次
        /// </summary>
        private async System.Threading.Tasks.Task StartBatchAsync()
        {
            try
            {
                var batch = _batchManager.CreateBatch();
                _batchManager.StartBatch(batch.BatchId);
                _loggingService.StartBatch(batch.BatchId);
                BatchSnapshot = _batchManager.Snapshot;
            }
            catch (Exception ex)
            {
                // 错误处理（可以显示消息框或更新状态）
            }
        }

        /// <summary>
        /// 结束批次
        /// </summary>
        private async System.Threading.Tasks.Task EndBatchAsync()
        {
            try
            {
                _batchManager.EndBatch();
                _loggingService.EndBatch();
                BatchSnapshot = _batchManager.Snapshot;
            }
            catch (Exception ex)
            {
                // 错误处理
            }
        }

        /// <summary>
        /// 自动检验（测试按钮）
        /// </summary>
        private async System.Threading.Tasks.Task AutoCheckAsync()
        {
            try
            {
                // 触发一个测试 SN 的校验流程
                await _verificationFlowService.StartVerificationAsync("TEST_SN");
                VerificationSnapshot = _verificationFlowService.Snapshot;
            }
            catch (Exception ex)
            {
                // 错误处理
            }
        }

        /// <summary>
        /// 处理扫码输入
        /// </summary>
        public async System.Threading.Tasks.Task HandleScanInputAsync(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn) || IsProcessing)
                return;

            try
            {
                await _verificationFlowService.StartVerificationAsync(sn);
                VerificationSnapshot = _verificationFlowService.Snapshot;
                ScanInputText = ""; // 清空输入框
            }
            catch (Exception ex)
            {
                // 错误处理
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
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
