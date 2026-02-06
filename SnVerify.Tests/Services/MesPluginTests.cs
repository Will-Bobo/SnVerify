/// <summary>
/// Phase 3：杰科 MES 插件骨架与 Gate 挂载单元测试。
/// 契约：MES_Plugin_Gate_Design_Freeze.md；TDD 覆盖 JekeMesPlugin、MesMode（Disabled/Enabled/Strict 预留）、Pre-Gate 三态、Post-Report。
/// </summary>

using System.Threading.Tasks;
using NUnit.Framework;
using SnVerify.Services.Mes;
using SnVerify.Services.Mes.Gate;

namespace SnVerify.Tests.Services
{
    [TestFixture]
    public class MesPluginTests
    {
        // ---------- JekeMesPlugin 骨架：Pre-Gate 返回 Allow ----------
        [Test]
        public async Task PreGate_Allow_DisabledMode()
        {
            var plugin = new JekeMesPlugin();
            var context = new MesContext { SessionId = "1", StickerSN = "SN123" };
            var result = await plugin.CheckAsync(context);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Decision, Is.EqualTo(MesPreCheckDecision.Allow));
            Assert.That(result.Reason, Does.Contain("Stub").Or.Contain("骨架"));
        }

        // ---------- JekeMesPlugin 骨架：Post-Report Stub 不抛 ----------
        [Test]
        public async Task PostReport_Stub_Success()
        {
            var plugin = new JekeMesPlugin();
            var context = new TestResultContext
            {
                SessionId = "1",
                StickerSN = "SN123",
                Result = "PASS",
                DeviceSN = "SN123",
                VerifyTime = System.DateTime.Now,
            };
            Assert.DoesNotThrowAsync(async () => await plugin.ReportTestResultAsync(context));
            await plugin.ReportTestResultAsync(context);
        }

        // ---------- Capabilities 符合 Phase 3 骨架 ----------
        [Test]
        public void JekeMesPlugin_Capabilities_SupportsPreCheckAndResultReport()
        {
            var plugin = new JekeMesPlugin();
            Assert.That(plugin.Capabilities, Is.Not.Null);
            Assert.That(plugin.Capabilities.SupportsPreCheck, Is.True);
            Assert.That(plugin.Capabilities.RequiresPreCheck, Is.False);
            Assert.That(plugin.Capabilities.SupportsResultReport, Is.True);
        }

        // ---------- 实现 IMesPlugin（PreCheck + ResultReporter + Capabilities） ----------
        [Test]
        public void JekeMesPlugin_Implements_IMesPlugin()
        {
            var plugin = new JekeMesPlugin();
            Assert.That(plugin, Is.InstanceOf<IMesPlugin>());
            Assert.That(plugin, Is.InstanceOf<IMesPreCheck>());
            Assert.That(plugin, Is.InstanceOf<IMesResultReporter>());
        }

        // ---------- Pre-Gate 三态：Allow ----------
        [Test]
        public async Task PreGate_Decision_Allow_ReturnsAllow()
        {
            var plugin = new JekeMesPlugin();
            var result = await plugin.CheckAsync(new MesContext { StickerSN = "X" });
            Assert.That(result.Decision, Is.EqualTo(MesPreCheckDecision.Allow));
        }

        // ---------- Post-Report 入参只读，插件不抛 ----------
        [Test]
        public async Task PostReport_WithFailResult_DoesNotThrow()
        {
            var plugin = new JekeMesPlugin();
            var context = new TestResultContext
            {
                SessionId = "S1",
                StickerSN = "SN_FAIL",
                Result = "FAIL",
                FailReason = "设备SN不匹配",
                VerifyTime = System.DateTime.Now,
            };
            await plugin.ReportTestResultAsync(context);
        }
    }
}
