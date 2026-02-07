/// <author>AI Assistant</author>
/// <remarks>
/// 导出内容类型选择：SN 检验 / 版本检验，至少勾选一项。
/// 默认勾选可由 InitializeDefaultTypes 根据 Session/记录的 VerificationType 动态设置。
/// </remarks>

using System.Collections.Generic;
using System.Windows;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Export;
using SnVerify.Services.Ui;

namespace SnVerify.Views.Dialogs
{
    /// <summary>
    /// 选择导出内容：SN 检验 / 版本检验，至少勾选一项。
    /// </summary>
    public partial class ExportRecordFilterDialog : Window
    {
        public ExportRecordFilterDialog()
        {
            InitializeComponent();
            ChkSnMatch.Checked += OnSelectionChanged;
            ChkSnMatch.Unchecked += OnSelectionChanged;
            ChkVersionMatch.Checked += OnSelectionChanged;
            ChkVersionMatch.Unchecked += OnSelectionChanged;
            // 默认：两个都勾选（All），由 InitializeDefaultTypes 可覆盖
            ChkSnMatch.IsChecked = true;
            ChkVersionMatch.IsChecked = true;
            BtnOk.IsEnabled = GetFilter() != null;
        }

        /// <summary>
        /// 根据 Session/记录的 VerificationType 设置默认勾选。
        /// - 仅 SnMatch → 勾选 SN，不勾选版本
        /// - 仅 VersionMatch → 勾选版本，不勾选 SN
        /// - 混合或空 → 两个都勾选
        /// </summary>
        public void InitializeDefaultTypes(IReadOnlyList<VerificationType> types)
        {
            var (snChecked, verChecked) = ExportRecordFilterDefaults.GetDefaultCheckState(types);
            ChkSnMatch.IsChecked = snChecked;
            ChkVersionMatch.IsChecked = verChecked;
            BtnOk.IsEnabled = GetFilter() != null;
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            BtnOk.IsEnabled = GetFilter() != null;
        }

        /// <summary>
        /// 当前选择对应的过滤；至少勾选一项才非 null。
        /// </summary>
        public ExportRecordFilter SelectedFilter => GetFilter();

        private ExportRecordFilter GetFilter()
        {
            var sn = ChkSnMatch.IsChecked == true;
            var ver = ChkVersionMatch.IsChecked == true;
            return ExportRecordFilterDefaults.ToFilter(sn, ver);
        }

        private void BtnOk_OnClick(object sender, RoutedEventArgs e)
        {
            if (GetFilter() == null) return;
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
