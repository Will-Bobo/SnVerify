/// <author>AI Assistant</author>
/// <remarks>
/// UI 交互抽象（阶段 3）：用于在 ViewModel 中避免直接调用 MessageBox/WinForms/Dispatcher。
/// 该接口不引用任何 WPF 类型，便于单元测试替身实现。
/// </remarks>
#pragma warning disable CS8632 // 可为 null 的引用类型在未启用 #nullable 时会有此警告
using System.Collections.Generic;
using SnVerify.Domain.Enums;
using SnVerify.Domain.Export;
using SnVerify.Domain.Models;

namespace SnVerify.Services.Ui
{
    /// <summary>
    /// 导出维度枚举（阶段 3 C1.2：「选维度 → 选对象 → 执行」）。
    /// </summary>
    public enum ExportDimension
    {
        /// <summary>按项目导出</summary>
        ByProject,
        /// <summary>按订单导出</summary>
        ByOrder
    }

    /// <summary>
    /// UI 交互服务：所有需要弹窗/选择器的交互都应通过该抽象完成。
    /// </summary>
    public interface IUserDialogService
    {
        /// <summary>
        /// 选择导出维度；返回 null 表示用户取消。
        /// </summary>
        ExportDimension? ChooseExportDimension();

        /// <summary>
        /// 选择导出记录过滤（SN / 版本 / 全部）；返回 null 表示用户取消。
        /// </summary>
        /// <param name="defaultTypes">可选，Session/记录的 VerificationType 列表，用于设置默认勾选</param>
        ExportRecordFilter? ChooseExportRecordFilter(IReadOnlyList<VerificationType> defaultTypes = null);

        /// <summary>
        /// 选择项目 ID；返回 null 表示用户取消。
        /// </summary>
        string ChooseProjectId(IReadOnlyList<string> projectIds);

        /// <summary>
        /// 选择订单；返回 null 表示用户取消。
        /// </summary>
        Order ChooseOrder(IReadOnlyList<Order> orders);

        /// <summary>
        /// 选择导出文件夹；返回 null 表示用户取消。
        /// </summary>
        string ChooseFolder(string description, string initialPath = null);

        /// <summary>
        /// 覆盖确认；true 表示允许覆盖，false 表示取消。
        /// </summary>
        bool ConfirmOverwrite(string message);

        /// <summary>
        /// 信息提示。
        /// </summary>
        void ShowInfo(string message, string title = "提示");

        /// <summary>
        /// 警告提示。
        /// </summary>
        void ShowWarning(string message, string title = "警告");
    }
}

