using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using SnVerify.Domain.Models;
using SnVerify.Domain.Product;
using SnVerify.Services.Logging;
using SnVerify.Services.Storage;
using SnVerify.Services.Yme009;
using SnVerify.Tests.Domain;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class Yme009DeviceAccessCoordinatorTests
    {
        [Test]
        public async Task Should_AcceptUniqueStationTransport_When_ComChangesBetweenExchanges()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("B", "COM5"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));

            var access = await harness.Coordinator.AccessAsync(CancellationToken.None);

            Assert.That(access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(access.FormalContextDelivered, Is.True);
            Assert.That(access.Connection.BuildDirectedArguments("version"), Is.EqualTo("-t 9 version"));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4" }));
            Assert.That(harness.Ports.Live, Is.EqualTo(0));

            var close = await harness.Coordinator.TryCloseAsync(CancellationToken.None);
            harness.Coordinator.Release();

            Assert.That(close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4", "COM5" }));
            Assert.That(harness.Ports.Live, Is.EqualTo(0));
            Assert.That(access.Connection.IsRevoked, Is.True);
            Assert.That(access.Connection.AllowsBusinessUse, Is.False);
            Assert.That(access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
        }

        [Test]
        public async Task Should_DeliverFormalContext_When_ExactlyOneDeviceStateHasTransport()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));

            var result = await harness.Round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(result.Access.Connection.Stage, Is.EqualTo(Yme009ConnectionStage.Formal));
            Assert.That(result.Access.Connection.TransportId, Is.EqualTo("9"));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(result.CleanedUp, Is.True);
            Assert.That(result.Access.Connection.IsRevoked, Is.True);
            Assert.That(harness.Coordinator.IsObserving, Is.False);
        }

        [Test]
        public async Task Should_NotSelectAmongDeviceStates_When_MoreThanOneIsCommunicable()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"), new Yme009AdbDevice("OTHER", "device", "8"));

            var result = await harness.Round.RunAsync("SN-MANY", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AssociationFailed));
            Assert.That(result.Access.FormalContextDelivered, Is.False);
            Assert.That(result.Access.Connection.TransportId, Is.Empty);
            Assert.That(result.Access.FailureDetail, Does.Contain("数量=2"));
            Assert.That(() => result.Access.Connection.BuildDirectedArguments("version"), Throws.InvalidOperationException);
            Assert.That(string.Join("\n", harness.Lines), Does.Contain("多设备拒绝，device数量=2"));
            Assert.That(result.CleanedUp, Is.True);
            Assert.That(result.Access.Connection.IsRevoked, Is.True);
        }

        [Test]
        public async Task Should_NotDeliverFormalContext_When_UniqueDeviceHasNoTransport()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM6"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", " "));

            var result = await harness.Round.RunAsync("SN-NO-TID", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AssociationFailed));
            Assert.That(result.Access.FormalContextDelivered, Is.False);
            Assert.That(result.Access.Connection.TransportId, Is.Empty);
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AssociationFailed));
            Assert.That(string.Join("\n", harness.Lines), Does.Contain("Transport ID 无效"));
        }

        [Test]
        public async Task Should_AcceptTransport_When_DeviceAppearsAfterAnEmptyQuery()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Adb.Sequence.Enqueue(Query());
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));

            var result = await harness.Round.RunAsync("SN-LATER", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(result.Access.Connection.TransportId, Is.EqualTo("9"));
            Assert.That(harness.Adb.Calls, Is.EqualTo(2));
        }

        [Test]
        public async Task Should_SendDisable_When_CdcIdentityChangesButCurrentPortIsUnique()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("B", "COM9"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));

            var result = await harness.Round.RunAsync("SN-CDC-CHANGE", CancellationToken.None);
            var text = string.Join("\n", harness.Lines);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4", "COM9" }));
            Assert.That(text, Does.Contain("不比较 Enable 接口实例"));
            Assert.That(text, Does.Contain("Disable 当前 CDC 唯一且 COM 有效，COM=COM9"));
            Assert.That(text, Does.Contain("资源清理开始"));
            Assert.That(text, Does.Contain("正式上下文已作废"));
        }

        [Test]
        public async Task Should_SkipDisable_When_CurrentCdcIsMissingOrNotUniqueOrPortIsInvalid()
        {
            var missing = CreateHarness();
            missing.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), Yme009CdcScan.Found(new Yme009CdcCandidate[0]));
            missing.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));
            var missingResult = await missing.Round.RunAsync("SN-CDC-0", CancellationToken.None);
            Assert.That(missingResult.Access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(missingResult.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Skipped));
            Assert.That(missingResult.CleanedUp, Is.True);

            var many = CreateLoggedHarness();
            many.Discovery.Enqueue(
                One("A", "COM3"),
                One("A", "COM3"),
                One("A", "COM3"),
                One("A", "COM4"),
                Yme009CdcScan.Found(new[] { new Yme009CdcCandidate("B", "COM8"), new Yme009CdcCandidate("C", "COM9") }));
            many.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));
            var manyResult = await many.Round.RunAsync("SN-CDC-2", CancellationToken.None);
            Assert.That(manyResult.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Skipped));
            Assert.That(string.Join("\n", many.Lines), Does.Contain("当前不是唯一设备，数量=2"));
            Assert.That(manyResult.Access.Connection.IsRevoked, Is.True);

            var invalidPort = CreateLoggedHarness();
            invalidPort.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("B", "LPT1"));
            invalidPort.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));
            var invalidResult = await invalidPort.Round.RunAsync("SN-COM", CancellationToken.None);
            Assert.That(invalidResult.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Skipped));
            Assert.That(invalidPort.Ports.Opened, Is.EqualTo(new[] { "COM4" }));
            Assert.That(string.Join("\n", invalidPort.Lines), Does.Contain("COM无效"));
        }

        [Test]
        public async Task Should_SkipDisable_When_CurrentPortCannotOpen()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("B", "COM9"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));
            harness.Ports.ThrowOnOpenNumber = 2;

            var result = await harness.Round.RunAsync("SN-OPEN", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Skipped));
            Assert.That(string.Join("\n", harness.Lines), Does.Contain("串口打开失败，命令未发送"));
            Assert.That(result.Access.Connection.IsRevoked, Is.True);
        }

        [Test]
        public async Task Should_NotSendEnable_When_RediscoveredIdentityDiffers()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("B", "COM4"));

            var result = await harness.Round.RunAsync("SN-ENABLE-ID", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AtNotSent));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.NotAttempted));
            Assert.That(harness.Ports.Opened, Is.Empty);
            Assert.That(string.Join("\n", harness.Lines), Does.Contain("身份不匹配"));
        }

        [Test]
        public async Task Should_NotClose_When_AtResultIsUncertain()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"));
            harness.Ports.Responses.Enqueue("ERROR");

            var result = await harness.Round.RunAsync("SN-2", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AtUncertain));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.NotAttempted));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4" }));
            Assert.That(harness.Ports.Live, Is.EqualTo(0));
        }

        [Test]
        public async Task Should_NotOpenPort_When_EnableRediscoveryIsNotUnique()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(
                One("A", "COM3"),
                One("A", "COM3"),
                One("A", "COM3"),
                Yme009CdcScan.Found(new[]
                {
                    new Yme009CdcCandidate("A", "COM4"),
                    new Yme009CdcCandidate("B", "COM5")
                }));

            var result = await harness.Round.RunAsync("SN-3", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AtNotSent));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.NotAttempted));
            Assert.That(harness.Ports.Opened, Is.Empty);
        }

        [Test]
        public async Task Should_TimeOutWithoutAt_When_NoUniqueDeviceAppears()
        {
            var harness = CreateHarness();

            var result = await harness.Round.RunAsync("SN-4", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.WaitTimedOut));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.NotAttempted));
            Assert.That(harness.Ports.Opened, Is.Empty);
            Assert.That(harness.Adb.Calls, Is.EqualTo(0));
        }

        [Test]
        public async Task Should_CloseAfterReadyFailure_When_NoCommunicableDeviceAppears()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM6"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "offline", "9"));

            var result = await harness.Round.RunAsync("SN-5", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.ReadyFailed));
            Assert.That(result.Access.FormalContextDelivered, Is.False);
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4", "COM6" }));
        }

        [Test]
        public async Task Should_StillCloseWithCleanupToken_When_WorkTokenIsCancelledAfterAt()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM7"));
            var cts = new CancellationTokenSource();
            harness.Adb.OnQuery = _ => cts.Cancel();

            var result = await harness.Round.RunAsync("SN-6", cts.Token);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.StoppedAfterAt));
            Assert.That(result.Access.AtConfirmed, Is.True);
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4", "COM7" }));
            Assert.That(result.Access.Connection.IsRevoked, Is.True);
        }

        [Test]
        public async Task Should_KeepAccessResult_When_CloseResponseIsUncertain()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Ports.Responses.Enqueue(OkResponse);
            harness.Ports.Responses.Enqueue("NO");
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "offline", "1"));

            var result = await harness.Round.RunAsync("SN-7", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.ReadyFailed));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Failed));
            Assert.That(result.CleanedUp, Is.True);
            Assert.That(harness.Ports.Live, Is.EqualTo(0));
        }

        [Test]
        public async Task Should_RecordReadyAndCloseTimeline_When_OnlyOfflineDeviceAppears()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM6"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "offline", "9"));

            var result = await harness.Round.RunAsync("SN-LOG", CancellationToken.None);
            var text = string.Join("\n", harness.Lines);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.ReadyFailed));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(text, Does.Contain("[YME009][Round="));
            Assert.That(text, Does.Contain("[SN=SN-LOG]"));
            Assert.That(text, Does.Contain("CDC门禁通过。发送前复核通过"));
            Assert.That(text, Does.Contain("与上次身份一致=是"));
            Assert.That(text, Does.Contain("Enable 串口打开成功，COM=COM4"));
            Assert.That(text, Does.Contain("Enable 收到符合规则的成功响应"));
            Assert.That(text, Does.Contain("ADB Ready结束，分类=C"));
            Assert.That(text, Does.Contain("未选定唯一 ADB Transport"));
            Assert.That(text, Does.Contain("进入关闭策略，选择临时上下文"));
            Assert.That(text, Does.Contain("关闭结果=Confirmed"));
            Assert.That(text, Does.Contain("资源清理开始"));
            Assert.That(text, Does.Contain("临时上下文已作废"));
            Assert.That(text, Does.Contain("结果=ADB 未就绪"));
        }

        [Test]
        public async Task Should_RecordUncertainClose_When_DisableResponseDoesNotMatch()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Ports.Responses.Enqueue(OkResponse);
            harness.Ports.Responses.Enqueue("NO\r\n");
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "offline", "1"));

            var result = await harness.Round.RunAsync("SN-UNCERTAIN", CancellationToken.None);
            var text = string.Join("\n", harness.Lines);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.ReadyFailed));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Failed));
            Assert.That(text, Does.Contain("Disable 收到响应但格式不符合规则"));
            Assert.That(text, Does.Contain("原始响应=NO\\r\\n"));
            Assert.That(text, Does.Contain("关闭结果=Uncertain"));
            Assert.That(text, Does.Contain("结果=ADB 未就绪"));
        }

        [Test]
        public async Task Should_RecordTimeout_When_EnableResponseIsEmpty()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"));
            harness.Ports.Responses.Enqueue(string.Empty);

            var result = await harness.Round.RunAsync("SN-TIMEOUT", CancellationToken.None);
            var text = string.Join("\n", harness.Lines);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AtUncertain));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.NotAttempted));
            Assert.That(text, Does.Contain("AT命令已写入"));
            Assert.That(text, Does.Contain("命令已发送但响应超时"));
            Assert.That(text, Does.Contain("关闭策略未进入发送"));
        }

        [Test]
        public async Task Should_RecordPortNotOpened_When_EnableRediscoveryIsNotUnique()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.Enqueue(
                One("A", "COM3"),
                One("A", "COM3"),
                One("A", "COM3"),
                Yme009CdcScan.Found(new[]
                {
                    new Yme009CdcCandidate("A", "COM4"),
                    new Yme009CdcCandidate("B", "COM5")
                }));

            var result = await harness.Round.RunAsync("SN-PORT", CancellationToken.None);
            var text = string.Join("\n", harness.Lines);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AtNotSent));
            Assert.That(text, Does.Contain("当前不是唯一设备，数量=2"));
            Assert.That(text, Does.Contain("串口未打开，命令未发送"));
        }

        [Test]
        public async Task Should_RecordDiscoveryException_When_ScanThrows()
        {
            var harness = CreateLoggedHarness();
            harness.Discovery.ThrowNext = new InvalidOperationException("setup-broken");

            var result = await harness.Round.RunAsync("SN-EX", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.WaitTimedOut));
            Assert.That(string.Join("\n", harness.Lines), Does.Contain("CDC发现抛出异常：InvalidOperationException setup-broken"));
        }

        [Test]
        public async Task Should_SaveThenDisable_When_InspectionPasses()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            var openedAtSave = 0;
            var round = CreateInspectionRound(harness, PassRaw("SN-1"), saved, record =>
            {
                openedAtSave = harness.Ports.Opened.Count;
                record.Id = 11;
            });

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(result.RecordSaved, Is.True);
            Assert.That(result.RecordId, Is.EqualTo(11));
            Assert.That(result.OperatorText, Does.Contain("综合=PASS").And.Contain("记录已保存 Id=11"));
            Assert.That(saved[0].DeviceSN, Is.EqualTo("SN-1"));
            Assert.That(saved[0].ActualVersion, Is.EqualTo("SW-1"));
            Assert.That(saved[0].CitValue, Is.EqualTo("1"));
            Assert.That(saved[0].ChipId, Is.Null);
            Assert.That(openedAtSave, Is.EqualTo(1));
            Assert.That(harness.Ports.Opened, Is.EqualTo(new[] { "COM4", "COM5" }));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(result.Access.Connection.IsRevoked, Is.True);
        }

        [Test]
        public async Task Should_SaveBusinessFailAndStillDisable_When_ProductionTestIsZero()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            var round = CreateInspectionRound(harness, new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded("SN-1")).ProductionTest(Yme009FieldRaw.Succeeded("0")).Build(), saved, record => record.Id = 12);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(saved[0].Result, Is.EqualTo("FAIL"));
            Assert.That(saved[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.CitFail));
            Assert.That(result.OperatorText, Does.Contain("综合=FAIL").And.Contain("记录已保存"));
            Assert.That(result.OperatorText, Does.Not.Contain("读取异常"));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
        }

        [Test]
        public async Task Should_SaveReadException_When_RequiredFieldTimesOut()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            var round = CreateInspectionRound(harness, new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.TimedOut()).Build(), saved, record => record.Id = 13);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(saved[0].Result, Is.EqualTo("FAIL"));
            Assert.That(saved[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.ReadFail));
            Assert.That(saved[0].DeviceSN, Is.Null);
            Assert.That(saved[0].SnReadStatus, Is.EqualTo("TimedOut"));
            Assert.That(result.OperatorText, Does.Contain("读取异常").And.Contain("记录已保存"));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
        }

        [Test]
        public async Task Should_KeepPass_When_InformationFieldFails()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            var raw = new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded("SN-1")).Imei1(Yme009FieldRaw.CommandFailed()).Build();
            var round = CreateInspectionRound(harness, raw, saved, record => record.Id = 14);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(saved[0].Result, Is.EqualTo("PASS"));
            Assert.That(saved[0].FailReason, Is.Null);
            Assert.That(saved[0].Imei1, Is.Null);
            Assert.That(saved[0].Imei1ReadStatus, Is.EqualTo("CommandFailed"));
            Assert.That(result.OperatorText, Does.Contain("综合=PASS"));
        }

        [Test]
        public async Task Should_DisableWithoutChangingResult_When_SaveFails()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var lines = new List<string>();
            var logging = new Mock<ILoggingService>();
            logging.Setup(item => item.LogInfo(It.IsAny<string>())).Callback<string>(lines.Add);
            logging.Setup(item => item.LogError(It.IsAny<string>(), It.IsAny<Exception>())).Callback<string, Exception>((message, _) => lines.Add(message));
            var storage = new Mock<IStorageService>();
            SetupNoHistoricalPass(storage);
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>())).ThrowsAsync(new InvalidOperationException("db"));
            var round = new Yme009RoundCoordinator(harness.Coordinator, logging.Object, null, new StubPropertyReader { Raw = PassRaw("SN-1") }, storage.Object);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(result.RecordSaved, Is.False);
            Assert.That(result.OperatorText, Does.Contain("综合=PASS").And.Contain("记录未保存"));
            Assert.That(result.OperatorText, Does.Not.Contain("记录已保存"));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(string.Join("\n", lines), Does.Contain("记录保存失败").And.Contain("PASS"));
            Assert.That(result.Access.Connection.IsRevoked, Is.True);
        }

        [Test]
        public async Task Should_NotReadOrSave_When_AccessDoesNotDeliverFormalContext()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"), new Yme009AdbDevice("OTHER", "device", "8"));
            var reader = new StubPropertyReader { Raw = PassRaw("SN-1") };
            var storage = new Mock<IStorageService>();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, reader, storage.Object);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-MANY", CancellationToken.None);

            Assert.That(reader.Calls, Is.EqualTo(0));
            storage.Verify(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
            Assert.That(result.OperatorText, Does.Contain("未保存检测记录").And.Not.Contain("记录已保存"));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
        }

        [Test]
        public async Task Should_NotReadOrSaveOrDisable_When_AtWasNotConfirmed()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("B", "COM4"));
            var reader = new StubPropertyReader { Raw = PassRaw("SN-1") };
            var storage = new Mock<IStorageService>();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, reader, storage.Object);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(result.Access.Stop, Is.EqualTo(Yme009AccessStop.AtNotSent));
            Assert.That(reader.Calls, Is.EqualTo(0));
            storage.Verify(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.NotAttempted));
            Assert.That(harness.Ports.Opened, Is.Empty);
            Assert.That(result.CleanedUp, Is.True);
        }

        [Test]
        public async Task Should_NotRead_When_TargetVersionIsMissing()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var reader = new StubPropertyReader { Raw = PassRaw("SN-1") };
            var storage = new Mock<IStorageService>();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, reader, storage.Object);

            round.UseInspection(new Yme009InspectionRequest(3, " "));
            var result = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(reader.Calls, Is.EqualTo(0));
            storage.Verify(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
            Assert.That(result.OperatorText, Does.Contain("目标软件版本不能为空").And.Not.Contain("PASS"));
            Assert.That(result.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
        }

        [Test]
        public async Task Should_KeepSavedResult_When_DisableIsSkippedOrFails()
        {
            var harness = CreateHarness();
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));
            var saved = new List<TestRecord>();
            var round = CreateInspectionRound(harness, PassRaw("SN-1"), saved, record => record.Id = 15);

            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var skipped = await round.RunAsync("SN-1", CancellationToken.None);

            Assert.That(skipped.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Skipped));
            Assert.That(skipped.RecordSaved, Is.True);
            Assert.That(skipped.OperatorText, Does.Contain("综合=PASS").And.Contain("记录已保存"));

            var failed = CreateHarness();
            PrepareUniqueDevice(failed);
            failed.Ports.Responses.Enqueue(OkResponse);
            failed.Ports.Responses.Enqueue("NO\r\n");
            var failedSaved = new List<TestRecord>();
            var failedRound = CreateInspectionRound(failed, PassRaw("SN-1"), failedSaved, record => record.Id = 16);
            failedRound.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var failedResult = await failedRound.RunAsync("SN-1", CancellationToken.None);

            Assert.That(failedResult.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Failed));
            Assert.That(failedResult.RecordSaved, Is.True);
            Assert.That(failedResult.Access.Stop, Is.EqualTo(Yme009AccessStop.AccessSucceeded));
            Assert.That(failedResult.OperatorText, Does.Contain("综合=PASS").And.Contain("记录已保存"));
        }

        [Test]
        public async Task Should_StillCleanUp_When_ReadThrowsOrWorkIsCancelled()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var storage = new Mock<IStorageService>();
            var reader = new StubPropertyReader { Error = new InvalidOperationException("read") };
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, reader, storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var failed = await round.RunAsync("SN-1", CancellationToken.None);

            storage.Verify(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
            Assert.That(failed.OperatorText, Does.Contain("检测未完成，记录未保存"));
            Assert.That(failed.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(failed.Access.Connection.IsRevoked, Is.True);
            Assert.That(reader.SawBusinessUse, Is.True);

            var cancelled = CreateHarness();
            cancelled.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM7"));
            var cts = new CancellationTokenSource();
            cancelled.Adb.OnQuery = _ => cts.Cancel();
            var cancelStorage = new Mock<IStorageService>();
            var cancelReader = new StubPropertyReader { Raw = PassRaw("SN-1") };
            var cancelRound = new Yme009RoundCoordinator(cancelled.Coordinator, Mock.Of<ILoggingService>(), null, cancelReader, cancelStorage.Object);
            cancelRound.UseInspection(new Yme009InspectionRequest(3, "SW-1"));
            var cancelResult = await cancelRound.RunAsync("SN-6", cts.Token);

            Assert.That(cancelReader.Calls, Is.EqualTo(0));
            cancelStorage.Verify(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Never);
            Assert.That(cancelResult.Close.Disposition, Is.EqualTo(Yme009CloseDisposition.Sent));
            Assert.That(cancelResult.Access.Connection.IsRevoked, Is.True);
            Assert.That(cancelResult.CleanedUp, Is.True);
        }

        [Test]
        public async Task Should_SaveDuplicateFail_When_HistoricalPassExists()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            string queriedSn = null;
            var storage = new Mock<IStorageService>();
            storage.Setup(item => item.GetYme009BatchScopeBySessionIdAsync(3))
                .ReturnsAsync(new Yme009BatchScope("YME009", "ORD-1"));
            storage.Setup(item => item.IsStickerSnPassedInBatchAsync("YME009", "ORD-1", It.IsAny<string>()))
                .Callback<string, string, string>((_, __, sn) => queriedSn = sn)
                .ReturnsAsync(true);
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Callback<TestRecord>(record =>
                {
                    saved.Add(record);
                    record.Id = 21;
                })
                .Returns(Task.CompletedTask);
            var raw = new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded(" SN-1 "))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, new StubPropertyReader { Raw = raw }, storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var result = await round.RunAsync("SN-1", CancellationToken.None);
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(queriedSn, Is.EqualTo("SN-1"));
            Assert.That(queriedSn, Is.Not.EqualTo(" SN-1 "));
            Assert.That(saved, Has.Count.EqualTo(1));
            Assert.That(saved[0].Result, Is.EqualTo("FAIL"));
            Assert.That(saved[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(saved[0].StickerSN, Is.EqualTo("SN-1"));
            Assert.That(result.Decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(result.Decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.Decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.Decision.AgingReset, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.Decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.RecordSaved, Is.True);
            Assert.That(snapshot.PrimaryReason, Is.EqualTo("设备SN已经检验"));
            Assert.That(snapshot.SaveStatusText, Is.EqualTo(Yme009WorkstationText.Saved));
            Assert.That(snapshot.SaveStatusText, Is.Not.EqualTo(Yme009WorkstationText.RecordNotSaved));
        }

        [Test]
        public async Task Should_QueryTrimmedScannedSn_When_DeviceSnTextDiffersOnlyByWhitespace()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            string queriedSn = null;
            var storage = new Mock<IStorageService>();
            storage.Setup(item => item.GetYme009BatchScopeBySessionIdAsync(3))
                .ReturnsAsync(new Yme009BatchScope("YME009", "ORD-1"));
            storage.Setup(item => item.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, __, sn) => queriedSn = sn)
                .ReturnsAsync(false);
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Callback<TestRecord>(record => record.Id = 22)
                .Returns(Task.CompletedTask);
            var raw = new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded(" SN-1 ")).Build();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, new StubPropertyReader { Raw = raw }, storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var result = await round.RunAsync("  SN-1", CancellationToken.None);

            Assert.That(queriedSn, Is.EqualTo("SN-1"));
            Assert.That(result.Decision.Result, Is.EqualTo("PASS"));
            Assert.That(result.Decision.FailReason, Is.Null);
        }

        [Test]
        public async Task Should_NotQueryHistory_When_ScannedSnDiffersFromDeviceSn()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            var storage = new Mock<IStorageService>();
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Callback<TestRecord>(record =>
                {
                    saved.Add(record);
                    record.Id = 23;
                })
                .Returns(Task.CompletedTask);
            var raw = new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("DEV-9"))
                .AgingReboot(Yme009FieldRaw.Succeeded("0"))
                .Build();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, new StubPropertyReader { Raw = raw }, storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var result = await round.RunAsync("SN-1", CancellationToken.None);

            storage.Verify(item => item.GetYme009BatchScopeBySessionIdAsync(It.IsAny<int>()), Times.Never);
            storage.Verify(item => item.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Assert.That(saved[0].Result, Is.EqualTo("FAIL"));
            Assert.That(saved[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(result.Decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(result.Decision.AgingReboot, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(result.Decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.Decision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(result.Decision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFail));
        }

        [Test]
        public async Task Should_NotQueryHistory_When_DeviceSnTimesOut()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var storage = new Mock<IStorageService>();
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Callback<TestRecord>(record => record.Id = 24)
                .Returns(Task.CompletedTask);
            var round = new Yme009RoundCoordinator(
                harness.Coordinator,
                Mock.Of<ILoggingService>(),
                null,
                new StubPropertyReader { Raw = new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.TimedOut()).Build() },
                storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var result = await round.RunAsync("SN-1", CancellationToken.None);

            storage.Verify(item => item.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Assert.That(result.Decision.Result, Is.EqualTo("FAIL"));
            Assert.That(result.Decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.ReadFail));
            Assert.That(result.Decision.FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnMismatch));
            Assert.That(result.Decision.SoftwareVersion, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.Decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Pass));
        }

        [Test]
        public async Task Should_SaveQueryFailure_When_HistoryLookupThrows()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var saved = new List<TestRecord>();
            var storage = new Mock<IStorageService>();
            storage.Setup(item => item.GetYme009BatchScopeBySessionIdAsync(It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("db"));
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Callback<TestRecord>(record =>
                {
                    saved.Add(record);
                    record.Id = 25;
                })
                .Returns(Task.CompletedTask);
            var raw = new Yme009RawBuilder()
                .DeviceSn(Yme009FieldRaw.Succeeded("SN-1"))
                .ProductionTest(Yme009FieldRaw.Succeeded("0"))
                .Build();
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, new StubPropertyReader { Raw = raw }, storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var result = await round.RunAsync("SN-1", CancellationToken.None);
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(saved, Has.Count.EqualTo(1));
            Assert.That(saved[0].Result, Is.EqualTo("FAIL"));
            Assert.That(saved[0].FailReason, Is.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFail));
            Assert.That(saved[0].FailReason, Is.Not.EqualTo(Yme009FailReasonCodes.SnDuplicate));
            Assert.That(result.Decision.Bucket, Is.EqualTo(Yme009StatisticBucket.ReadException));
            Assert.That(result.Decision.ProductionTest, Is.EqualTo(Yme009ItemJudgment.Fail));
            Assert.That(result.Decision.DeviceSn, Is.EqualTo(Yme009ItemJudgment.Pass));
            Assert.That(result.RecordSaved, Is.True);
            Assert.That(snapshot.PrimaryReason, Is.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFailText));
            Assert.That(snapshot.OverallText, Is.EqualTo("FAIL"));
        }

        [Test]
        public async Task Should_ShowRecordNotSaved_When_QueryFailureCannotBeSaved()
        {
            var harness = CreateHarness();
            PrepareUniqueDevice(harness);
            var storage = new Mock<IStorageService>();
            storage.Setup(item => item.GetYme009BatchScopeBySessionIdAsync(It.IsAny<int>()))
                .ReturnsAsync((Yme009BatchScope)null);
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .ThrowsAsync(new InvalidOperationException("db"));
            var round = new Yme009RoundCoordinator(harness.Coordinator, Mock.Of<ILoggingService>(), null, new StubPropertyReader { Raw = PassRaw("SN-1") }, storage.Object);
            round.UseInspection(new Yme009InspectionRequest(3, "SW-1"));

            var result = await round.RunAsync("SN-1", CancellationToken.None);
            var snapshot = Yme009WorkstationSnapshot.FromResult(result, "SN-1");

            Assert.That(result.RecordSaved, Is.False);
            Assert.That(result.Decision.Result, Is.EqualTo("FAIL"));
            Assert.That(result.Decision.FailReason, Is.EqualTo(Yme009FailReasonCodes.PassHistoryQueryFail));
            Assert.That(snapshot.SaveStatusText, Is.EqualTo("记录未保存"));
            Assert.That(snapshot.OverallText, Is.EqualTo("FAIL"));
            storage.Verify(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()), Times.Once);
        }

        private static void PrepareUniqueDevice(Harness harness)
        {
            harness.Discovery.Enqueue(One("A", "COM3"), One("A", "COM3"), One("A", "COM3"), One("A", "COM4"), One("A", "COM5"));
            harness.Adb.Next = Query(new Yme009AdbDevice("SER", "device", "9"));
        }

        private static Yme009RawAttributeSet PassRaw(string serial)
        {
            return new Yme009RawBuilder().DeviceSn(Yme009FieldRaw.Succeeded(serial)).Build();
        }

        private static void SetupNoHistoricalPass(Mock<IStorageService> storage)
        {
            storage.Setup(item => item.GetYme009BatchScopeBySessionIdAsync(It.IsAny<int>()))
                .ReturnsAsync(new Yme009BatchScope("P-YME", "ORD-YME"));
            storage.Setup(item => item.IsStickerSnPassedInBatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
        }

        private static Yme009RoundCoordinator CreateInspectionRound(
            Harness harness,
            Yme009RawAttributeSet raw,
            List<TestRecord> saved,
            Action<TestRecord> accept)
        {
            var storage = new Mock<IStorageService>();
            SetupNoHistoricalPass(storage);
            storage.Setup(item => item.SaveTestRecordAsync(It.IsAny<TestRecord>()))
                .Callback<TestRecord>(record =>
                {
                    saved.Add(record);
                    accept(record);
                })
                .Returns(Task.CompletedTask);
            return new Yme009RoundCoordinator(
                harness.Coordinator,
                Mock.Of<ILoggingService>(),
                null,
                new StubPropertyReader { Raw = raw },
                storage.Object);
        }

        private sealed class StubPropertyReader : IYme009PropertyReader
        {
            public Yme009RawAttributeSet Raw { get; set; }

            public Exception Error { get; set; }

            public int Calls { get; private set; }

            public bool SawBusinessUse { get; private set; }

            public Task<Yme009RawAttributeSet> ReadAsync(Yme009DeviceConnection connection, CancellationToken cancellationToken)
            {
                Calls++;
                SawBusinessUse = connection != null && connection.AllowsBusinessUse;
                cancellationToken.ThrowIfCancellationRequested();
                if (Error != null)
                    throw Error;
                return Task.FromResult(Raw);
            }
        }

        private static Harness CreateHarness()
        {
            var clock = new ManualClock { UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var delay = new AdvancingDelay(clock);
            var discovery = new ScriptedDiscovery();
            var ports = new ScriptedPorts();
            var adb = new ScriptedAdb();
            var timing = new Yme009AccessTiming(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(1));
            var logging = Mock.Of<ILoggingService>();
            var at = new Yme009AtAdbSwitch(discovery, ports, clock, delay, timing.AtReadBudget);
            var coordinator = new Yme009DeviceAccessCoordinator(
                discovery, at, adb, logging, timing, clock, delay);
            return new Harness
            {
                Discovery = discovery,
                Ports = ports,
                Adb = adb,
                Coordinator = coordinator,
                Round = new Yme009RoundCoordinator(coordinator, logging)
            };
        }

        private static Harness CreateLoggedHarness()
        {
            var clock = new ManualClock { UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var delay = new AdvancingDelay(clock);
            var discovery = new ScriptedDiscovery();
            var ports = new ScriptedPorts();
            var adb = new ScriptedAdb();
            var timing = new Yme009AccessTiming(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(1));
            var lines = new List<string>();
            var logging = new Mock<ILoggingService>();
            logging.Setup(item => item.LogInfo(It.IsAny<string>())).Callback<string>(lines.Add);
            logging.Setup(item => item.LogWarning(It.IsAny<string>())).Callback<string>(lines.Add);
            logging.Setup(item => item.LogError(It.IsAny<string>(), It.IsAny<Exception>()))
                .Callback<string, Exception>((message, _) => lines.Add(message));
            var scope = new Yme009RoundLogScope();
            var at = new Yme009AtAdbSwitch(discovery, ports, clock, delay, timing.AtReadBudget, logging.Object, scope);
            var coordinator = new Yme009DeviceAccessCoordinator(
                discovery, at, adb, logging.Object, timing, clock, delay, scope);
            return new Harness
            {
                Discovery = discovery,
                Ports = ports,
                Adb = adb,
                Coordinator = coordinator,
                Round = new Yme009RoundCoordinator(coordinator, logging.Object, scope),
                Lines = lines
            };
        }

        private static Yme009CdcScan One(string identity, string port)
        {
            return Yme009CdcScan.Found(new[] { new Yme009CdcCandidate(identity, port) });
        }

        private static Yme009AdbQueryResult Query(params Yme009AdbDevice[] devices)
        {
            return new Yme009AdbQueryResult(true, devices);
        }

        private const string OkResponse = "SIMO AT COMMAND RESULT:\r\nOK\r\n";

        private sealed class Harness
        {
            public ScriptedDiscovery Discovery { get; set; }

            public ScriptedPorts Ports { get; set; }

            public ScriptedAdb Adb { get; set; }

            public Yme009DeviceAccessCoordinator Coordinator { get; set; }

            public Yme009RoundCoordinator Round { get; set; }

            public List<string> Lines { get; set; }
        }

        private sealed class ManualClock : IYme009Clock
        {
            public DateTime UtcNow { get; set; }
        }

        private sealed class AdvancingDelay : IYme009AsyncDelay
        {
            private readonly ManualClock _clock;

            public AdvancingDelay(ManualClock clock)
            {
                _clock = clock;
            }

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _clock.UtcNow = _clock.UtcNow.Add(delay);
                return Task.CompletedTask;
            }
        }

        private sealed class ScriptedDiscovery : IYme009CdcDiscovery
        {
            private readonly Queue<Yme009CdcScan> _scans = new Queue<Yme009CdcScan>();

            public Exception ThrowNext { get; set; }

            public void Enqueue(params Yme009CdcScan[] scans)
            {
                for (var i = 0; i < scans.Length; i++)
                    _scans.Enqueue(scans[i]);
            }

            public Task<Yme009CdcScan> DiscoverAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ThrowNext != null)
                {
                    var error = ThrowNext;
                    ThrowNext = null;
                    throw error;
                }

                if (_scans.Count == 0)
                    return Task.FromResult(Yme009CdcScan.Found(Array.Empty<Yme009CdcCandidate>()));

                return Task.FromResult(_scans.Dequeue());
            }
        }

        private sealed class ScriptedPorts : IYme009SerialPortFactory
        {
            public readonly List<string> Opened = new List<string>();
            public readonly Queue<string> Responses = new Queue<string>();
            public int Live { get; private set; }

            public int ThrowOnOpenNumber { get; set; }

            public IYme009SerialPortSession Open(string portName)
            {
                if (ThrowOnOpenNumber > 0 && Opened.Count + 1 == ThrowOnOpenNumber)
                    throw new InvalidOperationException("open-denied");

                if (Live != 0)
                    throw new InvalidOperationException("上一次串口尚未释放");

                Live++;
                Opened.Add(portName);
                var response = Responses.Count == 0 ? OkResponse : Responses.Dequeue();
                return new Session(this, response);
            }

            private sealed class Session : IYme009SerialPortSession
            {
                private readonly ScriptedPorts _owner;
                private readonly string _response;
                private bool _disposed;

                public Session(ScriptedPorts owner, string response)
                {
                    _owner = owner;
                    _response = response;
                }

                public void Write(string text)
                {
                    if (_disposed)
                        throw new ObjectDisposedException("port");
                }

                public string ReadExisting()
                {
                    if (_disposed)
                        throw new ObjectDisposedException("port");

                    return _response;
                }

                public void Dispose()
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    _owner.Live--;
                }
            }
        }

        private sealed class ScriptedAdb : IYme009AdbDeviceQuery
        {
            public readonly Queue<Yme009AdbQueryResult> Sequence = new Queue<Yme009AdbQueryResult>();
            public Yme009AdbQueryResult Next = Query();
            public int Calls;
            public Action<CancellationToken> OnQuery;

            public Task<Yme009AdbQueryResult> QueryAsync(CancellationToken cancellationToken)
            {
                Calls++;
                OnQuery?.Invoke(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Sequence.Count > 0)
                    return Task.FromResult(Sequence.Dequeue());

                return Task.FromResult(Next);
            }
        }
    }
}
