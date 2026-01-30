/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.6 Step 1：导出维度选择 WPF 对话框，替代 MessageBox Yes/No/Cancel。
/// </remarks>
using System.Windows;
using SnVerify.Services.Ui;

namespace SnVerify.Views.Dialogs
{
    /// <summary>
    /// 选择导出维度：按项目 / 按订单。未选择时确定不可用；取消或关闭返回 null。
    /// </summary>
    public partial class ExportDimensionDialog : Window
    {
        public ExportDimensionDialog()
        {
            InitializeComponent();
            // 默认选中“按项目导出”，确定按钮可用
            BtnOk.IsEnabled = GetSelectedDimension() != null;
            RadioByProject.Checked += OnSelectionChanged;
            RadioByOrder.Checked += OnSelectionChanged;
            RadioByProject.Unchecked += OnSelectionChanged;
            RadioByOrder.Unchecked += OnSelectionChanged;
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            BtnOk.IsEnabled = GetSelectedDimension() != null;
        }

        /// <summary>
        /// 当前选择对应的维度；未选为 null。用于 ShowDialog 后由调用方读取。
        /// </summary>
        public ExportDimension? SelectedDimension => GetSelectedDimension();

        /// <summary>
        /// 从选项状态计算返回值，便于单元测试逻辑（不依赖 UI）。
        /// </summary>
        public static ExportDimension? FromSelection(bool? byProject, bool? byOrder)
        {
            if (byProject == true && byOrder != true) return ExportDimension.ByProject;
            if (byOrder == true && byProject != true) return ExportDimension.ByOrder;
            return null;
        }

        private ExportDimension? GetSelectedDimension()
        {
            return FromSelection(RadioByProject.IsChecked, RadioByOrder.IsChecked);
        }

        private void BtnOk_OnClick(object sender, RoutedEventArgs e)
        {
            if (GetSelectedDimension() == null) return;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
