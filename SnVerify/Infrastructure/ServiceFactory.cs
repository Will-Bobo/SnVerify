/// <author>
/// AI Assistant
/// </author>

using System;
using SnVerify.Services.Batch;
using SnVerify.Services.Coordination;
using SnVerify.Services.Logging;
using SnVerify.Services.MES;
using SnVerify.Services.Storage;
using SnVerify.Services.Input;
using SnVerify.Services.Adb;
using SnVerify.ViewModels;

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
        /// 创建 MainViewModel 实例及其所有依赖
        /// </summary>
        public static MainViewModel CreateMainViewModel()
        {
            // 创建存储服务并初始化数据库连接
            var storageService = new StorageService("SnVerify.db");
            // 同步初始化数据库（在 UI 线程上执行，避免异步问题）
            storageService.InitializeAsync().GetAwaiter().GetResult();

            // 创建批次管理器
            var batchManager = new BatchManager(storageService);

            // 创建日志服务（使用临时目录）
            var logDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SnVerify_Logs");
            var loggingService = new LoggingService(logDirectory);

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

            // 创建 MES 接口服务（使用占位 URL）
            var mesBaseUrl = "http://localhost/mes"; // 占位 URL，实际使用时需要配置
            var mesInterface = new MESInterface(mesBaseUrl, loggingService);

            // 校验流程服务工厂：按批次 ID 创建 ProcessCoordinator+VerificationFlowService
            var flowServiceFactory = new VerificationFlowServiceFactory(storageService, adbAccessService, loggingService);

            // 创建 MainViewModel（需 storage/adb 用于导出与自检，logDirectory 用于导出时复制日志）
            var viewModel = new MainViewModel(
                batchManager,
                flowServiceFactory,
                loggingService,
                mesInterface,
                storageService,
                adbAccessService,
                logDirectory);

            return viewModel;
        }
    }
}
