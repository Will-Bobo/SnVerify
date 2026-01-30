/// <summary>
/// Phase 2.6 Step 1：验证导出维度选择逻辑返回值（未选 → null，按项目 → ByProject，按订单 → ByOrder）。
/// 测试目标为逻辑返回值，非 UI 自动化。
/// </summary>
using NUnit.Framework;
using SnVerify.Services.Ui;
using SnVerify.Views.Dialogs;

namespace SnVerify.Tests.Ui
{
    [TestFixture]
    public class ExportDimensionDialogTests
    {
        [Test]
        public void FromSelection_WhenNothingSelected_ReturnsNull()
        {
            var result = ExportDimensionDialog.FromSelection(null, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FromSelection_WhenBothUnchecked_ReturnsNull()
        {
            var result = ExportDimensionDialog.FromSelection(false, false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FromSelection_WhenByProjectSelected_ReturnsByProject()
        {
            var result = ExportDimensionDialog.FromSelection(true, false);
            Assert.That(result, Is.EqualTo(ExportDimension.ByProject));
        }

        [Test]
        public void FromSelection_WhenByOrderSelected_ReturnsByOrder()
        {
            var result = ExportDimensionDialog.FromSelection(false, true);
            Assert.That(result, Is.EqualTo(ExportDimension.ByOrder));
        }
    }
}
