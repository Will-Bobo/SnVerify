/// <summary>
/// Phase 2.6：验证列表选择对话框过滤逻辑与返回值。测试目标为逻辑与过滤行为，非 UI 自动化。
/// </summary>
using System.Linq;
using NUnit.Framework;
using SnVerify.Views.Dialogs;

namespace SnVerify.Tests.Ui
{
    [TestFixture]
    public class ChooseFromListDialogTests
    {
        [Test]
        public void FilterItems_WhenItemsNull_ReturnsEmpty()
        {
            var result = ChooseFromListDialog.FilterItems(null, "").ToArray();
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FilterItems_WhenItemsEmpty_ReturnsEmpty()
        {
            var result = ChooseFromListDialog.FilterItems(new string[0], "x").ToArray();
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FilterItems_WhenSearchEmpty_ReturnsAllItems()
        {
            var items = new[] { "项目A", "项目B", "订单X" };
            var result = ChooseFromListDialog.FilterItems(items, "").ToArray();
            Assert.That(result, Is.EqualTo(items));
        }

        [Test]
        public void FilterItems_WhenSearchNull_ReturnsAllItems()
        {
            var items = new[] { "项目A", "项目B" };
            var result = ChooseFromListDialog.FilterItems(items, null).ToArray();
            Assert.That(result, Is.EqualTo(items));
        }

        [Test]
        public void FilterItems_WhenSearchWhitespaceOnly_ReturnsAllItems()
        {
            var items = new[] { "项目A", "项目B" };
            var result = ChooseFromListDialog.FilterItems(items, "   ").ToArray();
            Assert.That(result, Is.EqualTo(items));
        }

        [Test]
        public void FilterItems_WhenKeywordMatches_ReturnsMatchingItems()
        {
            var items = new[] { "项目A", "项目B", "订单X", "订单Y" };
            var result = ChooseFromListDialog.FilterItems(items, "项目").ToArray();
            Assert.That(result, Is.EqualTo(new[] { "项目A", "项目B" }));
        }

        [Test]
        public void FilterItems_WhenKeywordCaseInsensitive_ReturnsMatchingItems()
        {
            var items = new[] { "OrderX", "OrderY", "ProjectZ" };
            var result = ChooseFromListDialog.FilterItems(items, "order").ToArray();
            Assert.That(result, Is.EqualTo(new[] { "OrderX", "OrderY" }));
        }

        [Test]
        public void FilterItems_WhenNoMatch_ReturnsEmpty()
        {
            var items = new[] { "项目A", "项目B" };
            var result = ChooseFromListDialog.FilterItems(items, "订单").ToArray();
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Dialog_Constructor_WithValidArgs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var d = new ChooseFromListDialog("选择项目", new[] { "P1", "P2" });
                Assert.That(d.Title, Is.EqualTo("选择项目"));
            });
        }

        [Test]
        public void Dialog_WithEmptyItems_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var d = new ChooseFromListDialog("选择", new string[0]);
                Assert.That(d.Title, Is.EqualTo("选择"));
            });
        }
    }
}
