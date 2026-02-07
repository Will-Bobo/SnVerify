/// <author>
/// AI Assistant
/// </author>

using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SnVerify.Domain.Enums;
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

            // 规则 12（Step 3 自检）：避免 Dispatcher/后台线程创建 ViewModel。
            // 使用 async/await 在 UI 线程初始化 DataContext。
            Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// 窗口加载完成后，异步创建 ViewModel
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel = await Infrastructure.ServiceFactory.CreateMainViewModelAsync();
                DataContext = _viewModel;

                // 初始化状态跟踪（仅用于后续可扩展；不在 code-behind 做业务逻辑）
                _previousIsBatchActive = _viewModel.IsSessionActive; // Phase 2.5: 使用 IsSessionActive
                _previousIsProcessing = _viewModel.IsProcessing;
                _previousIsSelfChecking = _viewModel.IsSelfChecking;

                // 订阅 ViewModel 属性变化：开始测试后聚焦并清空扫码框；检验完成后焦点回扫码框；调试日志更新时滚动到底部
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 1) 开始测试后：焦点放入扫码输入框并清空内容。
        /// 2) 人工检验完成后：焦点回到扫码输入框并刷新光标（解决竖条不闪烁问题）。
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            if (e.PropertyName == nameof(MainViewModel.UiLogs))
            {
                ScrollDebugLogToBottom();
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.IsSessionActive))
            {
                bool nowActive = _viewModel.IsSessionActive;
                if (!_previousIsBatchActive && nowActive)
                {
                    FocusScanInputAndClear();
                }
                _previousIsBatchActive = nowActive;
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.IsProcessing))
            {
                bool nowProcessing = _viewModel.IsProcessing;
                if (_previousIsProcessing && !nowProcessing)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        FocusScanInput();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                _previousIsProcessing = nowProcessing;
                return;
            }
        }

        /// <summary>
        /// 焦点放入扫码输入框并清空内容（用于「开始测试」后）
        /// </summary>
        private void FocusScanInputAndClear()
        {
            _viewModel.ScanInputText = "";
            var textBox = ScanInputTextBox;
            if (textBox != null)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        /// <summary>
        /// 将焦点设回扫码输入框并全选（用于检验完成后，含人工检验；配合 Keyboard.Focus 恢复光标闪烁）
        /// </summary>
        private void FocusScanInput()
        {
            var textBox = ScanInputTextBox;
            if (textBox == null) return;
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();
        }

        /// <summary>
        /// 调试日志更新时，将滚动条移到底部以显示最新日志
        /// </summary>
        private void ScrollDebugLogToBottom()
        {
            var listBox = DebugLogListBox;
            if (listBox?.Items == null || listBox.Items.Count == 0) return;
            var lastItem = listBox.Items[listBox.Items.Count - 1];
            Dispatcher.BeginInvoke(new Action(() =>
            {
                listBox.ScrollIntoView(lastItem);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 打开调试日志区域时，滚动到底部以显示最新日志
        /// </summary>
        private void DebugLogExpander_Expanded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(ScrollDebugLogToBottom), System.Windows.Threading.DispatcherPriority.Loaded);
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
        private async void ScanInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // 检测 Enter 键（扫码完成信号）
            if (e.Key == Key.Enter && _viewModel != null)
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    // 检查 Session 是否已开始（Phase 2.5）
                    if (!_viewModel.IsSessionActive)
                    {
                        // Session 未开始，在错误详情面板显示提示
                        _viewModel.SetBatchError("请确认当前检验已经开始");
                        return;
                    }

                    // 清除之前的批次错误提示（如果有）
                    _viewModel.ClearBatchError();

                    // 提取 SN（Enter 之前的全部内容）
                    string sn = textBox.Text;

                    try
                    {
                        // 触发扫码输入处理（异步执行，不阻塞 UI）
                        await _viewModel.HandleScanInputAsync(sn);
                    }
                    catch
                    {
                        // 忽略异常：错误日志由 Service 记录；避免 UI 卡死
                    }
                    finally
                    {
                        // 每次检验完成后，将焦点返回到扫码输入框并全选，便于连续操作
                        textBox.Focus();
                        textBox.SelectAll();
                    }
                }
            }
        }

        /// <summary>
        /// 人工检验按钮点击后，将焦点返回到扫码输入框（命令逻辑仍由 StartVerifyCommand 执行）
        /// </summary>
        private void StartVerifyButton_Click(object sender, RoutedEventArgs e)
        {
            var textBox = this.FindName("ScanInputTextBox") as System.Windows.Controls.TextBox;
            if (textBox != null)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        /// <summary>
        /// 自检按钮点击后，自动展开调试日志并保持焦点在扫码输入框
        /// </summary>
        private void SelfCheckButton_Click(object sender, RoutedEventArgs e)
        {
            var expander = this.FindName("DebugLogExpander") as System.Windows.Controls.Expander;
            if (expander != null)
            {
                expander.IsExpanded = true;
            }

            var textBox = this.FindName("ScanInputTextBox") as System.Windows.Controls.TextBox;
            if (textBox != null)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        /// <summary>
        /// 设备信息按钮：触发 ViewModel 的临时设备信息读取接口，并展开调试日志。
        /// </summary>
        private async void DeviceInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                try
                {
                    await _viewModel.ReadDeviceInfoForDebugAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] DeviceInfoButton_Click exception: {ex.Message}");
                }
            }

            var expander = this.FindName("DebugLogExpander") as System.Windows.Controls.Expander;
            if (expander != null)
            {
                expander.IsExpanded = true;
            }
        }
    }

    /// <summary>
    /// 布尔值取反转换器，用于将 true 转换为 false，false 转换为 true（与 MainWindow 同程序集，供 XAML 使用）
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return false;
        }
    }

    /// <summary>
    /// 根据 VerificationType 转换为 Visibility：当 value 与 parameter 匹配时 Visible，否则 Collapsed。
    /// parameter 为 "SnMatch" 或 "VersionMatch" 字符串。
    /// </summary>
    public class VerificationTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is VerificationType type && parameter is string paramStr)
            {
                if (paramStr == "SnMatch" && type == VerificationType.SnMatch)
                    return Visibility.Visible;
                if (paramStr == "VersionMatch" && type == VerificationType.VersionMatch)
                    return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// VerificationType 转显示文本：SnMatch -> "SN", VersionMatch -> "Version"
    /// </summary>
    public class VerificationTypeToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is VerificationType type)
            {
                switch (type)
                {
                    case VerificationType.SnMatch: return "SN检验";
                    case VerificationType.VersionMatch: return "版本检验";
                }
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
