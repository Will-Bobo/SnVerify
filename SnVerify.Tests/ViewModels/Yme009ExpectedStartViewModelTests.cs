using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Product;
using SnVerify.Domain.State;
using SnVerify.Services.ExpectedData;
using SnVerify.Services.Session;
using SnVerify.Services.Storage;
using SnVerify.Services.Ui;
using SnVerify.ViewModels;

namespace SnVerify.Tests.ViewModels
{
    [TestFixture]
    public class Yme009ExpectedStartViewModelTests
    {
        [Test]
        public async Task Should_UseExistingStart_When_NoExcelIsConfigured()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out var session, out _, out var storage, expected.Object);
            Prepare(vm, session, storage, "ORD-YME_20261125_090000");

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.SessionSnapshot != null && vm.SessionSnapshot.IsActive);

            session.Verify(s => s.CreateAndStartSession("ORD", "ORD", "P", Yme009Product.Code), Times.Once);
            expected.Verify(s => s.StartAsync(It.IsAny<Yme009ExpectedStartRequest>()), Times.Never);
            Assert.That(vm.Yme009StatusTitle, Is.Not.EqualTo(Yme009ExpectedOperatorText.ImportingTitle));
            Assert.That(vm.Yme009StatusTitle, Is.Not.EqualTo(Yme009ExpectedOperatorText.ReadyTitle));
        }

        [Test]
        public async Task Should_ShowNameAndCount_When_SelectedWorkbookPassesPrecheck()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out _, out var dialog, out _, expected.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            dialog.Setup(d => d.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(@"D:\expected.xlsx");
            expected.Setup(s => s.StageAsync(@"D:\expected.xlsx")).ReturnsAsync(Accepted(@"D:\expected.xlsx", "expected.xlsx", 6));

            vm.SelectYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => vm.Yme009ExpectedSummary.Contains("expected.xlsx"));

            Assert.That(vm.Yme009ExpectedSummary, Does.Contain("6"));
            dialog.Verify(d => d.ShowWarning(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Should_KeepPreviousStaging_When_ReplacementPrecheckFails()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out _, out var dialog, out _, expected.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            dialog.SetupSequence(d => d.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(@"D:\good.xlsx")
                .Returns(@"D:\bad.xlsx");
            expected.Setup(s => s.StageAsync(@"D:\good.xlsx")).ReturnsAsync(Accepted(@"D:\good.xlsx", "good.xlsx", 2));
            expected.Setup(s => s.StageAsync(@"D:\bad.xlsx")).ReturnsAsync(new Yme009ExpectedStageResult(
                Yme009ExpectedReadStatus.Rejected,
                Yme009ExpectedReadReasons.RequiredHeaderMissing,
                null,
                Yme009ExpectedOperatorText.ForPrecheck(Yme009ExpectedReadReasons.RequiredHeaderMissing)));

            var warned = false;
            dialog.Setup(d => d.ShowWarning(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => warned = true);
            vm.SelectYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => vm.Yme009ExpectedSummary.Contains("good.xlsx"));
            vm.SelectYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => warned);

            Assert.That(vm.Yme009ExpectedSummary, Does.Contain("good.xlsx"));
            dialog.Verify(d => d.ShowWarning(
                It.Is<string>(text => text.Contains("表头")),
                Yme009ExpectedOperatorText.ImportFailedTitle), Times.Once);
        }

        [Test]
        public async Task Should_NotStageUnresolvedFile_When_WorkbookHasNoDeviceRows()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out _, out var dialog, out _, expected.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            dialog.Setup(d => d.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(@"D:\header.xlsx");
            expected.Setup(s => s.StageAsync(@"D:\header.xlsx")).ReturnsAsync(new Yme009ExpectedStageResult(
                Yme009ExpectedReadStatus.Unresolved,
                Yme009ExpectedReadReasons.UnresolvedNoDeviceRows,
                null,
                Yme009ExpectedOperatorText.UnresolvedFile));

            var warned = false;
            dialog.Setup(d => d.ShowWarning(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => warned = true);
            vm.SelectYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => warned);

            Assert.That(vm.Yme009ExpectedSummary, Is.EqualTo("未选择"));
            dialog.Verify(d => d.ShowWarning(
                Yme009ExpectedOperatorText.UnresolvedFile,
                Yme009ExpectedOperatorText.UnresolvedTitle), Times.Once);
            dialog.Verify(d => d.ShowWarning(It.IsAny<string>(), Yme009ExpectedOperatorText.ImportFailedTitle), Times.Never);
        }

        [Test]
        public async Task Should_ClearMemoryOnly_When_OperatorClearsExcel()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out _, out var dialog, out var storage, expected.Object);
            vm.SelectedProductCode = Yme009Product.Code;
            dialog.Setup(d => d.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(@"D:\expected.xlsx");
            expected.Setup(s => s.StageAsync(@"D:\expected.xlsx")).ReturnsAsync(Accepted(@"D:\expected.xlsx", "expected.xlsx", 1));
            vm.SelectYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => vm.Yme009ExpectedSummary.Contains("expected.xlsx"));

            vm.ClearYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => vm.Yme009ExpectedSummary == "未选择");

            storage.Verify(s => s.ImportYme009ExpectedDataAsync(It.IsAny<Yme009ExpectedImportRequest>()), Times.Never);
        }

        [Test]
        public async Task Should_ActivateOnlyAfterCommit_When_ExcelImportSucceeds()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out var session, out var dialog, out var storage, expected.Object);
            const string sessionName = "ORD_20261125_090000";
            Prepare(vm, session, storage, sessionName);
            await StageAsync(vm, dialog, expected, @"D:\expected.xlsx", "expected.xlsx", 3);
            var activated = false;
            expected.Setup(s => s.StartAsync(It.IsAny<Yme009ExpectedStartRequest>()))
                .Returns(async () =>
                {
                    await Task.Delay(30);
                    Assert.That(activated, Is.False);
                    Assert.That(vm.Yme009StatusTitle, Is.EqualTo(Yme009ExpectedOperatorText.ImportingTitle));
                    Assert.That(vm.Yme009StatusDetail, Is.EqualTo(Yme009ExpectedOperatorText.ImportingDetail));
                    return new Yme009ExpectedStartResult(true, sessionName, 11, 3, null, "预期数据导入完成。共 3 条。");
                });
            session.Setup(s => s.StartSession(sessionName, "ORD"))
                .Callback(() =>
                {
                    activated = true;
                    session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD", DateTime.Now));
                });

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.Yme009StatusTitle == Yme009ExpectedOperatorText.ReadyTitle, 4000);

            session.Verify(s => s.CreateAndStartSession(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            session.Verify(s => s.StartSession(sessionName, "ORD"), Times.Once);
            Assert.That(vm.SessionSnapshot.IsActive, Is.True);
            Assert.That(vm.IsYme009ExpectedImporting, Is.False);
        }

        [Test]
        public async Task Should_NotActivate_When_ImportFails()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out var session, out var dialog, out var storage, expected.Object);
            Prepare(vm, session, storage, "ORD_20261125_090001");
            await StageAsync(vm, dialog, expected, @"D:\expected.xlsx", "expected.xlsx", 1);
            expected.Setup(s => s.StartAsync(It.IsAny<Yme009ExpectedStartRequest>()))
                .ReturnsAsync(new Yme009ExpectedStartResult(
                    false, string.Empty, 0, 0,
                    Yme009ExpectedReadReasons.FileUnreadable,
                    Yme009ExpectedOperatorText.FileUnavailable));

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => !vm.IsYme009ExpectedImporting && vm.Yme009StatusTitle == "等待扫码", 3000);

            session.Verify(s => s.StartSession(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            session.Verify(s => s.CreateAndStartSession(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Assert.That(vm.SessionSnapshot == null || !vm.SessionSnapshot.IsActive, Is.True);
            dialog.Verify(d => d.ShowWarning(
                Yme009ExpectedOperatorText.FileUnavailable,
                Yme009ExpectedOperatorText.StartFailedTitle), Times.Once);
        }

        [Test]
        public async Task Should_NotStartTwice_When_StartIsClickedAgainDuringImport()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out var session, out var dialog, out var storage, expected.Object);
            const string sessionName = "ORD_20261125_090002";
            Prepare(vm, session, storage, sessionName);
            await StageAsync(vm, dialog, expected, @"D:\expected.xlsx", "expected.xlsx", 1);
            var gate = new TaskCompletionSource<Yme009ExpectedStartResult>();
            var calls = 0;
            expected.Setup(s => s.StartAsync(It.IsAny<Yme009ExpectedStartRequest>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref calls);
                    return gate.Task;
                });
            session.Setup(s => s.StartSession(sessionName, "ORD"))
                .Callback(() => session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD", DateTime.Now)));

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.IsYme009ExpectedImporting);
            vm.StartBatchCommand.Execute(null);
            await Task.Delay(50);

            Assert.That(calls, Is.EqualTo(1));
            gate.SetResult(new Yme009ExpectedStartResult(true, sessionName, 12, 1, null, "预期数据导入完成。共 1 条。"));
            await WaitUntilAsync(() => vm.Yme009StatusTitle == Yme009ExpectedOperatorText.ReadyTitle, 4000);
            session.Verify(s => s.StartSession(sessionName, "ORD"), Times.Once);
        }

        [Test]
        public async Task Should_LockExcelCommands_When_SessionIsActive()
        {
            var expected = new Mock<IYme009ExpectedDataService>();
            var vm = Yme009ProductAccessTests.CreateForExpectedData(out var session, out var dialog, out var storage, expected.Object);
            const string sessionName = "ORD_20261125_090003";
            Prepare(vm, session, storage, sessionName);
            await StageAsync(vm, dialog, expected, @"D:\expected.xlsx", "expected.xlsx", 1);
            expected.Setup(s => s.StartAsync(It.IsAny<Yme009ExpectedStartRequest>()))
                .ReturnsAsync(new Yme009ExpectedStartResult(true, sessionName, 13, 1, null, "预期数据导入完成。共 1 条。"));
            session.Setup(s => s.StartSession(sessionName, "ORD"))
                .Callback(() => session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD", DateTime.Now)));

            vm.StartBatchCommand.Execute(null);
            await WaitUntilAsync(() => vm.IsSessionActive, 4000);

            vm.SelectYme009ExpectedFileCommand.Execute(null);
            vm.ClearYme009ExpectedFileCommand.Execute(null);
            await Task.Delay(50);

            Assert.That(vm.IsYme009ExpectedConfigLocked, Is.True);
            Assert.That(vm.Yme009ExpectedSummary, Does.Contain("expected.xlsx"));
            dialog.Verify(d => d.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        private static void Prepare(
            MainViewModel vm,
            Mock<ISessionLifecycleService> session,
            Mock<IStorageService> storage,
            string sessionName)
        {
            vm.SelectedProductCode = Yme009Product.Code;
            vm.ProjectIdInput = "P";
            vm.OrderIdInput = "ORD";
            vm.ExpectedAndroidVersion = "SW-1";
            storage.Setup(s => s.ProjectNameExistsAsync("P")).ReturnsAsync(false);
            session.Setup(s => s.CreateAndStartSession("ORD", "ORD", "P", Yme009Product.Code))
                .Callback(() => session.Setup(x => x.Snapshot).Returns(SessionSnapshot.Active(sessionName, "ORD", DateTime.Now)))
                .Returns(sessionName);
        }

        private static async Task StageAsync(
            MainViewModel vm,
            Mock<IUserDialogService> dialog,
            Mock<IYme009ExpectedDataService> expected,
            string path,
            string fileName,
            int count)
        {
            dialog.Setup(d => d.ChooseOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(path);
            expected.Setup(s => s.StageAsync(path)).ReturnsAsync(Accepted(path, fileName, count));
            vm.SelectYme009ExpectedFileCommand.Execute(null);
            await WaitUntilAsync(() => vm.Yme009ExpectedSummary.Contains(fileName));
        }

        private static Yme009ExpectedStageResult Accepted(string path, string fileName, int count)
        {
            return new Yme009ExpectedStageResult(
                Yme009ExpectedReadStatus.Accepted,
                null,
                new Yme009ExpectedFileStaging(path, fileName, count, "abc123"),
                fileName + "（" + count + " 条）");
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000)
        {
            var start = DateTime.UtcNow;
            while (!predicate())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");
                await Task.Delay(20);
            }
        }
    }
}
