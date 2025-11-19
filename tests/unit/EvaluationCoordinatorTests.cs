using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Triggers;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests
{
    internal sealed class InMemoryTriggerRepo : ITriggerRepository
    {
        private readonly List<Trigger> _items;
        public InMemoryTriggerRepo(IEnumerable<Trigger> seed) => _items = seed.ToList();
        public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_items.FirstOrDefault(t => t.Id == id));
        public Task UpsertAsync(Trigger trigger, CancellationToken ct = default)
        {
            var idx = _items.FindIndex(t => t.Id == trigger.Id);
            if (idx >= 0) _items[idx] = trigger; else _items.Add(trigger);
            return Task.CompletedTask;
        }
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var removed = _items.RemoveAll(t => t.Id == id) > 0;
            return Task.FromResult(removed);
        }
        public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Trigger>)_items.ToList());
    }

    internal sealed class ImmediateDelayEvaluator : GameBot.Domain.Triggers.ITriggerEvaluator
    {
        public bool CanEvaluate(Trigger trigger) => trigger.Type == TriggerType.Delay && trigger.Params is DelayParams;
        public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now)
            => new TriggerEvaluationResult { Status = TriggerStatus.Satisfied, EvaluatedAt = now };
    }

    public class EvaluationCoordinatorTests
    {
        [Fact(DisplayName="Coordinator evaluates and persists timestamps")]
        public async Task EvaluateAndPersist()
        {
            var trigger = new Trigger { Id = "t1", Type = TriggerType.Delay, Enabled = true, CooldownSeconds = 0, Params = new DelayParams { Seconds = 0 } };
            var repo = new InMemoryTriggerRepo(new[] { trigger });
            var service = new TriggerEvaluationService(new ITriggerEvaluator[] { new ImmediateDelayEvaluator() });
            var coord = new TriggerEvaluationCoordinator(repo, service);

            var first = await coord.EvaluateAllAsync(null);
            Assert.Equal(1, first);
            Assert.NotNull(trigger.LastEvaluatedAt);
            Assert.NotNull(trigger.LastFiredAt);

            var second = await coord.EvaluateAllAsync();
            Assert.Equal(1, second);
            Assert.NotNull(trigger.LastEvaluatedAt);
            Assert.NotNull(trigger.LastFiredAt);
        }
    }
}
