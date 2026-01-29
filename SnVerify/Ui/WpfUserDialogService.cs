/// <author>AI Assistant</author>
/// <remarks>
/// WPF/WinForms 具体 UI 交互实现（阶段 3）。
/// 约束：ViewModel 不直接调用 MessageBox/FolderDialog；统一经由 IUserDialogService。
/// </remarks>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SnVerify.Domain.Models;
using SnVerify.Services.Ui;

namespace SnVerify.Ui
{
    /// <summary>
    /// WPF 交互实现：MessageBox + 简单选择窗体（WinForms）。
    /// </summary>
    public class WpfUserDialogService : IUserDialogService
    {
        /// <inheritdoc />
        public ExportDimension? ChooseExportDimension()
        {
            var result = MessageBox.Show(
                "请选择导出维度：\n\n是 = 按项目导出\n否 = 按订单导出\n取消 = 取消导出",
                "导出维度选择",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) return ExportDimension.ByProject;
            if (result == MessageBoxResult.No) return ExportDimension.ByOrder;
            return null;
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

            using (var dialog = new System.Windows.Forms.Form
            {
                Text = title ?? "选择",
                Width = 420,
                Height = 420,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
            })
            {
                string selected = null;
                var listBox = new System.Windows.Forms.ListBox
                {
                    Dock = System.Windows.Forms.DockStyle.Fill
                };
                listBox.Items.AddRange(items);

                var btnOk = new System.Windows.Forms.Button { Text = "确定", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 30 };
                var btnCancel = new System.Windows.Forms.Button { Text = "取消", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 30 };

                btnOk.Click += (s, e) =>
                {
                    selected = listBox.SelectedItem?.ToString();
                    dialog.DialogResult = System.Windows.Forms.DialogResult.OK;
                };
                btnCancel.Click += (s, e) => dialog.DialogResult = System.Windows.Forms.DialogResult.Cancel;

                dialog.Controls.Add(listBox);
                dialog.Controls.Add(btnOk);
                dialog.Controls.Add(btnCancel);

                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? selected : null;
            }
        }
    }
}

