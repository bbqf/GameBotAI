using System;
using Xunit;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Tests.Unit.Triggers {
    public class ScheduleTriggerEvaluatorBoundaryTests {
        [Fact]
        public void StatusIsPendingBeforeTimestamp() {
            var now = DateTimeOffset.UtcNow;
            var future = now.AddMinutes(5);
            var eval = new ScheduleTriggerEvaluator();
            var trigger = new Trigger {
                Id = "sched1",
                Enabled = true,
                Type = TriggerType.Schedule,
                Params = new ScheduleParams {
                    Timestamp = future
                },
                EnabledAt = now
            };
            var result = eval.Evaluate(trigger, now);
            Assert.Equal(TriggerStatus.Pending, result.Status);
            Assert.Equal("waiting_for_time", result.Reason);
        }

        [Fact]
        public void StatusIsSatisfiedAtOrAfterTimestamp() {
            var now = DateTimeOffset.UtcNow;
            var eval = new ScheduleTriggerEvaluator();
            var trigger = new Trigger {
                Id = "sched2",
                Enabled = true,
                Type = TriggerType.Schedule,
                Params = new ScheduleParams {
                    Timestamp = now
                },
                EnabledAt = now
            };
            var result = eval.Evaluate(trigger, now);
            Assert.Equal(TriggerStatus.Satisfied, result.Status);
            Assert.Equal("time_reached", result.Reason);
        }

        [Fact]
        public void StatusIsDisabledIfEnabledAfterTimestamp() {
            var now = DateTimeOffset.UtcNow;
            var past = now.AddMinutes(-10);
            var eval = new ScheduleTriggerEvaluator();
            var trigger = new Trigger {
                Id = "sched3",
                Enabled = true,
                Type = TriggerType.Schedule,
                Params = new ScheduleParams {
                    Timestamp = past
                },
                EnabledAt = now
            };
            var result = eval.Evaluate(trigger, now);
            Assert.Equal(TriggerStatus.Disabled, result.Status);
            Assert.Equal("scheduled_time_in_past_when_enabled", result.Reason);
        }
    }
}
