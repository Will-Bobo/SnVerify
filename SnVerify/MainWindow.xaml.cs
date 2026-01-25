/// <author>
/// AI Assistant
/// </author>

using System;
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

        public MainWindow()
        {
            InitializeComponent();
            
            // 注意：实际使用时需要通过依赖注入获取 ViewModel
            // 这里暂时留空，等待依赖注入配置
            // _viewModel = serviceProvider.GetService<MainViewModel>();
            // DataContext = _viewModel;
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
        /// 处理扫码输入框按键事件
        /// </summary>
        private void ScanInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel != null)
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    // 触发扫码输入处理
                    _viewModel.HandleScanInputAsync(textBox.Text).ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            // 错误处理
                        }
                    });
                }
            }
        }
    }
}
