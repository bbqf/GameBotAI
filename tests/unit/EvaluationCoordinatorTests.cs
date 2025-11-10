using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests
{
    internal sealed class InMemoryProfileRepo : IProfileRepository
    {
        private readonly List<AutomationProfile> _items;
        public InMemoryProfileRepo(IEnumerable<AutomationProfile> seed) => _items = seed.ToList();
        public Task<AutomationProfile> AddAsync(AutomationProfile profile, CancellationToken ct = default)
        {
            _items.Add(profile);
            return Task.FromResult(profile);
        }
        public Task<AutomationProfile?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<AutomationProfile>> ListAsync(string? gameId = null, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<AutomationProfile>)_items.Where(p => gameId == null || p.GameId == gameId).ToList());
        public Task<AutomationProfile?> UpdateAsync(AutomationProfile profile, CancellationToken ct = default)
            => Task.FromResult<AutomationProfile?>(profile);
    }

    internal sealed class ImmediateDelayEvaluator : ITriggerEvaluator
    {
        public bool CanEvaluate(ProfileTrigger trigger) => trigger.Type == TriggerType.Delay && trigger.Params is DelayParams;
        public TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now)
            => new TriggerEvaluationResult { Status = TriggerStatus.Satisfied, EvaluatedAt = now };
    }

    public class EvaluationCoordinatorTests
    {
        [Fact(DisplayName="Coordinator evaluates and persists timestamps")]
        public async Task EvaluateAndPersist()
        {
            var profile = new AutomationProfile
            {
                Id = "p1",
                Name = "P1",
                GameId = "g1"
            };
            profile.Triggers.Add(new ProfileTrigger
            {
                Id = "t1",
                Type = TriggerType.Delay,
                Params = new DelayParams { Seconds = 0 }
            });

            var repo = new InMemoryProfileRepo(new[] { profile });
            var service = new TriggerEvaluationService(new ITriggerEvaluator[] { new ImmediateDelayEvaluator() });
            var coord = new TriggerEvaluationCoordinator(repo, service);

            var count = await coord.EvaluateAllAsync();
            Assert.Equal(1, count);
            Assert.NotNull(profile.Triggers[0].LastEvaluatedAt);
            Assert.NotNull(profile.Triggers[0].LastFiredAt);
        }
    }
}
