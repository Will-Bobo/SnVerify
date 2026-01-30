/// <author>
/// AI Assistant
/// </author>

using System;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Input;
using SnVerify.Services.Adb;
using SnVerify.ViewModels;
using SnVerify.Domain.Validation;
using SnVerify.Services.Ui;
using SnVerify.Ui;
using System.Threading.Tasks;

namespace SnVerify.Infrastructure
{
    /// <summary>
    /// 简单的服务工厂，用于创建 MainViewModel 所需的依赖（临时方案，用于可运行闭环）
    /// </summary>
    /// <remarks>
    /// 这是一个临时方案，用于快速实现可运行闭环。
    /// 未来应该使用依赖注入容器（如 Unity、Autofac 等）来管理服务生命周期。
    /// </remarks>
    public static class ServiceFactory
    {
        /// <summary>
        /// 创建 MainViewModel 实例及其所有依赖（异步版本，确保 ViewModel 在 UI 线程构造）
        /// </summary>
        public static async Task<MainViewModel> CreateMainViewModelAsync()
        {
            // 创建存储服务并初始化数据库连接
            var storageService = new StorageService("SnVerify.db");
            // 关键：不要 ConfigureAwait(false)。
            // CreateMainViewModelAsync 是从 MainWindow_Loaded(UI线程) 调用的，
            // 若这里 ConfigureAwait(false)，后续会在后台线程继续执行并构造 MainViewModel，
            // 导致 ViewModel 捕获的 SynchronizationContext 不是 UI 上下文，引发跨线程异常。
            await storageService.InitializeAsync();

            // 创建日志服务（使用程序安装目录下的 logs 文件夹，避免被系统清理工具清除；最近 3000 条用于 UI 显示）
            var logDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            var loggingService = new LoggingService(logDirectory, maxRecentMessages: 3000);

            // 创建 Session 生命周期服务（Phase 2.5：替代 BatchManager）
            var sessionLifecycleService = new SessionLifecycleService(storageService, loggingService);

            // 创建 ADB 访问服务
            // 优先从输出目录查找，如果不存在则从项目根目录查找
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var adbPath = System.IO.Path.Combine(baseDirectory, "tools", "adb", "adb.exe");
            
            // 如果输出目录中没有 tools，尝试从项目根目录查找（开发环境）
            if (!System.IO.File.Exists(adbPath))
            {
                // 尝试向上查找项目根目录（假设项目结构：...\SnVerify\SnVerify\bin\Debug\）
                var projectRoot = System.IO.Directory.GetParent(baseDirectory)?.Parent?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    var projectAdbPath = System.IO.Path.Combine(projectRoot, "tools", "adb", "adb.exe");
                    if (System.IO.File.Exists(projectAdbPath))
                    {
                        adbPath = projectAdbPath;
                    }
                }
            }
            
            var adbAccessService = new AdbAccessService(adbPath);

            // 创建扫码输入服务
            var scanInputService = new ScanInputService();

            // 校验流程服务工厂：按批次 ID 创建 ProcessCoordinator+VerificationFlowService
            var flowServiceFactory = new VerificationFlowServiceFactory(storageService, adbAccessService, loggingService);

            // 导出聚合服务（阶段 2 B 做，阶段 3 C 调用）：使用 LoggingService 提供的 Session 日志文件
            var exportAggregationService = new ExportAggregationService(storageService, loggingService, loggingService);

            // 命名校验服务（阶段 3 C1.3 校验弹窗挂接）
            var orderNameValidator = new OrderNameValidator();

            // UI 交互服务（阶段 3：ViewModel 禁止直接弹窗/FolderDialog）
            IUserDialogService dialogService = new WpfUserDialogService();

            // 创建 MainViewModel（需 storage/adb 用于导出与自检，logDirectory 用于导出时复制日志）
            var viewModel = new MainViewModel(
                sessionLifecycleService,
                flowServiceFactory,
                loggingService,
                storageService,
                adbAccessService,
                exportAggregationService,
                orderNameValidator,
                dialogService,
                logDirectory);

            return viewModel;
        }

        /// <summary>
        /// 创建 MainViewModel（同步兼容入口；尽量不要在 UI 线程调用）
        /// </summary>
        public static MainViewModel CreateMainViewModel()
        {
            return CreateMainViewModelAsync().GetAwaiter().GetResult();
        }
    }
}
