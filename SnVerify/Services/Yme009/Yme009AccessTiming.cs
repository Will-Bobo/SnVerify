using System;
using System.Threading;
using System.Threading.Tasks;

namespace SnVerify.Services.Yme009
{
    /// <summary>
    /// 30 秒 CDC 窗口是已确认规则。AT 读取预算、Ready 窗口和轮询间隔来自参考工程量级，
    /// 技术设计明确这些数值尚未在 YME009 上定案，集中放在这里以便真机后替换。
    /// </summary>
    public sealed class Yme009AccessTiming
    {
        public static readonly Yme009AccessTiming Default = new Yme009AccessTiming(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(1));

        public Yme009AccessTiming(
            TimeSpan cdcWindow,
            TimeSpan nonUniquePause,
            TimeSpan atReadBudget,
            TimeSpan readyWindow,
            TimeSpan readyPoll)
        {
            CdcWindow = cdcWindow;
            NonUniquePause = nonUniquePause;
            AtReadBudget = atReadBudget;
            ReadyWindow = readyWindow;
            ReadyPoll = readyPoll;
        }

        public TimeSpan CdcWindow { get; }

        public TimeSpan NonUniquePause { get; }

        public TimeSpan AtReadBudget { get; }

        public TimeSpan ReadyWindow { get; }

        public TimeSpan ReadyPoll { get; }
    }

    public interface IYme009Clock
    {
        DateTime UtcNow { get; }
    }

    public sealed class Yme009SystemClock : IYme009Clock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }

    public interface IYme009AsyncDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class Yme009SystemDelay : IYme009AsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
