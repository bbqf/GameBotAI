using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.Unit.Emulator.Triggers;

public sealed class TimeTriggerEvaluatorsTests
{
    private static Trigger MakeDelay(int seconds, DateTimeOffset? enabledAt = null) => new()
    {
        Id = "d1",
        Type = TriggerType.Delay,
        Enabled = true,
        EnabledAt = enabledAt,
        Params = new DelayParams { Seconds = seconds }
    };

    private static Trigger MakeSchedule(DateTimeOffset ts, DateTimeOffset? enabledAt = null) => new()
    {
        Id = "s1",
        Type = TriggerType.Schedule,
        Enabled = true,
        EnabledAt = enabledAt,
        Params = new ScheduleParams { Timestamp = ts }
    };

    [Fact]
    public void DelayNotYetElapsedShouldRemainPending()
    {
        var now = DateTimeOffset.UtcNow;
        var trig = MakeDelay(10, now);
        var eval = new DelayTriggerEvaluator();
        var res = eval.Evaluate(trig, now.AddSeconds(5));
        Assert.Equal(TriggerStatus.Pending, res.Status);
        Assert.Equal("waiting_delay", res.Reason);
    }

    [Fact]
    public void DelayElapsedShouldBeSatisfied()
    {
        var now = DateTimeOffset.UtcNow;
        var trig = MakeDelay(5, now);
        var eval = new DelayTriggerEvaluator();
        var res = eval.Evaluate(trig, now.AddSeconds(6));
        Assert.Equal(TriggerStatus.Satisfied, res.Status);
        Assert.Equal("delay_elapsed", res.Reason);
    }

    [Fact]
    public void ScheduleFutureTimePending()
    {
        var now = DateTimeOffset.UtcNow;
        var trig = MakeSchedule(now.AddSeconds(30), now);
        var eval = new ScheduleTriggerEvaluator();
        var res = eval.Evaluate(trig, now.AddSeconds(10));
        Assert.Equal(TriggerStatus.Pending, res.Status);
        Assert.Equal("waiting_for_time", res.Reason);
    }

    [Fact]
    public void ScheduleTimeReachedSatisfied()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.AddSeconds(15);
        var trig = MakeSchedule(ts, now);
        var eval = new ScheduleTriggerEvaluator();
        var res = eval.Evaluate(trig, ts.AddSeconds(1));
        Assert.Equal(TriggerStatus.Satisfied, res.Status);
        Assert.Equal("time_reached", res.Reason);
    }

    [Fact]
    public void ScheduleEnabledAfterPastTimeDisabled()
    {
        var now = DateTimeOffset.UtcNow;
        var past = now.AddSeconds(-30);
        var trig = MakeSchedule(past, now);
        var eval = new ScheduleTriggerEvaluator();
        var res = eval.Evaluate(trig, now);
        Assert.Equal(TriggerStatus.Disabled, res.Status);
        Assert.Equal("scheduled_time_in_past_when_enabled", res.Reason);
    }
}