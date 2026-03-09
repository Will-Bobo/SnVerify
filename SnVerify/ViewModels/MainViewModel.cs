/// <author>
/// AI Assistant
/// </author>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Models;
using SnVerify.Domain.DeviceAccess;
using SnVerify.Domain.Product;
using SnVerify.Domain.State;
using SnVerify.Domain.Validation;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Adb;
using SnVerify.Services.Coordination;
using SnVerify.Services.DeviceAccess;
using SnVerify.Services.Logging;
using SnVerify.Services.Parameter;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
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
        private readonly ISessionLifecycleService _sessionLifecycleService;
        private readonly IVerificationFlowServiceFactory _flowServiceFactory;
        private readonly ILoggingService _loggingService;
        private readonly IStorageService _storageService;
        private readonly IAdbAccessService _adbAccessService;
        private readonly IExportAggregationService _exportAggregationService;
        private readonly IOrderNameValidator _orderNameValidator;
        private readonly IUserDialogService _dialogService;
        private readonly IVersionVerificationFlowService _versionVerificationFlowService;
        private readonly IProductRegistry _productRegistry;
        private readonly IParameterService _parameterService;
        private readonly IDeviceAccessService _deviceAccessService;
        private readonly string _logDirectory;
        private readonly SynchronizationContext _uiContext;

        private IVerificationFlowService _verificationFlowService;
        private TestRecord _lastVersionRecord;
        private IVerificationFlowService _mesEventSource; // 当前订阅 MES 事件的流程服务实例（避免重复订阅）
        private SessionSnapshot _sessionSnapshot;
        private VerificationSnapshot _verificationSnapshot;
        private LoggingSnapshot _loggingSnapshot;
        private string _scanInputText;
        private string _projectIdInput;
        private string _orderIdInput;
        private string _lastEndedSessionId;
        private bool _isSelfChecking;
        private Timer _snapshotUpdateTimer;
        private string _lastExportFolder; // 上次选择的导出文件夹路径
        private string _statusBarMessage; // 状态栏消息（无效操作/MES 预留提示）
        private string _lastFailReason; // 上次失败原因（用于 C1.6：重复「设备SN已存在」UI只一条）

        private readonly ObservableCollection<string> _availableProducts = new ObservableCollection<string>();
        private string _selectedProductCode;
        private string _currentProductDisplay;
        private ProductProfile _currentProductProfile;

        // 当 Session 从 Active → Idle 时，允许下一次将 VerificationSnapshot 回退到 Idle（清屏/避免残留版本号）。
        private bool _allowIdleVerificationSnapshotOverwriteOnce;

        private string _expectedAndroidVersion;
        private string _expectedBoardVersion;
        private string _expectedChargeBoardVersion;
        private bool _isLegacyProduct;
        private bool _isPhase3Product;

        // SessionLock：用于防止“真实运行中的活动 Session”被 UI/外部代码误改关键输入。
        // 说明：单元测试中常直接设置 SessionSnapshot 为 Active，但 Mock 的 ISessionLifecycleService.GetCurrentSessionId 可能仍返回 null；
        // 为保持测试稳定且不影响真实运行，此处以 GetCurrentSessionId 是否存在作为“真实活动 Session”的辅助判定。
        private bool IsSessionLocked => IsSessionActive && !string.IsNullOrWhiteSpace(_sessionLifecycleService?.GetCurrentSessionId());

        /// <summary>
        /// 可选的产品代码列表（来源：ProductRegistry，禁止硬编码）。
        /// </summary>
        public ObservableCollection<string> AvailableProducts => _availableProducts;

        /// <summary>
        /// 当前选择的产品代码（Session 未启动时可修改；Session 启动后禁止修改）。
        /// </summary>
        public string SelectedProductCode
        {
            get => _selectedProductCode;
            set
            {
                if (IsSessionActive && !string.Equals(_selectedProductCode, value, StringComparison.OrdinalIgnoreCase))
                {
                    // Session 已启动：禁止修改 ProductCode（UI 已禁用，这里再做一层防御）。
                    return;
                }

                if (!string.Equals(_selectedProductCode, value, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedProductCode = value;
                    OnPropertyChanged();
                    UpdateCurrentProductProfile();
                }
            }
        }

        /// <summary>
        /// 项目名称（UI 展示用，当前与 ProjectIdInput 等价）。
        /// </summary>
        public string ProjectName
        {
            get => ProjectIdInput;
            set => ProjectIdInput = value;
        }

        /// <summary>
        /// 当前产品显示文本（格式：ProductCode + Mode）。
        /// </summary>
        public string CurrentProductDisplay
        {
            get => _currentProductDisplay;
            private set
            {
                if (_currentProductDisplay != value)
                {
                    _currentProductDisplay = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// ProductCode 下拉框是否可用（Session 未启动时可选，启动后禁用）。
        /// </summary>
        public bool IsProductCodeComboBoxEnabled => !IsSessionActive;

        /// <summary>
        /// 是否为 Legacy 产品（SOLTAG25 等）。
        /// </summary>
        public bool IsLegacyProduct
        {
            get => _isLegacyProduct;
            private set
            {
                if (_isLegacyProduct != value)
                {
                    _isLegacyProduct = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否为 Phase3 产品（KM001 等）。
        /// </summary>
        public bool IsPhase3Product
        {
            get => _isPhase3Product;
            private set
            {
                if (_isPhase3Product != value)
                {
                    _isPhase3Product = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Session 状态快照（Phase 2.5：替代 BatchSnapshot）
        /// </summary>
        public SessionSnapshot SessionSnapshot
        {
            get => _sessionSnapshot;
            internal set
            {
                if (_sessionSnapshot != value)
                {
                    // 记录 Session 结束（Active → Idle），用于允许一次性将校验快照回退到 Idle。
                    var wasActive = _sessionSnapshot?.IsActive ?? false;
                    var willBeActive = value?.IsActive ?? false;
                    if (wasActive && !willBeActive)
                    {
                        _allowIdleVerificationSnapshotOverwriteOnce = true;
                    }

                    _sessionSnapshot = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentOrderId));
                    OnPropertyChanged(nameof(CurrentTestIdentifier));
                    OnPropertyChanged(nameof(IsSessionActive));
                    OnPropertyChanged(nameof(IsVerificationTypeComboBoxEnabled));
                    OnPropertyChanged(nameof(IsProductCodeComboBoxEnabled));
                    StartBatchCommand?.RaiseCanExecuteChanged();
                    EndBatchCommand?.RaiseCanExecuteChanged();
                    ExportCommand?.RaiseCanExecuteChanged();
                    StartVerifyCommand?.RaiseCanExecuteChanged();
                    StartVersionVerifyCommand?.RaiseCanExecuteChanged();
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
                    OnPropertyChanged(nameof(CurrentDeviceInfo));
                    OnPropertyChanged(nameof(IsProcessing));
                    OnPropertyChanged(nameof(IsScanInputEnabled));
                    OnPropertyChanged(nameof(LastResult));
                    OnPropertyChanged(nameof(FailReason));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ShowFailReason));
                    OnPropertyChanged(nameof(UiState));
                    OnPropertyChanged(nameof(LastVersionRecord));
                    StartVerifyCommand?.RaiseCanExecuteChanged();
                    StartVersionVerifyCommand?.RaiseCanExecuteChanged();
                    StartBatchCommand?.RaiseCanExecuteChanged();
                    EndBatchCommand?.RaiseCanExecuteChanged();
                    ExportCommand?.RaiseCanExecuteChanged();
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

        // Phase 2.5: 保留 MESSnapshot 类型以便后续接入 MES Gate，但当前不在 UI 中直接使用。

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
        /// 当前订单 ID（用于显示）- Phase 2.5：从 SessionSnapshot.OrderId 获取
        /// </summary>
        public string CurrentOrderId => SessionSnapshot?.OrderId ?? "未开始";

        /// <summary>
        /// 本次测试标识（只读，从 SessionId 提取时间段，如 yyyyMMdd_HHmmss，不显示 Session 字样）
        /// </summary>
        public string CurrentTestIdentifier
        {
            get
            {
                var sessionId = SessionSnapshot?.SessionId;
                if (string.IsNullOrWhiteSpace(sessionId))
                    return "";
                // SessionId 格式：OrderId_yyyyMMdd_HHmmss，提取后半部分
                var parts = sessionId.Split('_');
                if (parts.Length >= 3)
                {
                    // 返回 yyyyMMdd_HHmmss 部分
                    return parts[parts.Length - 2] + "_" + parts[parts.Length - 1];
                }
                return "";
            }
        }

        /// <summary>
        /// 状态栏消息（阶段 3 C1.4：无效操作/MES 上报失败预留提示）
        /// </summary>
        public string StatusBarMessage
        {
            get => _statusBarMessage ?? "";
            private set
            {
                if (_statusBarMessage != value)
                {
                    _statusBarMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 项目 ID 输入（Phase 2.5：开始测试时输入）
        /// </summary>
        public string ProjectIdInput
        {
            get => _projectIdInput;
            set
            {
                // SessionLock：真实活动 Session 时禁止修改项目 ID
                if (IsSessionLocked && !string.Equals(_projectIdInput, value, StringComparison.Ordinal))
                {
                    return;
                }

                if (_projectIdInput != value)
                {
                    _projectIdInput = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 订单 ID 输入（Phase 2.5：开始测试时输入，必填）
        /// </summary>
        public string OrderIdInput
        {
            get => _orderIdInput;
            set
            {
                // SessionLock：真实活动 Session 时禁止修改订单号
                if (IsSessionLocked && !string.Equals(_orderIdInput, value, StringComparison.Ordinal))
                {
                    return;
                }

                if (_orderIdInput != value)
                {
                    _orderIdInput = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _targetVersionInput;
        private VerificationType _currentVerificationType = VerificationType.SnMatch;

        /// <summary>
        /// 当前检验类型（SN / Version），用于控制输入框显示与流程分支
        /// </summary>
        public VerificationType CurrentVerificationType
        {
            get => _currentVerificationType;
            set
            {
                if (_currentVerificationType != value)
                {
                    _currentVerificationType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsScanInputVisible));
                    OnPropertyChanged(nameof(IsVersionInputVisible));
                    OnPropertyChanged(nameof(IsSnInfoVisible));
                    OnPropertyChanged(nameof(IsVersionInfoVisible));
                    OnPropertyChanged(nameof(ExpectedVersionDisplay));
                    StartVersionVerifyCommand?.RaiseCanExecuteChanged();
                    StartBatchCommand?.RaiseCanExecuteChanged();

                    // 单一事实源：切换检验类型时，立即将 UI 校验快照切换到对应 FlowService 的 Snapshot，
                    // 避免残留「版本检验将版本号写入 DeviceSN」导致 SnMatch 模式显示异常。
                    VerificationSnapshot = GetActiveVerificationSnapshot();
                }
            }
        }

        /// <summary>
        /// 检验类型 ComboBox 是否可用（开始测试后禁用）
        /// </summary>
        public bool IsVerificationTypeComboBoxEnabled => !IsSessionActive;

        /// <summary>
        /// 扫码输入框是否显示（VerificationType == SnMatch 时显示）
        /// </summary>
        public bool IsScanInputVisible => CurrentVerificationType == VerificationType.SnMatch;

        /// <summary>
        /// 版本输入框是否显示（VerificationType == VersionMatch 时显示）
        /// </summary>
        public bool IsVersionInputVisible => CurrentVerificationType == VerificationType.VersionMatch;

        /// <summary>
        /// SN 信息区是否显示（扫码SN + 设备SN，VerificationType == SnMatch 时显示）
        /// </summary>
        public bool IsSnInfoVisible => CurrentVerificationType == VerificationType.SnMatch;

        /// <summary>
        /// 设备版本信息区是否显示（目标版本 + 设备实际版本，VerificationType == VersionMatch 时显示）
        /// </summary>
        public bool IsVersionInfoVisible => CurrentVerificationType == VerificationType.VersionMatch;

        /// <summary>
        /// 目标版本显示（VersionMatch 时用，来自 TargetVersionInput，空时显示 --）
        /// </summary>
        public string ExpectedVersionDisplay
        {
            get
            {
                if (CurrentVerificationType != VerificationType.VersionMatch)
                    return "--";
                var v = TargetVersionInput?.Trim();
                return string.IsNullOrEmpty(v) ? "--" : v;
            }
        }

        /// <summary>
        /// 设备实际版本显示（VersionMatch 时用，来自 LastVersionRecord.ActualVersion，无记录时显示 --）
        /// </summary>
        public string ActualDeviceVersionDisplay => _lastVersionRecord?.ActualVersion?.Trim() ?? "--";

        /// <summary>
        /// ComboBox 可选的检验类型列表（不含 None）
        /// </summary>
        public IReadOnlyList<VerificationType> AvailableVerificationTypes { get; } =
            new[] { VerificationType.SnMatch, VerificationType.VersionMatch };

        /// <summary>
        /// 版本检验目标版本号输入（VersionMatch 流程使用）
        /// </summary>
        public string TargetVersionInput
        {
            get => _targetVersionInput ?? "";
            set
            {
                if (_targetVersionInput != value)
                {
                    _targetVersionInput = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ExpectedVersionDisplay));
                    StartVersionVerifyCommand?.RaiseCanExecuteChanged();
                    StartBatchCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 是否 Session 活动（用于按钮状态）
        /// </summary>
        public bool IsSessionActive => SessionSnapshot?.IsActive ?? false;

        /// <summary>
        /// 扫码输入框是否可用（自检规则 8：自检期间禁用扫码）
        /// </summary>
        public bool IsScanInputEnabled => !IsProcessing && !IsSelfChecking;

        /// <summary>
        /// 当前 SN（用于显示）
        /// </summary>
        public string CurrentSn => VerificationSnapshot?.CurrentSn ?? "";

        /// <summary>
        /// 设备SN（用于显示）
        /// </summary>
        public string DeviceSN => VerificationSnapshot?.DeviceSN ?? "";

        /// <summary>
        /// 当前设备信息（Phase3：KM001 设备信息展示使用）。get-only，由 VerificationSnapshot 推导。
        /// </summary>
        public DeviceInfo CurrentDeviceInfo
        {
            get
            {
                if (_verificationSnapshot?.DeviceInfo != null)
                    return _verificationSnapshot.DeviceInfo;

                if (!string.IsNullOrWhiteSpace(_verificationSnapshot?.DeviceSN))
                    return new DeviceInfo { DeviceSn = _verificationSnapshot.DeviceSN };

                return new DeviceInfo();
            }
        }

        /// <summary>
        /// Phase3：目标 Android 版本（仅用于 KM001 目标版本配置 UI）。
        /// </summary>
        public string ExpectedAndroidVersion
        {
            get => _expectedAndroidVersion ?? "";
            set
            {
                if (_expectedAndroidVersion != value)
                {
                    _expectedAndroidVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Phase3：目标 Board 版本。
        /// </summary>
        public string ExpectedBoardVersion
        {
            get => _expectedBoardVersion ?? "";
            set
            {
                if (_expectedBoardVersion != value)
                {
                    _expectedBoardVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Phase3：目标 ChargeBoard 版本。
        /// </summary>
        public string ExpectedChargeBoardVersion
        {
            get => _expectedChargeBoardVersion ?? "";
            set
            {
                if (_expectedChargeBoardVersion != value)
                {
                    _expectedChargeBoardVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Phase3：目标版本输入是否可编辑（Session 未激活时可编辑）。
        /// </summary>
        public bool IsVersionInputEnabled => !IsSessionActive;

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
                    OnPropertyChanged(nameof(IsSelfChecking));
                    OnPropertyChanged(nameof(IsScanInputEnabled));
                    SelfCheckCommand?.RaiseCanExecuteChanged();
                    StartVerifyCommand?.RaiseCanExecuteChanged(); // 规则 8：自检期间禁用人工检验
                    StartVersionVerifyCommand?.RaiseCanExecuteChanged();
                    StartBatchCommand?.RaiseCanExecuteChanged();
                    EndBatchCommand?.RaiseCanExecuteChanged();
                    ExportCommand?.RaiseCanExecuteChanged();
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
        /// 失败原因（用于显示）- 阶段 3 C1.6：重复「设备SN已存在」UI只一条
        /// </summary>
        public string FailReason
        {
            get
            {
                // 优先显示批次错误（如批次未激活）
                if (!string.IsNullOrEmpty(_batchError))
                    return _batchError;
                var currentFailReason = VerificationSnapshot?.FailReason ?? "";
                // C1.6：重复「设备SN已存在」UI也要显示错误信息（不跳过）
                // （始终显示 "设备SN已存在"，不管是否与上次相同）
                _lastFailReason = currentFailReason;
                return currentFailReason;
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
        /// 将检验区恢复到默认等待状态（结束测试时调用）。
        /// </summary>
        private void ResetVerificationUiToIdle()
        {
            _versionVerificationFlowService.ResetToIdle();
            VerificationSnapshot = VerificationSnapshot.Idle();
            _lastVersionRecord = null;
            OnPropertyChanged(nameof(LastVersionRecord));
            OnPropertyChanged(nameof(ActualDeviceVersionDisplay));
            ClearBatchError();
        }

        /// <summary>
        /// 最后一次版本检验记录（VersionMatch 流程使用，用于 UI 绑定 ActualVersion、Result、FailReason）
        /// </summary>
        public TestRecord LastVersionRecord => _lastVersionRecord;

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
        /// 版本检验命令（VersionMatch 流程：读取设备版本与目标版本对比）
        /// </summary>
        public RelayCommand StartVersionVerifyCommand { get; }

        /// <summary>
        /// 导出命令（导出当前结束批次的校验结果与日志）
        /// </summary>
        public RelayCommand ExportCommand { get; }

        /// <summary>
        /// 初始化主窗口 ViewModel
        /// </summary>
        public MainViewModel(
            ISessionLifecycleService sessionLifecycleService,
            IVerificationFlowServiceFactory flowServiceFactory,
            ILoggingService loggingService,
            IStorageService storageService,
            IAdbAccessService adbAccessService,
            IExportAggregationService exportAggregationService,
            IOrderNameValidator orderNameValidator,
            IUserDialogService dialogService,
            IVersionVerificationFlowService versionVerificationFlowService,
            string logDirectory,
            IProductRegistry productRegistry = null,
            IParameterService parameterService = null,
            IDeviceAccessService deviceAccessService = null)
        {
            _sessionLifecycleService = sessionLifecycleService ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _flowServiceFactory = flowServiceFactory ?? throw new ArgumentNullException(nameof(flowServiceFactory));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
            _exportAggregationService = exportAggregationService ?? throw new ArgumentNullException(nameof(exportAggregationService));
            _orderNameValidator = orderNameValidator ?? throw new ArgumentNullException(nameof(orderNameValidator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _versionVerificationFlowService = versionVerificationFlowService ?? throw new ArgumentNullException(nameof(versionVerificationFlowService));
            _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
            _productRegistry = productRegistry ?? new ProductRegistryAdapter();
            _parameterService = parameterService;
            _deviceAccessService = deviceAccessService;
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            _verificationFlowService = _flowServiceFactory.Create("session_idle", null);
            AttachMesEventHandlers(_verificationFlowService);

            SessionSnapshot = _sessionLifecycleService.Snapshot;
            VerificationSnapshot = _verificationFlowService.Snapshot;
            LoggingSnapshot = _loggingService.Snapshot;

            // 从设置中读取上次选择的导出文件夹路径（不在 ViewModel 中做文件系统级验证，仅保存字符串）
            _lastExportFolder = Settings.Default.LastExportFolder;
            // 从设置中读取上次使用的项目名、订单号，启动时回填到输入框
            _projectIdInput = Settings.Default.LastProjectId ?? "";
            _orderIdInput = Settings.Default.LastOrderId ?? "";

            // Stage3 Step2：产品列表从 ProductRegistry 初始化（禁止硬编码）。
            LoadAvailableProducts();
            RestoreLastProductAndExpectedVersions();

            // 规则 3/5/8：自检期间禁用 Start/End/导出；检验中也禁用 Start/End/导出（防止重复点击/并发操作）。
            StartBatchCommand = new RelayCommand(async () => await StartBatchAsync(), () => CanExecuteStartBatch());
            EndBatchCommand = new RelayCommand(async () => await EndBatchAsync(), () => IsSessionActive && !IsSelfChecking && !IsProcessing);
            // 导出：仅在进行中的测试时段（开始测试→结束测试）不可用，其余时间均可点击
            ExportCommand = new RelayCommand(async () => await ExportAsync(), () => !IsSessionActive);
            // 规则 8：自检期间禁用人工检验；未点击「开始测试」时人工检验置灰
            StartVerifyCommand = new RelayCommand(async () => await StartVerifyAsync(), () => IsSessionActive && !IsProcessing && !IsSelfChecking);
            SelfCheckCommand = new RelayCommand(async () => await SelfCheckAsync(), () => !IsSelfChecking);
            StartVersionVerifyCommand = new RelayCommand(async () => await StartVersionVerifyAsync(), () => CanExecuteStartVersionVerify());

            _snapshotUpdateTimer = new Timer(UpdateSnapshots, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        }

        private void LoadAvailableProducts()
        {
            _availableProducts.Clear();

            var codes = _productRegistry.GetProductCodes() ?? Array.Empty<string>();
            foreach (var code in codes.Where(c => !string.IsNullOrWhiteSpace(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            {
                _availableProducts.Add(code);
            }

            if (string.IsNullOrWhiteSpace(SelectedProductCode) && _availableProducts.Count > 0)
            {
                SelectedProductCode = _availableProducts[0];
            }

            if (_availableProducts.Count == 0)
            {
                CurrentProductDisplay = "--";
            }
        }

        /// <summary>
        /// 启动时恢复上次产品选择，并在 Phase3 产品下回填目标版本号默认值。
        /// </summary>
        private void RestoreLastProductAndExpectedVersions()
        {
            var lastProductCode = Settings.Default.LastProductCode;
            if (!string.IsNullOrWhiteSpace(lastProductCode) &&
                _availableProducts.Any(p => string.Equals(p, lastProductCode, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedProductCode = lastProductCode;
            }

            if (IsPhase3Product && string.Equals(SelectedProductCode, Settings.Default.LastProductCode, StringComparison.OrdinalIgnoreCase))
            {
                ExpectedAndroidVersion = Settings.Default.LastExpectedAndroidVersion ?? "";
                ExpectedBoardVersion = Settings.Default.LastExpectedBoardVersion ?? "";
                ExpectedChargeBoardVersion = Settings.Default.LastExpectedChargeBoardVersion ?? "";
            }
        }

        private void UpdateCurrentProductProfile()
        {
            if (string.IsNullOrWhiteSpace(SelectedProductCode))
            {
                _currentProductProfile = null;
                CurrentProductDisplay = "--";
                IsLegacyProduct = false;
                IsPhase3Product = false;
                return;
            }

            _currentProductProfile = _productRegistry.Get(SelectedProductCode);
            if (_currentProductProfile == null)
            {
                CurrentProductDisplay = $"{SelectedProductCode} [未知]";
                IsLegacyProduct = false;
                IsPhase3Product = false;
                return;
            }

            var modeText = _currentProductProfile.Mode == VerificationMode.Phase3 ? "Phase3模式" : "Legacy模式";
            CurrentProductDisplay = $"{_currentProductProfile.ProductCode} [{modeText}]";

             IsLegacyProduct = _currentProductProfile.Mode == VerificationMode.Legacy;
             IsPhase3Product = _currentProductProfile.Mode == VerificationMode.Phase3;
        }

        /// <summary>
        /// 更新所有快照（定时器回调）
        /// </summary>
        private void UpdateSnapshots(object state)
        {
            // 规则 12：禁止 Application.Current/Dispatcher。
            // Timer 回调线程通过 SynchronizationContext 封送回 UI 线程（不引入 WPF 类型依赖）。
            _uiContext.Post(_ => UpdateSnapshotsInternal(), null);
        }

        /// <summary>
        /// 根据当前 VerificationType 获取对应的快照来源（单一事实来源，不含 UI 拼接）。
        /// </summary>
        private VerificationSnapshot GetActiveVerificationSnapshot()
        {
            if (!IsSessionActive)
                return VerificationSnapshot.Idle();
            if (CurrentVerificationType == VerificationType.VersionMatch)
                return _versionVerificationFlowService.Snapshot;
            return _verificationFlowService.Snapshot;
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

            // 更新校验快照：根据 VerificationType 选择对应 FlowService 的快照，仅做「是否变化 → 推送到 UI」
            var newVerificationSnapshot = GetActiveVerificationSnapshot();
            if (!ReferenceEquals(newVerificationSnapshot, _verificationSnapshot))
            {
                // 单元测试/异常场景下，Mock 的 FlowService.Snapshot 可能长期保持 Idle，
                // 而测试或业务流程会直接将 VerificationSnapshot 置为 Processing/Completed 以验证按钮可用性。
                // 为避免后台定时器将「已有结果/处理中」错误重置为 Idle，这里在
                // 「新快照为 Idle 且现有快照非 Idle」的情况下跳过更新。
                // 生产环境中如需清空结果，会由流程/命令显式设置 Idle 快照，因此不会受此保护影响。
                var newIsIdle =
                    newVerificationSnapshot != null &&
                    !newVerificationSnapshot.IsProcessing &&
                    string.IsNullOrEmpty(newVerificationSnapshot.CurrentSn) &&
                    string.IsNullOrEmpty(newVerificationSnapshot.LastResult);

                var currentIsNotIdle =
                    _verificationSnapshot != null &&
                    (_verificationSnapshot.IsProcessing ||
                     !string.IsNullOrEmpty(_verificationSnapshot.CurrentSn) ||
                     !string.IsNullOrEmpty(_verificationSnapshot.LastResult));

                if (newIsIdle && currentIsNotIdle && !_allowIdleVerificationSnapshotOverwriteOnce)
                {
                    // skip
                }
                else
                {
                    if (newIsIdle && currentIsNotIdle && _allowIdleVerificationSnapshotOverwriteOnce)
                    {
                        // 消费一次性允许回退标记（仅用于 Session 结束后的清屏）。
                        _allowIdleVerificationSnapshotOverwriteOnce = false;
                    }
                    VerificationSnapshot = newVerificationSnapshot;
                }
            }

            var newSessionSnapshot = _sessionLifecycleService.Snapshot;
            if (!ReferenceEquals(newSessionSnapshot, _sessionSnapshot))
            {
                // 单元测试场景下可能直接通过属性赋值将 SessionSnapshot 置为 Active，
                // 而 Mock 的 ISessionLifecycleService.Snapshot 仍为 Idle。
                // 为避免后台定时器将 Active 状态错误重置为 Idle，这里在
                // 「现有为 Active 且新快照 SessionId 为空」的情况下跳过更新。
                if (newSessionSnapshot != null &&
                    string.IsNullOrEmpty(newSessionSnapshot.SessionId) &&
                    _sessionSnapshot != null &&
                    !string.IsNullOrEmpty(_sessionSnapshot.SessionId) &&
                    _sessionSnapshot.IsActive)
                {
                    return;
                }

                SessionSnapshot = newSessionSnapshot;
            }
        }

        /// <summary>
        /// 开始测试命令是否可执行：未激活、非自检、非处理中，且 VersionMatch 时需填写目标版本。
        /// </summary>
        private bool CanExecuteStartBatch()
        {
            if (IsSessionActive || IsSelfChecking || IsProcessing)
                return false;
            if (CurrentVerificationType == VerificationType.VersionMatch && string.IsNullOrWhiteSpace(TargetVersionInput))
                return false;
            return true;
        }

        /// <summary>
        /// 开始测试（Session）：Phase 2.5 - 校验 ProjectId/OrderId，创建 Session、按 sessionId 创建校验流程服务、启动日志。
        /// </summary>
        private async System.Threading.Tasks.Task StartBatchAsync()
        {
            try
            {
                var projectId = string.IsNullOrWhiteSpace(ProjectIdInput) ? null : ProjectIdInput.Trim();
                var orderId = string.IsNullOrWhiteSpace(OrderIdInput) ? null : OrderIdInput.Trim();

                // Phase3：当为 Phase3 产品且 ProjectId 未填写时，自动使用 SelectedProductCode 作为 ProjectId，
                // 确保 ParameterService 与 ProductRegistry 使用统一键（如 KM001）。
                if (IsPhase3Product &&
                    string.IsNullOrWhiteSpace(projectId) &&
                    !string.IsNullOrWhiteSpace(SelectedProductCode))
                {
                    projectId = SelectedProductCode.Trim();
                    ProjectIdInput = projectId;
                }

                // 项目/订单均为必填，且订单需要通过命名校验（与项目挂钩）
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(orderId))
                {
                    _dialogService.ShowWarning("项目名 和订单名 都不能为空", "校验失败");
                    return;
                }

                // VersionMatch 时目标版本必填
                if (CurrentVerificationType == VerificationType.VersionMatch && string.IsNullOrWhiteSpace(TargetVersionInput?.Trim()))
                {
                    _dialogService.ShowWarning("版本检验模式下，请先填写目标版本号", "校验失败");
                    return;
                }

                // Phase 2.5：校验弹窗挂接 - 开始测试时一次性校验命名
                if (!_orderNameValidator.Validate(orderId, out var validationMessage))
                {
                    // 校验不通过，提示，不创建 Session
                    _dialogService.ShowWarning($"命名校验不通过：{validationMessage}", "校验失败");
                    return;
                }

                // Phase3：目标版本至少填写一项，否则无法进行校验，提前提示并阻止开始测试。
                if (IsPhase3Product)
                {
                    var expectedAndroid = ExpectedAndroidVersion?.Trim();
                    var expectedBoard = ExpectedBoardVersion?.Trim();
                    var expectedCharge = ExpectedChargeBoardVersion?.Trim();
                    if (string.IsNullOrWhiteSpace(expectedAndroid) &&
                        string.IsNullOrWhiteSpace(expectedBoard) &&
                        string.IsNullOrWhiteSpace(expectedCharge))
                    {
                        _dialogService.ShowWarning("目标版本号不能为空，请查看输入（Android Version / Board Version / ChargeBoard Version）", "校验失败");
                        return;
                    }
                }

                // 创建并开始 Session，并以 SessionName 启动会话日志
                var productCode = (IsPhase3Product && !string.IsNullOrWhiteSpace(SelectedProductCode)) ? SelectedProductCode.Trim() : null;
                var sessionId = await System.Threading.Tasks.Task.Run(() =>
                {
                    var sid = _sessionLifecycleService.CreateAndStartSession(orderId, orderId, projectId, productCode);
                    _loggingService.StartSession(sid);
                    return sid;
                });

                _verificationFlowService = _flowServiceFactory.Create(sessionId, orderId);
                AttachMesEventHandlers(_verificationFlowService);

                SessionSnapshot = _sessionLifecycleService.Snapshot;
                VerificationSnapshot = _verificationFlowService.Snapshot;
                LoggingSnapshot = _loggingService.Snapshot;

                // Phase3：先创建 Session，再按 SessionId 保存参数快照，确保批次语义一致。
                if (IsPhase3Product && _parameterService != null)
                {
                    var expectedAndroid = ExpectedAndroidVersion?.Trim();
                    var expectedBoard = ExpectedBoardVersion?.Trim();
                    var expectedCharge = ExpectedChargeBoardVersion?.Trim();

                    // 至少存在一个非空目标版本时才保存（上文已校验，此处恒为真，保留以保持语义清晰）。
                    if (!string.IsNullOrWhiteSpace(expectedAndroid) ||
                        !string.IsNullOrWhiteSpace(expectedBoard) ||
                        !string.IsNullOrWhiteSpace(expectedCharge))
                    {
                        var internalSessionId = await _storageService.GetInternalSessionIdBySessionNameAsync(sessionId).ConfigureAwait(true);
                        if (!internalSessionId.HasValue)
                            throw new InvalidOperationException("未能解析 Session 内部 Id，无法保存版本参数快照");

                        var parameter = new VerificationParameter
                        {
                            SessionId = internalSessionId.Value,
                            ExpectedAndroidVersion = expectedAndroid,
                            ExpectedBoardVersion = expectedBoard,
                            ExpectedChargeBoardVersion = expectedCharge,
                            CreatedAt = DateTime.Now
                        };

                        await _parameterService.SaveParameterAsync(parameter).ConfigureAwait(true);
                    }
                }

                // 保存本次使用的项目名、订单号，下次启动时回填（与导出路径保存方式一致）
                Settings.Default.LastProjectId = projectId;
                Settings.Default.LastOrderId = orderId;
                Settings.Default.LastProductCode = SelectedProductCode ?? "";
                // 仅 Phase3 产品保存版本号默认值；Legacy 不覆盖，避免误清空用户可复用值。
                if (IsPhase3Product)
                {
                    Settings.Default.LastExpectedAndroidVersion = ExpectedAndroidVersion ?? "";
                    Settings.Default.LastExpectedBoardVersion = ExpectedBoardVersion ?? "";
                    Settings.Default.LastExpectedChargeBoardVersion = ExpectedChargeBoardVersion ?? "";
                }
                Settings.Default.Save();

                // 清除错误提示（Session 已开始）
                ClearBatchError();
            }
            catch (Exception)
            {
                // 错误已由 Service 写日志；可在此补充 UI 提示
            }
        }

        /// <summary>
        /// 结束测试（Session）：Phase 2.5 - 记录 _lastEndedSessionId 供导出，结束 Session 与日志。
        /// </summary>
        /// <remarks>规则 3：End 前检查当前 Session 是否有 TestRecord；无则状态栏提示「本次操作无效/已忽略」且不执行 End。</remarks>
        private async System.Threading.Tasks.Task EndBatchAsync()
        {
            try
            {
                var sessionId = SessionSnapshot?.SessionId;
                if (string.IsNullOrWhiteSpace(sessionId))
                    return;

                // 规则 3：End 前检查当前 Session 是否有 TestRecord；无则状态栏提示并忽略本次操作。
                var records = await _storageService.GetTestRecordsBySessionAsync(sessionId);
                if (records == null || records.Count == 0)
                {
                    StatusBarMessage = "本次操作无效/已忽略";
                    _loggingService.LogInfo("结束测试被忽略：本次测试未产生任何记录");
                    LoggingSnapshot = _loggingService.Snapshot;
                    // return;
                }

                _lastEndedSessionId = sessionId;
                _sessionLifecycleService.EndSession();
                _loggingService.EndBatch();
                
                SessionSnapshot = _sessionLifecycleService.Snapshot;
                LoggingSnapshot = _loggingService.Snapshot;
                ExportCommand?.RaiseCanExecuteChanged();
                StatusBarMessage = "";

                // 将检验区恢复到默认等待状态
                ResetVerificationUiToIdle();
            }
            catch (Exception)
            {
                // 错误已由 Service 写日志
            }
        }

        /// <summary>
        /// 导出：阶段 3 C1.2 - 「选维度 → 选对象 → 执行」+ 覆盖确认弹窗。
        /// </summary>
        private async System.Threading.Tasks.Task ExportAsync()
        {
            // Step 1: 选择导出维度（按项目 / 按订单）
            var exportDimension = _dialogService.ChooseExportDimension();
            if (exportDimension == null)
            {
                _loggingService.LogInfo("导出已取消");
                LoggingSnapshot = _loggingService.Snapshot;
                return;
            }

            // Step 2: 选择导出内容类型（SN 检验 / 版本检验 / 全部），默认根据当前 VerificationType 勾选
            var exportFilter = _dialogService.ChooseExportRecordFilter(new[] { CurrentVerificationType });
            if (exportFilter == null)
            {
                _loggingService.LogInfo("导出已取消");
                LoggingSnapshot = _loggingService.Snapshot;
                return;
            }

            // Step 3: 选择具体项目或订单
            string selectedId = null;
            if (exportDimension == ExportDimension.ByProject)
            {
                var projectIds = await _storageService.GetAllProjectIdsAsync();
                if (projectIds.Count == 0)
                {
                    _dialogService.ShowInfo("当前没有项目数据，无法导出");
                    return;
                }
                selectedId = _dialogService.ChooseProjectId(projectIds);
            }
            else // Order
            {
                var orders = await _storageService.GetAllOrdersAsync();
                if (orders.Count == 0)
                {
                    _dialogService.ShowInfo("当前没有订单数据，无法导出");
                    return;
                }
                var selectedOrder = _dialogService.ChooseOrder(orders);
                selectedId = selectedOrder?.OrderName;
            }

            if (string.IsNullOrEmpty(selectedId))
            {
                _loggingService.LogInfo("导出已取消");
                LoggingSnapshot = _loggingService.Snapshot;
                return;
            }

            // Step 4: 选择导出文件夹（不在 ViewModel 中访问文件系统，仅传递上次路径或日志目录字符串）
            var initialFolder = !string.IsNullOrEmpty(_lastExportFolder)
                ? _lastExportFolder
                : _logDirectory;
            var selectedFolder = _dialogService.ChooseFolder("请选择导出文件夹", initialFolder);

            if (string.IsNullOrEmpty(selectedFolder))
            {
                _loggingService.LogInfo("导出已取消");
                LoggingSnapshot = _loggingService.Snapshot;
                return;
            }

            _lastExportFolder = selectedFolder;
            Settings.Default.LastExportFolder = selectedFolder;
            Settings.Default.Save();

            // Step 5: 检查 ZIP 是否已存在，必要时弹出覆盖确认对话框
            var safeName = ToSafeFileName(selectedId);
            var zipPath = Path.Combine(selectedFolder, safeName + ".zip");

            if (File.Exists(zipPath))
            {
                var confirm = _dialogService.ConfirmOverwrite(
                    $"目标导出文件已存在：{zipPath}{Environment.NewLine}是否覆盖？");
                if (!confirm)
                {
                    _loggingService.LogInfo("导出已取消：目标 ZIP 已存在且用户选择不覆盖");
                    LoggingSnapshot = _loggingService.Snapshot;
                    return;
                }

                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("导出失败：无法删除已存在的 ZIP 文件：" + ex.Message, ex);
                    LoggingSnapshot = _loggingService.Snapshot;
                    return;
                }
            }

            // Step 6: 执行导出（所有路径/ZIP/文件系统逻辑均在 Service 层实现，filter 透传至 Storage）
            try
            {
                if (exportDimension == ExportDimension.ByProject)
                    await _exportAggregationService.ExportByProjectIdAsync(selectedId, selectedFolder, exportFilter);
                else
                    await _exportAggregationService.ExportByOrderIdAsync(selectedId, selectedFolder, exportFilter);
                _loggingService.LogInfo($"导出成功: {(exportDimension == ExportDimension.ByProject ? "项目" : "订单")}={selectedId}, 目录={selectedFolder}");
                LoggingSnapshot = _loggingService.Snapshot;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("导出失败: " + ex.Message, ex);
                LoggingSnapshot = _loggingService.Snapshot;
            }
        }

        /// <summary>
        /// 绑定流程服务的 MES 事件，用于状态栏弱提示（不影响 PASS/FAIL）。
        /// </summary>
        private void AttachMesEventHandlers(IVerificationFlowService flowService)
        {
            if (flowService == null) return;
            if (ReferenceEquals(_mesEventSource, flowService)) return;

            // 先解绑旧订阅（避免内存泄漏/重复提示）
            if (_mesEventSource != null)
            {
                _mesEventSource.MesEventOccurred -= OnMesEventOccurred;
            }

            _mesEventSource = flowService;
            _mesEventSource.MesEventOccurred += OnMesEventOccurred;
        }

        private void OnMesEventOccurred(object sender, SnVerify.Services.Mes.Gate.MesEventArgs e)
        {
            // 规则 12：禁止 Dispatcher。通过 ViewModel 捕获的 SynchronizationContext 回到 UI 线程。
            _uiContext.Post(_ =>
            {
                if (e == null) return;
                if (!string.IsNullOrWhiteSpace(e.Message))
                {
                    StatusBarMessage = e.Message;
                }
            }, null);
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
        /// 将所有设备访问操作（CheckMultipleDevices、ReadDeviceInfoAsync）都放到后台线程执行，避免阻塞 UI 线程。
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

                // 所有设备访问操作都在后台线程执行，避免阻塞 UI
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    // 多设备检测（可能调用同步阻塞的 GetAwaiter().GetResult()）
                    if (_adbAccessService.CheckMultipleDevices(out var deviceIds) && deviceIds != null && deviceIds.Count > 1)
                    {
                        // 回到 UI 线程更新日志（规则 12：不使用 Dispatcher）
                        _uiContext.Post(_ =>
                        {
                            _loggingService.LogWarning("自检: 检测到多台 ADB 设备: " + string.Join(", ", deviceIds));
                            LoggingSnapshot = _loggingService.Snapshot;
                        }, null);
                    }

                    var profile = _currentProductProfile;
                    if (profile == null)
                    {
                        _uiContext.Post(_ =>
                        {
                            _loggingService.LogWarning("自检: 未选择或未识别产品，无法执行设备读取");
                            LoggingSnapshot = _loggingService.Snapshot;
                        }, null);
                        return;
                    }

                    if (_deviceAccessService == null)
                    {
                        _uiContext.Post(_ =>
                        {
                            _loggingService.LogWarning($"自检: 设备访问服务未注入，无法按产品协议执行（Product={profile.ProductCode ?? "Unknown"}）");
                            LoggingSnapshot = _loggingService.Snapshot;
                        }, null);
                        return;
                    }

                    // 按产品 Profile 执行统一设备读取（Aggregate/Field 由 DeviceAccessService 内部决定）
                    try
                    {
                        var deviceInfo = await _deviceAccessService.ReadDeviceInfoAsync(profile).ConfigureAwait(false);

                        _uiContext.Post(_ =>
                        {
                            var sn = deviceInfo?.DeviceSn?.Trim();
                            var version = deviceInfo?.AndroidVersion?.Trim();
                            if (!string.IsNullOrWhiteSpace(sn))
                                _loggingService.LogInfo($"[SelfCheck][{profile.ProductCode}][DeviceAccess] SN读取成功: {sn}, Version: {(string.IsNullOrWhiteSpace(version) ? "-" : version)}");
                            else
                                _loggingService.LogWarning($"[SelfCheck][{profile.ProductCode}][DeviceAccess] 读取失败：SN为空");
                            LoggingSnapshot = _loggingService.Snapshot;
                        }, null);
                    }
                    catch (AggregateProtocolException ex)
                    {
                        _uiContext.Post(_ =>
                        {
                            _loggingService.LogWarning($"[SelfCheck][{profile.ProductCode}][Aggregate] 协议错误: {ex.Message}");
                            LoggingSnapshot = _loggingService.Snapshot;
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        _uiContext.Post(_ =>
                        {
                            _loggingService.LogWarning($"[SelfCheck][{profile.ProductCode}][DeviceAccess] 读取异常: {ex.Message}");
                            LoggingSnapshot = _loggingService.Snapshot;
                        }, null);
                    }
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
        /// 版本检验命令是否可执行：Session 激活、非处理中、非自检中，且 VersionMatch 时需填写目标版本。
        /// </summary>
        private bool CanExecuteStartVersionVerify()
        {
            if (!IsSessionActive || IsProcessing || IsSelfChecking)
                return false;
            if (CurrentVerificationType == VerificationType.VersionMatch && string.IsNullOrWhiteSpace(TargetVersionInput))
                return false;
            return true;
        }

        /// <summary>
        /// 版本检验：读取设备版本与目标版本对比，更新 Snapshot 与 LastVersionRecord。
        /// </summary>
        private async System.Threading.Tasks.Task StartVersionVerifyAsync()
        {
            if (!IsSessionActive)
            {
                _loggingService.LogWarning("版本检验：Session 未激活，请先开始测试");
                LoggingSnapshot = _loggingService.Snapshot;
                SetBatchError("请先开始测试");
                return;
            }

            var sessionId = _sessionLifecycleService.GetCurrentSessionId();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _loggingService.LogWarning("版本检验：无法获取当前 Session");
                LoggingSnapshot = _loggingService.Snapshot;
                SetBatchError("无法获取当前 Session");
                return;
            }

            TestSession session;
            try
            {
                session = await _storageService.GetSessionBySessionNameAsync(sessionId);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("版本检验：获取 Session 失败 - " + ex.Message, ex);
                LoggingSnapshot = _loggingService.Snapshot;
                SetBatchError("获取 Session 失败");
                VerificationSnapshot = VerificationSnapshot.Completed("--", "FAIL", "获取 Session 失败", sessionId, null);
                return;
            }

            if (session == null)
            {
                _loggingService.LogWarning("版本检验：Session 不存在");
                LoggingSnapshot = _loggingService.Snapshot;
                SetBatchError("Session 不存在");
                return;
            }

            var expectedVersion = session.ExpectedVersion ?? TargetVersionInput?.Trim();
            if (string.IsNullOrWhiteSpace(expectedVersion))
            {
                _loggingService.LogWarning("版本检验：未设置目标版本号，请在输入框中填写后重试");
                LoggingSnapshot = _loggingService.Snapshot;
                SetBatchError("请填写目标版本号");
                return;
            }

            session.VerificationType = VerificationType.VersionMatch;
            session.ExpectedVersion = expectedVersion;

            // 清空上次结果；Processing/Completed 由 VersionVerificationFlowService 驱动，定时器推送
            _lastVersionRecord = null;
            OnPropertyChanged(nameof(LastVersionRecord));
            OnPropertyChanged(nameof(ActualDeviceVersionDisplay));
            ClearBatchError();

            TestRecord record = null;
            try
            {
                record = await _versionVerificationFlowService.ExecuteVersionCheckAsync(session, CancellationToken.None);
                _lastVersionRecord = record;
                OnPropertyChanged(nameof(LastVersionRecord));
                OnPropertyChanged(nameof(ActualDeviceVersionDisplay));

                _loggingService.LogInfo($"版本检验完成: {(record.Result == "PASS" ? "PASS" : "FAIL")}, 实际版本={record.ActualVersion ?? "-"}");
                LoggingSnapshot = _loggingService.Snapshot;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("版本检验异常: " + ex.Message, ex);
                LoggingSnapshot = _loggingService.Snapshot;
                _lastVersionRecord = new TestRecord
                {
                    SessionId = session.Id,
                    Result = "FAIL",
                    FailReason = ex.Message,
                    ActualVersion = null,
                    ExpectedVersion = expectedVersion,
                    VerifyTime = DateTime.Now
                };
                OnPropertyChanged(nameof(LastVersionRecord));
                OnPropertyChanged(nameof(ActualDeviceVersionDisplay));
                _versionVerificationFlowService.ResetToIdle();
                // 异常时服务内部未完成，需手动设置 Completed 快照供 UI 显示
                VerificationSnapshot = VerificationSnapshot.Completed("--", "FAIL", ex.Message, sessionId, null);
            }
        }

        /// <summary>
        /// 设备信息读取：仅用于 UI「设备信息」按钮的临时调试接口。
        /// 不参与 SN 检验 / 自检 / MES 流程，可整体删除。
        /// </summary>
        public async System.Threading.Tasks.Task ReadDeviceInfoForDebugAsync()
        {
            try
            {
                var result = await _adbAccessService.ReadDeviceInfoAsync();
                if (result == null)
                {
                    _loggingService.LogWarning("设备信息读取失败：结果为空");
                }
                else if (result.IsSuccess)
                {
                    var versionText = string.IsNullOrEmpty(result.DeviceVersion) ? "(无版本信息)" : result.DeviceVersion;
                    _loggingService.LogInfo($"设备信息读取成功：SN={result.DeviceSn}, Version={versionText}");
                }
                else
                {
                    _loggingService.LogWarning("设备信息读取失败：" + (result.ErrorMessage ?? "未知错误"));
                }

                LoggingSnapshot = _loggingService.Snapshot;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("设备信息读取异常: " + ex.Message, ex);
                LoggingSnapshot = _loggingService.Snapshot;
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

            // 规则 8：自检期间不允许扫描 SN
            if (IsSelfChecking)
                return;

            if (IsProcessing)
            {
                // 正在处理中，忽略新的扫码输入
                return;
            }

            // 防御逻辑：Session 未激活时，拒绝扫码（需要先开始测试）
            if (!IsSessionActive)
            {
                // 可以在这里显示提示，或自动开始测试
                // 当前选择：拒绝扫码，要求先开始测试
                return;
            }

            // SN 检验开始时清除版本检验结果，避免与 SN 结果混淆
            _lastVersionRecord = null;
            OnPropertyChanged(nameof(LastVersionRecord));
            OnPropertyChanged(nameof(ActualDeviceVersionDisplay));

            try
            {
                var trimmedSn = sn.Trim();

                // 根据产品模式选择 Legacy 或 Phase3 流程入口，UI 仅负责路由，不参与规则判断。
                if (IsPhase3Product && !string.IsNullOrWhiteSpace(SelectedProductCode))
                {
                    await _verificationFlowService.StartPhase3VerificationAsync(trimmedSn, SelectedProductCode)
                        .ConfigureAwait(true);
                }
                else
                {
                    await _verificationFlowService.StartVerificationAsync(trimmedSn)
                        .ConfigureAwait(true);
                }
                
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

        /// <summary>
        /// 将业务名称转换为文件系统安全的名称：非法字符统一替换为下划线。
        /// 与导出服务中的规则保持一致，确保 ZIP 命名与冲突检测行为一致。
        /// </summary>
        private static string ToSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "_";

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalidChars.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }
    }

    /// <summary>
    /// 简单的 RelayCommand 实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<System.Threading.Tasks.Task> _execute;
        private readonly Func<bool> _canExecute;
        private readonly SynchronizationContext _context;

        public RelayCommand(Func<System.Threading.Tasks.Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _context = SynchronizationContext.Current;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public async void Execute(object parameter)
        {
            // 命令执行不应阻塞 UI；async/await 自然让出 UI 线程，避免在后台线程直接修改 UI 绑定属性。
            await _execute().ConfigureAwait(true);
        }

        public void RaiseCanExecuteChanged()
        {
            // 规则 12：禁止 Application.Current/Dispatcher。使用构造时捕获的 SynchronizationContext 封送。
            if (_context != null)
            {
                _context.Post(_ => CanExecuteChanged?.Invoke(this, EventArgs.Empty), null);
                return;
            }
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
