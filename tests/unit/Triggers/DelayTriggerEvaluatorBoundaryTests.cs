using System;
using Xunit;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Tests.Unit.Triggers {
    public class DelayTriggerEvaluatorBoundaryTests {
        [Fact]
        public void StatusIsPendingBeforeDelayElapsed() {
            var now = DateTimeOffset.UtcNow;
            var eval = new DelayTriggerEvaluator();
            var trigger = new Trigger {
                Id = "delay1",
                Enabled = true,
                Type = TriggerType.Delay,
                Params = new DelayParams {
                    Seconds = 10
                },
                EnabledAt = now
            };
            var result = eval.Evaluate(trigger, now.AddSeconds(5));
            Assert.Equal(TriggerStatus.Pending, result.Status);
            Assert.Equal("waiting_delay", result.Reason);
        }

        [Fact]
        public void StatusIsSatisfiedAtDelayElapsed() {
            var now = DateTimeOffset.UtcNow;
            var eval = new DelayTriggerEvaluator();
            var trigger = new Trigger {
                Id = "delay2",
                Enabled = true,
                Type = TriggerType.Delay,
                Params = new DelayParams {
                    Seconds = 10
                },
                EnabledAt = now
            };
            var result = eval.Evaluate(trigger, now.AddSeconds(10));
            Assert.Equal(TriggerStatus.Satisfied, result.Status);
            Assert.Equal("delay_elapsed", result.Reason);
        }

        [Fact]
        public void StatusIsSatisfiedZeroDelay() {
            var now = DateTimeOffset.UtcNow;
            var eval = new DelayTriggerEvaluator();
            var trigger = new Trigger {
                Id = "delay3",
                Enabled = true,
                Type = TriggerType.Delay,
                Params = new DelayParams {
                    Seconds = 0
                },
                EnabledAt = now
            };
            var result = eval.Evaluate(trigger, now);
            Assert.Equal(TriggerStatus.Satisfied, result.Status);
            Assert.Equal("delay_elapsed", result.Reason);
        }

        [Fact]
        public void StatusIsPendingInitialEvaluationWithNonZeroDelay() {
            var now = DateTimeOffset.UtcNow;
            var eval = new DelayTriggerEvaluator();
            var trigger = new Trigger {
                Id = "delay4",
                Enabled = true,
                Type = TriggerType.Delay,
                Params = new DelayParams {
                    Seconds = 5
                }
            };
            var result = eval.Evaluate(trigger, now);
            Assert.Equal(TriggerStatus.Pending, result.Status);
            Assert.Equal("delay_pending_initial", result.Reason);
        }
    }
}
