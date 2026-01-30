/// <author>AI Assistant</author>
/// <remarks>
/// Phase 2.6：列表选择 WPF 对话框，替代 WinForms ChooseFromList。支持搜索过滤、Enter/Esc、样式与 ExportDimensionDialog 一致。
/// </remarks>
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SnVerify.Views.Dialogs
{
    /// <summary>
    /// 从列表中选择一项。支持搜索过滤；未选时确定禁用；Enter 确定，Esc 取消。
    /// </summary>
    public partial class ChooseFromListDialog : Window
    {
        private readonly string[] _allItems;
        private string _lastSearchText = null; // null 表示尚未应用过滤，首次 Loaded 会填充列表

        public ChooseFromListDialog(string title, string[] items)
        {
            InitializeComponent();
            Title = title ?? "选择";
            TxtInstruction.Text = "请" + (title ?? "选择");
            _allItems = items ?? new string[0];
            Loaded += (s, e) =>
            {
                ApplyFilter();
                if (ListItems.Items.Count > 0)
                    ListItems.SelectedIndex = 0;
                UpdateOkState();
                UpdatePlaceholderVisibility();
            };
        }

        /// <summary>
        /// 当前选中项；未选或取消时为 null。ShowDialog 后由调用方读取。
        /// </summary>
        public string SelectedItem => ListItems.SelectedItem?.ToString();

        /// <summary>
        /// 根据关键词过滤列表，便于单元测试逻辑（不依赖 UI）。
        /// </summary>
        public static IEnumerable<string> FilterItems(string[] items, string searchText)
        {
            if (items == null || items.Length == 0) return new string[0];
            var term = (searchText ?? "").Trim();
            if (term.Length == 0) return items;
            return items.Where(s => (s ?? "").IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ApplyFilter()
        {
            var term = (TxtSearch?.Text ?? "").Trim();
            if (term == _lastSearchText) return;
            _lastSearchText = term;
            var filtered = FilterItems(_allItems, term).ToArray();
            ListItems.ItemsSource = filtered;
            if (filtered.Length > 0)
                ListItems.SelectedIndex = 0;
            else
                ListItems.SelectedIndex = -1;
            UpdateOkState();
        }

        private void UpdatePlaceholderVisibility()
        {
            if (TxtPlaceholder == null) return;
            TxtPlaceholder.Visibility = string.IsNullOrWhiteSpace(TxtSearch?.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void TxtSearch_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
            UpdatePlaceholderVisibility();
        }

        private void UpdateOkState()
        {
            if (BtnOk != null)
                BtnOk.IsEnabled = ListItems?.SelectedIndex >= 0;
        }

        private void ListItems_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOkState();
        }

        private void ListItems_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListItems.SelectedIndex >= 0)
            {
                DialogResult = true;
                Close();
            }
        }

        private void TxtSearch_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ListItems.SelectedIndex >= 0)
            {
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }

        private void BtnOk_OnClick(object sender, RoutedEventArgs e)
        {
            if (ListItems.SelectedIndex < 0) return;
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
