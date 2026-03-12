SnVerify 阶段架构综述

一、整体架构与技术栈
1. 客户端技术栈：WPF (.NET) + MVVM。
2. 主入口与 MVVM：
   - View：MainWindow.xaml，绑定 MainViewModel，负责 SN 输入、状态卡片、版本对比、导出入口等。
   - ViewModel：MainViewModel 聚合 SessionLifecycleService、VerificationFlowServiceFactory、StorageService、ExportAggregationService、LoggingService、AdbAccessService、DeviceAccessService、RulePipelineExecutor、VersionVerificationService、ParameterService、ProductRegistry 等。
3. 分层结构：
   - UI 层：XAML + ViewModel（命令与绑定）。
   - 流程编排层：ProcessCoordinator、VerificationFlowService、VersionVerificationFlowService（Legacy）。
   - 领域服务层：StorageService、SessionLifecycleService、AdbAccessService、DeviceAccessService、ExportAggregationService、LoggingService、RulePipelineExecutor、VersionVerificationService、MESInterface/Mes Gate 等。
   - 领域模型层：TestSession、TestRecord、DeviceInfo、VerificationParameter、Product、Order 等。
   - 基础设施层：ServiceFactory、VerificationFlowServiceFactory、DeviceAccess 实现、ProductExportRegistry、SessionExporterFactory 等。

二、核心业务流程
1. Legacy / Phase2.5 SN 校验流程（StartVerificationAsync）：
   - 入口：MainViewModel.HandleScanInputAsync → VerificationFlowService.StartVerificationAsync → ProcessCoordinator.StartVerificationAsync。
   - 可选 MES Pre-Gate：通过 IMesPreCheck/MesMode 决定是否放行或以 Strict 模式直接拒绝（当前默认 Disabled）。
   - ADB 读取设备 SN：通过 AdbAccessService.ReadDeviceSnAsync，内含 ylzero 预热、重试、超时处理。
   - 决策树：依据 StickerSN 与 DeviceSN 是否相等及历史 PASS（IsStickerSnInPassHistoryAsync / IsDeviceSnInPassHistoryAsync / IsBindingInPassHistoryAsync）判断 PASS/FAIL/TIMEOUT。
   - 结果写入：SaveResultAsync / SaveOrUpdateFailResultAsync 写 TestRecord，并在启用时调用 MES Post-Report。
2. Phase3 SN 扩展流程（ProcessScanAsync）（Phase3新增）：
   - 入口：MainViewModel 在 Phase3 产品模式下调用 VerificationFlowService.StartPhase3VerificationAsync → ProcessCoordinator.ProcessScanAsync(sn, projectId)。
   - 前置：通过 StorageService 与 ParameterService 读取该 Session 绑定的 VerificationParameter（期望 Android/Board/ChargeBoard 版本）。
   - ProductProfile：通过 ProductRegistry.GetProductProfile(projectId) 获取产品配置，找不到则直接写 FAIL（PRODUCT_PROFILE_NOT_FOUND）。
   - 规则执行：构造或注入 RulePipelineExecutor，内部调用 DeviceAccessService.ReadDeviceInfoAsync 读完整 DeviceInfo（DeviceSn、WifiMac、ChipId、BoardVersion、ChargeBoardVersion、AndroidVersion），并结合 StorageService/VersionVerificationService 完成：
     - SN 与 DeviceSN 匹配。
     - ChipId 格式校验与订单范围唯一性校验（IsChipIdPassedInOrderAsync）。
     - AndroidVersion/BoardVersion/ChargeBoardVersion 三元版本强校验。
   - 结果写入：SavePhase3ResultAsync 将 Result、FailReason、DeviceInfo 及期望版本快照写入 TestRecord。
3. 版本校验流程：
   - Legacy：VersionVerificationFlowService.ExecuteVersionCheckAsync 使用 AdbAccessService.ReadDeviceInfoAsync 比对 TestSession.ExpectedVersion 与实际版本，StickerSN 固定为 "-"。
   - Phase3：VersionVerificationService + VerificationParameter + RulePipelineExecutor 共同实现多字段强校验。
4. 历史查询与导出：
   - 历史查询/重复校验：基于 StorageService 的 TestRecord / TestSession 查询与 SN/ChipId 历史 PASS 判断。
   - 导出：ExportAggregationService 以订单或项目为维度，使用 SessionExporterFactory + ProductExportRegistry 生成每个 Session 的 Excel，连同日志打包 ZIP。

三、Phase3 扩展概览（Phase3新增）
1. 新字段与模型：
   - DeviceInfo 新增：ChipId、WifiMac、BoardVersion、ChargeBoardVersion、AndroidVersion。
   - TestRecord 新增列：WifiMac、ChipId、BoardVersion、ChargeBoardVersion、ExpectedBoardVersion、ExpectedChargeBoardVersion。
   - VerificationParameter 表：按 Session 维度存储 ExpectedAndroidVersion、ExpectedBoardVersion、ExpectedChargeBoardVersion。
2. 索引与唯一性：
   - 新增 idx_testrecord_chipid_result 索引，用于高效判断订单内 ChipId 是否已有 PASS。
   - SN 唯一性仍通过 StickerSN/DeviceSN + Result=PASS 的索引与逻辑层判断实现。
3. 流程变化点：
   - 将 Legacy SN 决策树封装在 StartVerificationAsync，Phase3 使用全新入口 ProcessScanAsync，不互相污染。
   - 将 DeviceInfo 读取与复杂规则纳入 RulePipelineExecutor，使校验逻辑与 ProductProfile 紧密结合。
4. 未包含的内容：
   - MES：Mes Gate 接口与 JekeMesPlugin 已存在，但默认 MesMode.Disabled，插件实现为 Stub / TODO，Phase3 主路径实际不调用 MES。
   - 动态规则配置：ProductProfile 仍为代码内静态配置，尚未从 DB 或配置文件加载。

四、数据库与持久化
1. 存储技术：手写 SQLite 封装（StorageService），无 EF DbContext。
2. 表结构概述：
   - Product：ProductName 唯一，新增 ProductCode 用于区分项目类型。
   - Order：OrderName + ProductId 复合唯一，支持按项目聚合订单。
   - TestSession：SessionName 唯一，与 Order 关联；SessionLifecycleService 负责创建/管理。
   - TestRecord：承载 SN/DeviceSN/WifiMac/ChipId/各版本与 Result/FailReason/VerifyTime，附 SN 与 ChipId 相关索引。
   - VerificationParameter：SessionId 唯一，一 Session 一份版本参数快照。
3. 查询与校验路径：
   - SN/DeviceSN 历史 PASS：IsStickerSnInPassHistoryAsync / IsDeviceSnInPassHistoryAsync / IsBindingInPassHistoryAsync。
   - 订单内唯一性：IsStickerSnPassedInOrderAsync / IsChipIdPassedInOrderAsync。

五、服务与调用关系（摘要）
1. MainViewModel：
   - 负责响应 UI 命令，驱动 SessionLifecycleService、VerificationFlowServiceFactory、ExportAggregationService、AdbAccessService 等。
2. ProcessCoordinator：
   - Legacy：StartVerificationAsync 中实现 SN 校验 + 可选 MES Pre/Post。
   - Phase3：ProcessScanAsync 调用 RulePipelineExecutor 进行 DeviceInfo + ChipId + 版本综合校验。
3. RulePipelineExecutor（Phase3）：
   - 依赖：StorageService、DeviceAccessService、VersionVerificationService、LoggingService。
   - 统一处理 SN 匹配、ChipId 规则、版本校验与结果聚合。
4. DeviceAccessService（Phase3）：
   - 基于 ProductProfile + ADB 命令+parser 组合读取完整 DeviceInfo。
5. ExportAggregationService：
   - 聚合 StorageService、SessionExporterFactory、LoggingService，完成订单/项目维度导出。

（以上为 SnVerify 阶段架构/流程/模块的整体综述，下面为针对当前状态的重构评估与改进建议。）

