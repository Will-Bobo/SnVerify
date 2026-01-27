/// <author>
/// AI Assistant
/// </author>

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SnVerify.ViewModels;

namespace SnVerify
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑（Phase2 扩展）
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private bool _previousIsBatchActive;
        private bool _previousIsProcessing;
        private bool _previousIsSelfChecking;

        public MainWindow()
        {
            InitializeComponent();
            
            // 异步创建 ViewModel，避免阻塞 UI 线程
            // 先显示窗口，然后异步初始化 ViewModel
            Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// 窗口加载完成后，异步创建 ViewModel
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 异步创建 ViewModel 及其依赖
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var viewModel = Infrastructure.ServiceFactory.CreateMainViewModel();
                    
                    // 在 UI 线程上设置 DataContext
                    Dispatcher.Invoke(() =>
                    {
                        _viewModel = viewModel;
                        DataContext = _viewModel;
                        
                        // 初始化状态跟踪
                        _previousIsBatchActive = _viewModel.IsBatchActive;
                        _previousIsProcessing = _viewModel.IsProcessing;
                        _previousIsSelfChecking = _viewModel.IsSelfChecking;
                        
                        // 订阅属性变化事件，用于自动聚焦
                        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                        
                        // 聚焦扫码输入框
                        ScanInputTextBox?.Focus();
                    });
                }
                catch (Exception ex)
                {
                    // 错误处理：在 UI 线程显示错误
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"初始化失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        /// <summary>
        /// 监听 ViewModel 属性变化，实现自动聚焦
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null)
                return;

            // 监听批次激活状态变化：从非激活变为激活时，聚焦扫码输入框并清空内容
            if (e.PropertyName == nameof(MainViewModel.IsBatchActive))
            {
                var currentIsBatchActive = _viewModel.IsBatchActive;
                if (!_previousIsBatchActive && currentIsBatchActive)
                {
                    // 批次从非激活变为激活，清空扫码输入框并聚焦
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        // 清空扫码输入框内容
                        if (ScanInputTextBox != null)
                        {
                            ScanInputTextBox.Text = "";
                        }
                        // 同时清空 ViewModel 中的绑定属性（确保数据同步）
                        if (_viewModel != null)
                        {
                            _viewModel.ScanInputText = "";
                        }
                        // 聚焦扫码输入框
                        ScanInputTextBox?.Focus();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                _previousIsBatchActive = currentIsBatchActive;
            }

            // 监听处理状态变化：从处理中变为完成时，聚焦扫码输入框
            if (e.PropertyName == nameof(MainViewModel.IsProcessing))
            {
                var currentIsProcessing = _viewModel.IsProcessing;
                
                // 当从处理中变为完成时，聚焦扫码输入框
                // 检查是否从 true（处理中）变为 false（完成）
                if (_previousIsProcessing && !currentIsProcessing)
                {
                    // 从处理中变为完成，聚焦扫码输入框
                    // 使用稍低的优先级，确保 UI 更新完成后再聚焦
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        // 确保 TextBox 存在且可见
                        if (ScanInputTextBox != null && ScanInputTextBox.IsVisible)
                        {
                            ScanInputTextBox.Focus();
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                
                _previousIsProcessing = currentIsProcessing;
            }

            // 监听自检状态变化：从自检中变为完成时，聚焦扫码输入框
            if (e.PropertyName == nameof(MainViewModel.IsSelfChecking))
            {
                var currentIsSelfChecking = _viewModel.IsSelfChecking;
                if (_previousIsSelfChecking && !currentIsSelfChecking)
                {
                    // 从自检中变为完成，聚焦扫码输入框
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        ScanInputTextBox?.Focus();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                _previousIsSelfChecking = currentIsSelfChecking;
            }
        }

        /// <summary>
        /// 设置 ViewModel（用于依赖注入）
        /// </summary>
        public void SetViewModel(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        /// <summary>
        /// 扫码输入框获得焦点时，设置输入法为英文（仅针对此 TextBox）
        /// </summary>
        private void ScanInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // 使用 WPF 的 InputMethod，只影响当前 TextBox，不影响整个窗口
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null)
            {
                try
                {
                    // 设置输入法为英文（en-US），只针对这个 TextBox
                    InputMethod.SetPreferredImeState(textBox, InputMethodState.Off);
                    // 或者使用 InputMethod.SetInputScope 来限制输入范围
                    var inputScope = new InputScope();
                    var inputScopeName = new InputScopeName { NameValue = InputScopeNameValue.Default };
                    inputScope.Names.Add(inputScopeName);
                    textBox.InputScope = inputScope;
                }
                catch (Exception ex)
                {
                    // 忽略异常，不影响主流程
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] 设置输入法失败: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 人工检验按钮点击：若扫码框为空，将焦点回收到扫码输入框。
        /// </summary>
        private void StartVerifyButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel != null && string.IsNullOrWhiteSpace(_viewModel.ScanInputText))
                    ScanInputTextBox?.Focus();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 自检按钮点击：展开调试日志区域，并将焦点保持在扫码输入框。
        /// </summary>
        private void SelfCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (DebugLogExpander != null)
                DebugLogExpander.IsExpanded = true;
            ScanInputTextBox?.Focus();
        }

        /// <summary>
        /// 处理扫码输入框按键事件
        /// </summary>
        /// <remarks>
        /// 触发机制：
        /// - Enter / \r / \n 视为一次扫码完成
        /// - Enter 之前的全部内容 = SN
        /// 
        /// 如果未来扫码枪不发 Enter，可以扩展为：
        /// - 监听 TextChanged 事件，检测特定字符（如 Tab）
        /// - 或使用定时器，检测输入停顿
        /// </remarks>
        private void ScanInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // 检测 Enter 键（扫码完成信号）
            if (e.Key == Key.Enter && _viewModel != null)
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    // 检查批次是否已开始
                    if (!_viewModel.IsBatchActive)
                    {
                        // 批次未开始，在错误详情面板显示提示
                        _viewModel.SetBatchError("请确认当前批次已经开始");
                        return;
                    }

                    // 清除之前的批次错误提示（如果有）
                    _viewModel.ClearBatchError();

                    // 提取 SN（Enter 之前的全部内容）
                    string sn = textBox.Text;
                    
                    // 触发扫码输入处理（异步执行，不阻塞 UI）
                    _viewModel.HandleScanInputAsync(sn).ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            // 错误处理：可以在 UI 线程显示错误提示
                            Dispatcher.Invoke(() =>
                            {
                                // 可以显示错误消息框
                                // MessageBox.Show($"处理扫码输入时发生错误: {task.Exception?.GetBaseException()?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
                }
            }
        }
    }
}
