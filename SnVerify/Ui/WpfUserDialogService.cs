/// <author>AI Assistant</author>
/// <remarks>
/// WPF/WinForms 具体 UI 交互实现（阶段 3）。
/// 约束：ViewModel 不直接调用 MessageBox/FolderDialog；统一经由 IUserDialogService。
/// </remarks>
#pragma warning disable CS8632
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;
using SnVerify.Services.Ui;
using SnVerify.Views.Dialogs;

namespace SnVerify.Ui
{
    /// <summary>
    /// WPF 交互实现：导出维度 / 列表选择等均使用 WPF 对话框。
    /// </summary>
    public class WpfUserDialogService : IUserDialogService
    {
        /// <inheritdoc />
        public ExportRecordFilter? ChooseExportRecordFilter(IReadOnlyList<VerificationType> defaultTypes = null)
        {
            var dialog = new ExportRecordFilterDialog();
            if (defaultTypes != null && defaultTypes.Count > 0)
                dialog.InitializeDefaultTypes(defaultTypes);
            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true)
                return null;
            return dialog.SelectedFilter;
        }

        /// <inheritdoc />
        public ExportDimension? ChooseExportDimension()
        {
            var dialog = new ExportDimensionDialog();
            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true)
                return null;
            return dialog.SelectedDimension;
        }

        /// <inheritdoc />
        public string ChooseProjectId(IReadOnlyList<string> projectIds)
        {
            if (projectIds == null || projectIds.Count == 0) return null;
            return ChooseFromList("选择项目", projectIds.ToArray());
        }

        /// <inheritdoc />
        public Order ChooseOrder(IReadOnlyList<Order> orders)
        {
            if (orders == null || orders.Count == 0) return null;
            // 展示：OrderName（Phase 2.5 Order 模型无 OrderId）
            var items = orders
                .Select(o => new KeyValuePair<string, Order>(o?.OrderName ?? "", o))
                .Where(kv => kv.Value != null)
                .ToArray();
            var selected = ChooseFromList("选择订单", items.Select(i => i.Key).ToArray());
            if (string.IsNullOrEmpty(selected)) return null;
            return items.FirstOrDefault(i => i.Key == selected).Value;
        }

        /// <inheritdoc />
        public string ChooseFolder(string description, string initialPath = null)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = description ?? "请选择文件夹";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(initialPath))
                    dialog.SelectedPath = initialPath;
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        /// <inheritdoc />
        public bool ConfirmOverwrite(string message)
        {
            var result = MessageBox.Show(
                message ?? "目标文件夹中已存在同名文件，是否覆盖？",
                "覆盖确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        /// <inheritdoc />
        public void ShowInfo(string message, string title = "提示")
        {
            MessageBox.Show(message ?? "", title ?? "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <inheritdoc />
        public void ShowWarning(string message, string title = "警告")
        {
            MessageBox.Show(message ?? "", title ?? "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string ChooseFromList(string title, string[] items)
        {
            if (items == null || items.Length == 0) return null;
            var dialog = new ChooseFromListDialog(title ?? "选择", items);
            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true)
                return null;
            return dialog.SelectedItem;
        }
    }
}

