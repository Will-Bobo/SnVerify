/// <author>
/// AI Assistant
/// </author>
/// <remarks>
/// Phase 2.5 冻结：核心检验链路 Scan SN → Read Device SN → Verify → Result 不可侵入；MES 仅通过 Pre-Gate / Post-Report 挂载。
/// Phase 3 挂载：每条 SN 前 Pre-Gate（MesMode Enabled 时 Reject 不阻断、仅弱提示；Strict 时 Reject 阻断）；结果落库后 Post-Report 异步上报，失败不反写结果。
/// </remarks>

using System;
using System.Threading;
using System.Threading.Tasks;
using SnVerify.Domain.Models;
using SnVerify.Domain.State;
using SnVerify.Services.Adb;
using SnVerify.Services.Logging;
using SnVerify.Services.Mes.Gate;
using SnVerify.Services.Storage;
using SnVerify.Services.Parameter;
using SnVerify.Services.Verification;
using SnVerify.Services.DeviceAccess;
using SnVerify.Infrastructure.Product;
using SnVerify.Services.Rules;

namespace SnVerify.Services.Coordination
{
    /// <summary>
    /// 流程编排服务实现，负责协调各个 Service 完成 SN 校验流程
    /// </summary>
    public class ProcessCoordinator : IProcessCoordinator
    {
        private readonly string _sessionId;
        private readonly string _orderId;
        private readonly IStorageService _storageService;
        private readonly IAdbAccessService _adbAccessService;
        private readonly ILoggingService _loggingService;
        private readonly IMesPreCheck _mesPreCheck;
        private readonly IMesResultReporter _mesReporter;
        private readonly MesMode _mesMode;
        private readonly IParameterService _parameterService;
        private readonly IVersionVerificationService _versionVerificationService;
        private readonly IProductRegistry _productRegistry;
        private readonly IDeviceAccessService _deviceAccessService;
        private readonly IRulePipelineExecutor _rulePipelineExecutor;
        private readonly object _lockObject = new object();
        private VerificationSnapshot _snapshot;

        /// <summary>
        /// 当前流程状态快照
        /// </summary>
        public VerificationSnapshot Snapshot
        {
            get
            {
                lock (_lockObject)
                {
                    return _snapshot ?? VerificationSnapshot.Idle();
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
        /// 状态快照变化事件
        /// </summary>
        public event EventHandler<VerificationSnapshot> SnapshotChanged;

        /// <summary>
        /// MES 事件通知（仅弱提示用途，不得影响 PASS/FAIL）。
        /// </summary>
        public event EventHandler<MesEventArgs> MesEventOccurred;

        /// <summary>
        /// 初始化流程编排服务（Phase 2.5 以 SessionId 为入口，MES 预留）
        /// </summary>
        /// <param name="sessionId">当前会话 ID</param>
        /// <param name="storageService">存储服务</param>
        /// <param name="adbAccessService">ADB 访问服务</param>
        /// <param name="loggingService">日志服务（可选）</param>
        /// <param name="mesPreCheck">MES Pre-Gate（可选，null 时不调用）</param>
        /// <param name="mesReporter">MES Post-Report（可选，null 时不调用）</param>
        /// <param name="mesMode">MES 模式，Disabled 时不调用 Pre/Post</param>
        /// <param name="orderId">订单 ID（可选，用于 MES 上下文与订单维度唯一性检查）</param>
        /// <param name="parameterService">版本参数服务（Phase 3：用于获取项目级版本目标配置，可选）</param>
        /// <param name="versionVerificationService">三版本强校验服务（Phase 3 Stage2：可选，未注入时使用默认实现）。</param>
        /// <param name="productRegistry">ProductRegistry 读取接口（Stage3：唯一规则入口，可选；默认使用静态注册表适配器）。</param>
        /// <param name="deviceAccessService">设备访问服务（Stage3：当 rulePipelineExecutor 为 null 时用于构建默认规则执行器，可选）。</param>
        /// <param name="rulePipelineExecutor">规则链执行器（Stage3：可选；为空且提供了 deviceAccessService 时使用默认实现）。</param>
        public ProcessCoordinator(
            string sessionId,
            IStorageService storageService,
            IAdbAccessService adbAccessService,
            ILoggingService loggingService = null,
            IMesPreCheck mesPreCheck = null,
            IMesResultReporter mesReporter = null,
            MesMode mesMode = MesMode.Disabled,
            string orderId = null,
            IParameterService parameterService = null,
            IVersionVerificationService versionVerificationService = null,
            IProductRegistry productRegistry = null,
            IDeviceAccessService deviceAccessService = null,
            IRulePipelineExecutor rulePipelineExecutor = null)
        {
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _orderId = orderId;
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _adbAccessService = adbAccessService ?? throw new ArgumentNullException(nameof(adbAccessService));
            _loggingService = loggingService;
            _mesPreCheck = mesPreCheck;
            _mesReporter = mesReporter;
            _mesMode = mesMode;
            _parameterService = parameterService;
            _versionVerificationService = versionVerificationService;
            _productRegistry = productRegistry ?? new ProductRegistryAdapter();
            _deviceAccessService = deviceAccessService;
            _rulePipelineExecutor = rulePipelineExecutor;
            _snapshot = VerificationSnapshot.Idle(_sessionId);
        }

        /// <summary>
        /// 启动校验流程（原子化执行）
        /// </summary>
        public async Task StartVerificationAsync(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));

            // 原子锁定检查
            bool shouldProcess = false;
            lock (_lockObject)
            {
                if (!_snapshot.IsProcessing)
                {
                    shouldProcess = true;
                    UpdateSnapshot(VerificationSnapshot.Processing(sn, _sessionId));
                }
            }

            if (!shouldProcess)
            {
                // 正在处理中，忽略本次请求
                return;
            }

            // MES Pre-Gate（Phase 2.5 冻结 / Phase 3 挂载）：MesMode≠Disabled 且 PreCheck 非 null 时，每条 SN 前调用一次
            if (_mesMode != MesMode.Disabled && _mesPreCheck != null)
            {
                var preCtx = new MesContext { SessionId = _sessionId, OrderId = _orderId, StickerSN = sn?.Trim(), At = DateTime.Now };
                MesPreCheckResult preResult = null;
                try
                {
                    preResult = await _mesPreCheck.CheckAsync(preCtx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _loggingService?.LogInfo($"MES Pre-Gate 异常: {ex.Message}");
                    if (_mesMode == MesMode.Strict)
                    {
                        UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", "MES Pre-Gate 异常", _sessionId, null));
                        return;
                    }
                    MesEventOccurred?.Invoke(this, new MesEventArgs(MesEventType.PreGateFailed, "MES Pre-Gate 异常（降级继续）", _sessionId, _orderId));
                }

                if (preResult != null)
                {
                    if (preResult.Decision == MesPreCheckDecision.Reject)
                    {
                        _loggingService?.LogInfo($"MES Pre-Gate 拒绝: {preResult.Reason ?? "Reject"}");
                        if (_mesMode == MesMode.Strict)
                        {
                            UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", preResult.Reason ?? "MES拒绝", _sessionId, null));
                            return;
                        }
                        MesEventOccurred?.Invoke(this, new MesEventArgs(MesEventType.PreGateFailed, preResult.Reason ?? "MES拒绝（不阻断）", _sessionId, _orderId));
                    }
                    else if (preResult.Decision == MesPreCheckDecision.DegradedAllow)
                    {
                        MesEventOccurred?.Invoke(this, new MesEventArgs(MesEventType.PreGateFailed, preResult.Reason ?? "MES降级放行", _sessionId, _orderId));
                    }
                }
            }

            // 记录检验开始
            var verifyStartTime = DateTime.Now;
            _loggingService?.LogInfo($"检验开始，扫码枪SN: {sn}");

            try
            {
                // Step 1: 通过 ADB 读取设备 SN
                var adbResult = await _adbAccessService.ReadDeviceSnAsync();
                if (!adbResult.IsSuccess)
                {
                    var result = adbResult.IsTimeout ? "TIMEOUT" : "FAIL";
                    var failReason = adbResult.IsTimeout
                        ? "ADB读取设备超时"
                        : $"请检查设备连接，{adbResult.ErrorReason}";
                    await SaveOrUpdateFailResultAsync(sn, result, failReason, null);
                    _loggingService?.LogInfo($"检验结果 [{result}] , [扫码枪SN: {sn}, 设备SN: N/A] , 错误结果: {failReason}");
                    _loggingService?.LogInfo("检验结束");
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, result, failReason, _sessionId, null));
                    return;
                }

                var deviceSN = adbResult.Sn;
                if (string.IsNullOrWhiteSpace(deviceSN))
                {
                    const string failReason = "ADB读取设备SN为空";
                    await SaveOrUpdateFailResultAsync(sn, "FAIL", failReason, null);
                    _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {sn}, 设备SN: N/A] , 错误结果: {failReason}");
                    _loggingService?.LogInfo("检验结束");
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", failReason, _sessionId, null));
                    return;
                }

                // Step 2: 决策树校验逻辑（基于 SN_Sticker_Device_Relation_Rules.md）
                var stickerSN = sn.Trim();
                var deviceSNNormalized = deviceSN.Trim();

                // 规则 1：绑定一致，且无历史 PASS 绑定 → PASS
                if (stickerSN == deviceSNNormalized)
                {
                    // 优先检查绑定关系（规则2优先于规则1）；PASS 时 StickerSN=DeviceSN，仅传一个 SN 即可
                    var bindingExists = await _storageService.IsBindingInPassHistoryAsync(stickerSN);
                    if (bindingExists)
                    {
                        // 规则 2：绑定一致，但存在历史 PASS 绑定 → FAIL（已出站）
                        const string failReason = "设备SN已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _sessionId, deviceSNNormalized));
                        return;
                    }

                    // 检查是否在历史 PASS 中（用于规则1判断）
                    var stickerExists = await _storageService.IsStickerSnInPassHistoryAsync(stickerSN);
                    var deviceExists = await _storageService.IsDeviceSnInPassHistoryAsync(deviceSNNormalized);

                    if (!stickerExists && !deviceExists)
                    {
                        // 规则 1：PASS
                        await SaveResultAsync(stickerSN, "PASS", null, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [PASS] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 成功结果");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "PASS", null, _sessionId, deviceSNNormalized));
                        return;
                    }
                    else
                    {
                        // 规则 2：绑定一致，但存在历史 PASS 绑定 → FAIL（已出站）
                        const string failReason = "设备SN已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _sessionId, deviceSNNormalized));
                        return;
                    }
                }
                else
                {
                    // 绑定不一致：StickerSN != DeviceSN
                    const string mismatchReason = "设备SN 与 条形码SN [不匹配]";

                    // 规则 3：StickerSN 已存在于历史 PASS 绑定中 → FAIL（贴纸重复）
                    var stickerExists = await _storageService.IsStickerSnInPassHistoryAsync(stickerSN);
                    if (stickerExists)
                    {
                        var failReason = $"{mismatchReason}，并且 条形码SN 已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _sessionId, deviceSNNormalized));
                        return;
                    }

                    // 规则 4：DeviceSN 已存在于历史 PASS 绑定中 → FAIL（设备已出站）
                    var deviceExists = await _storageService.IsDeviceSnInPassHistoryAsync(deviceSNNormalized);
                    if (deviceExists)
                    {
                        var failReason = $"{mismatchReason}，并且 设备SN 已存在";
                        await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason, deviceSNNormalized);
                        _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason}");
                        _loggingService?.LogInfo("检验结束");
                        UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason, _sessionId, deviceSNNormalized));
                        return;
                    }

                    // 规则 5：绑定不一致，且双方均无历史 PASS 绑定 → FAIL（包装不一致）
                    var failReason5 = mismatchReason;
                    await SaveOrUpdateFailResultAsync(stickerSN, "FAIL", failReason5, deviceSNNormalized);
                    _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {stickerSN}, 设备SN: {deviceSNNormalized}] , 错误结果: {failReason5}");
                    _loggingService?.LogInfo("检验结束");
                    UpdateSnapshot(VerificationSnapshot.Completed(stickerSN, "FAIL", failReason5, _sessionId, deviceSNNormalized));
                    return;
                }
            }
            catch (Exception ex)
            {
                // 异常处理
                await SaveOrUpdateFailResultAsync(sn, "FAIL", $"EXCEPTION: {ex.Message}", null);
                _loggingService?.LogInfo($"检验结果 [FAIL] , [扫码枪SN: {sn}, 设备SN: N/A] , 错误结果: EXCEPTION: {ex.Message}");
                _loggingService?.LogInfo("检验结束");
                UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", $"EXCEPTION: {ex.Message}", _sessionId, null));
            }
        }

        /// <summary>
        /// Phase 3 SN 校验流程（扩展版）：
        /// 获取项目参数 → ADB 读取设备信息 → SN 匹配 → ChipId 格式检查 → ChipId 订单内唯一性检查 → 版本校验 → 保存 TestRecord。
        /// 
        /// 说明：
        /// - 不改变原有 StartVerificationAsync 的行为，仅作为 Phase 3 扩展入口供后续挂接。
        /// - orderId 维度使用构造函数中注入的 _orderId，projectId 用于 ParameterService。
        /// </summary>
        /// <param name="sn">扫码输入的 SN（StickerSN）</param>
        /// <param name="projectId">项目 ID（用于参数读取）</param>
        public async Task ProcessScanAsync(string sn, string projectId)
        {
            if (string.IsNullOrWhiteSpace(sn))
                throw new ArgumentException("SN 不能为空", nameof(sn));
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("ProjectId 不能为空", nameof(projectId));
            if (string.IsNullOrWhiteSpace(_orderId))
                throw new InvalidOperationException("OrderId 未设置，无法执行订单维度唯一性检查");

            // 原子锁定检查
            bool shouldProcess = false;
            lock (_lockObject)
            {
                if (!_snapshot.IsProcessing)
                {
                    shouldProcess = true;
                    UpdateSnapshot(VerificationSnapshot.Processing(sn, _sessionId));
                }
            }

            if (!shouldProcess)
            {
                // 正在处理中，忽略本次请求
                return;
            }

            _loggingService?.LogInfo($"[Phase3] 校验开始，项目={projectId}, 订单={_orderId}, SN={sn}");

            try
            {
                // Step 1: 按当前 Session 解析项目个体名，再取版本参数（Session → Order → Product → ProductName）
                VerificationParameter parameter = null;
                if (_parameterService != null && _storageService != null)
                {
                    var productName = await _storageService.GetProductNameBySessionNameAsync(_sessionId).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(productName))
                        parameter = await _parameterService.GetParameterAsync(productName).ConfigureAwait(false);
                }

                // Stage3：规则判断全部外移到 RulePipelineExecutor；Profile 按产品类型（projectId = ProductCode）取。
                var productProfile = _productRegistry.GetProductProfile(projectId);
                if (productProfile == null)
                {
                    const string failReason = "PRODUCT_PROFILE_NOT_FOUND";
                    _loggingService?.LogInfo($"[Phase3] 未找到产品 Profile: productCode={projectId}");
                    await SavePhase3ResultAsync(sn.Trim(), "FAIL", failReason, null, parameter).ConfigureAwait(false);
                    UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", failReason, _sessionId, null));
                    return;
                }

                var executor = _rulePipelineExecutor ?? (_deviceAccessService != null
                    ? new RulePipelineExecutor(
                        _storageService,
                        _deviceAccessService,
                        _versionVerificationService ?? new VersionVerificationService())
                    : throw new InvalidOperationException("Phase3 需要注入 IRulePipelineExecutor 或 IDeviceAccessService"));

                var execResult = await executor
                    .ExecuteAsync(productProfile, deviceInfo: null, parameter, stickerSn: sn, orderId: _orderId)
                    .ConfigureAwait(false);

                var deviceSN = execResult?.DeviceInfo?.DeviceSn?.Trim();
                await SavePhase3ResultAsync(sn.Trim(), execResult?.Result ?? "FAIL", execResult?.FailReason, execResult?.DeviceInfo, parameter).ConfigureAwait(false);
                UpdateSnapshot(VerificationSnapshot.Completed(sn.Trim(), execResult?.Result ?? "FAIL", execResult?.FailReason, _sessionId, deviceSN));
            }
            catch (Exception ex)
            {
                var failReason = $"EXCEPTION: {ex.Message}";
                await SavePhase3ResultAsync(sn.Trim(), "FAIL", failReason, null, null).ConfigureAwait(false);
                _loggingService?.LogInfo($"[Phase3] 校验异常: {ex.Message}");
                UpdateSnapshot(VerificationSnapshot.Completed(sn, "FAIL", failReason, _sessionId, null));
            }
        }

        /// <summary>
        /// 重置流程状态，允许下一次扫描
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                UpdateSnapshot(VerificationSnapshot.Idle(_sessionId));
            }
        }

        /// <summary>
        /// 保存或更新 FAIL 结果（Phase 2.5 使用 TestRecord）；落库后执行 MES Post-Report。
        /// 
        /// 重要约束：
        /// - 若当前 Session + StickerSN 下已存在 PASS 记录，则保持该 PASS 事实不变，新的 FAIL/TIMEOUT 仅追加记录。
        /// - 若仅存在 FAIL/TIMEOUT 记录，则在原记录上更新（同一次重试场景）。
        /// </summary>
        private async Task SaveOrUpdateFailResultAsync(string sn, string result, string failReason, string deviceSN)
        {
            try
            {
                // 将业务 SessionId（字符串）映射为内部自增 Id（TestSession.Id）
                var internalSessionId = await _storageService.GetInternalSessionIdBySessionNameAsync(_sessionId).ConfigureAwait(false);
                if (!internalSessionId.HasValue)
                {
                    // 未找到对应 Session 记录时，不落库，仅跳过（不影响当前检验流程）
                    return;
                }

                var existing = await _storageService
                    .GetTestRecordBySessionAndStickerSnAsync(internalSessionId.Value, sn)
                    .ConfigureAwait(false);

                var at = DateTime.Now;

                if (existing != null && !string.Equals(existing.Result, "PASS", StringComparison.OrdinalIgnoreCase))
                {
                    // 仅在不存在 PASS 事实时才覆盖原有记录（例如重复 FAIL/重试场景）。
                    existing.Result = result;
                    existing.FailReason = failReason;
                    existing.DeviceSN = deviceSN;
                    existing.VerifyTime = at;
                    await _storageService.UpdateTestRecordAsync(existing).ConfigureAwait(false);
                }
                else
                {
                    // 若已存在 PASS，则追加一条新的 FAIL/TIMEOUT 记录，避免污染历史 PASS。
                    var record = new TestRecord
                    {
                        SessionId = internalSessionId.Value,
                        StickerSN = sn,
                        DeviceSN = deviceSN,
                        Result = result,
                        FailReason = failReason,
                        VerifyTime = at
                    };
                    await _storageService.SaveTestRecordAsync(record).ConfigureAwait(false);
                }

                await PostReportAsync(sn, result, failReason, deviceSN).ConfigureAwait(false);
            }
            catch
            {
                // 保存/更新失败不影响流程
            }
        }

        /// <summary>
        /// 保存校验结果到存储服务（Phase 2.5 使用 TestRecord）；落库后执行 MES Post-Report。
        /// </summary>
        private async Task SaveResultAsync(string sn, string result, string failReason, string deviceSN)
        {
            try
            {
                var internalSessionId = await _storageService.GetInternalSessionIdBySessionNameAsync(_sessionId).ConfigureAwait(false);
                if (!internalSessionId.HasValue)
                {
                    // 未找到对应 Session 记录时，不落库，仅跳过（不影响当前检验流程）
                    return;
                }

                var record = new TestRecord
                {
                    SessionId = internalSessionId.Value,
                    StickerSN = sn,
                    DeviceSN = deviceSN,
                    Result = result,
                    FailReason = failReason,
                    VerifyTime = DateTime.Now
                };
                await _storageService.SaveTestRecordAsync(record).ConfigureAwait(false);
                await PostReportAsync(sn, result, failReason, deviceSN).ConfigureAwait(false);
            }
            catch
            {
                // 保存失败不影响流程
            }
        }

        /// <summary>
        /// Phase 3：保存扩展字段的校验结果到 TestRecord（包含 DeviceInfo 与版本参数）。
        /// </summary>
        private async Task SavePhase3ResultAsync(string sn, string result, string failReason, DeviceInfo deviceInfo, VerificationParameter parameter)
        {
            try
            {
                var internalSessionId = await _storageService.GetInternalSessionIdBySessionNameAsync(_sessionId).ConfigureAwait(false);
                if (!internalSessionId.HasValue)
                {
                    return;
                }

                var record = new TestRecord
                {
                    SessionId = internalSessionId.Value,
                    StickerSN = sn,
                    DeviceSN = deviceInfo?.DeviceSn,
                    WifiMac = deviceInfo?.WifiMac,
                    ChipId = deviceInfo?.ChipId,
                    BoardVersion = deviceInfo?.BoardVersion,
                    ChargeBoardVersion = deviceInfo?.ChargeBoardVersion,
                    Result = result,
                    FailReason = failReason,
                    VerifyTime = DateTime.Now,
                    ExpectedVersion = parameter?.ExpectedAndroidVersion,
                    ActualVersion = deviceInfo?.AndroidVersion
                };

                await _storageService.SaveTestRecordAsync(record).ConfigureAwait(false);
                await PostReportAsync(sn, result, failReason, record.DeviceSN).ConfigureAwait(false);
            }
            catch
            {
                // Phase 3 扩展结果保存失败不应中断上层流程。
            }
        }

        /// <summary>
        /// MES Post-Report 调用点（Phase 2.5 预留）。MesMode≠Disabled 且 Reporter 非 null 时调用，失败仅记日志。
        /// </summary>
        private async Task PostReportAsync(string stickerSN, string result, string failReason, string deviceSN)
        {
            if (_mesMode == MesMode.Disabled || _mesReporter == null) return;
            try
            {
                await _mesReporter.ReportTestResultAsync(new TestResultContext
                {
                    SessionId = _sessionId,
                    OrderId = _orderId,
                    StickerSN = stickerSN,
                    DeviceSN = deviceSN,
                    Result = result,
                    FailReason = failReason,
                    VerifyTime = DateTime.Now
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _loggingService?.LogInfo($"MES Post-Report 失败: {ex.Message}");
                // Phase 2.5 规则：Post-Report 失败是“健康态异常”，不影响 PASS/FAIL，仅弱提示 + 日志。
                MesEventOccurred?.Invoke(this, new MesEventArgs(
                    MesEventType.ReportFailed,
                    "MES 上报失败（不影响当前测试结果）",
                    sessionId: _sessionId,
                    orderId: _orderId));
            }
        }

        /// <summary>
        /// 更新快照并触发事件
        /// </summary>
        private void UpdateSnapshot(VerificationSnapshot newSnapshot)
        {
            lock (_lockObject)
            {
                _snapshot = newSnapshot;
            }

            // 触发事件（在锁外，避免死锁）
            OnSnapshotChanged(newSnapshot);
        }

        /// <summary>
        /// 触发快照变化事件
        /// </summary>
        protected virtual void OnSnapshotChanged(VerificationSnapshot snapshot)
        {
            var handler = SnapshotChanged;
            if (handler != null)
            {
                handler(this, snapshot);
            }
        }
    }
}
