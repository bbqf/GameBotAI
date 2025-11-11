using System;
using System.IO;
using System.Threading.Tasks;
using GameBot.Domain.Profiles;
using Xunit;

namespace GameBot.UnitTests
{
    public class ProfilesSerializationTests
    {
        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "GameBotTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

    [Fact(DisplayName="DelayTrigger RoundTrips")]
    public async Task DelayTriggerRoundTrips()
        {
            var root = CreateTempRoot();
            var repo = new FileProfileRepository(root);

            var profile = new AutomationProfile
            {
                Id = string.Empty,
                Name = "p1",
                GameId = "g1"
            };
            profile.Triggers.Add(new ProfileTrigger
            {
                Id = "t1",
                Type = TriggerType.Delay,
                Params = new DelayParams { Seconds = 2 }
            });

            var added = await repo.AddAsync(profile);
            var fetched = await repo.GetAsync(added.Id);

            Assert.NotNull(fetched);
            Assert.Single(fetched!.Triggers);
            Assert.IsType<DelayParams>(fetched.Triggers[0].Params);
            Assert.Equal(2, ((DelayParams)fetched.Triggers[0].Params).Seconds);
        }

    [Fact(DisplayName="ScheduleTrigger RoundTrips")]
    public async Task ScheduleTriggerRoundTrips()
        {
            var root = CreateTempRoot();
            var repo = new FileProfileRepository(root);

            var now = DateTimeOffset.UtcNow;
            var profile = new AutomationProfile
            {
                Id = string.Empty,
                Name = "p2",
                GameId = "g2"
            };
            profile.Triggers.Add(new ProfileTrigger
            {
                Id = "t2",
                Type = TriggerType.Schedule,
                Params = new ScheduleParams { Timestamp = now }
            });

            var added = await repo.AddAsync(profile);
            var fetched = await repo.GetAsync(added.Id);

            Assert.NotNull(fetched);
            Assert.Single(fetched!.Triggers);
            var p = Assert.IsType<ScheduleParams>(fetched.Triggers[0].Params);
            Assert.Equal(now.ToUnixTimeSeconds(), p.Timestamp.ToUnixTimeSeconds());
        }

    [Fact(DisplayName="ImageMatchTrigger RoundTrips")]
    public async Task ImageMatchTriggerRoundTrips()
        {
            var root = CreateTempRoot();
            var repo = new FileProfileRepository(root);

            var profile = new AutomationProfile
            {
                Id = string.Empty,
                Name = "p3",
                GameId = "g3"
            };
            profile.Triggers.Add(new ProfileTrigger
            {
                Id = "t3",
                Type = TriggerType.ImageMatch,
                Params = new ImageMatchParams
                {
                    ReferenceImageId = "ref1",
                    Region = new Region { X = 0, Y = 0, Width = 0.5, Height = 0.5 },
                    SimilarityThreshold = 0.9
                }
            });

            var added = await repo.AddAsync(profile);
            var fetched = await repo.GetAsync(added.Id);

            Assert.NotNull(fetched);
            Assert.Single(fetched!.Triggers);
            var p = Assert.IsType<ImageMatchParams>(fetched.Triggers[0].Params);
            Assert.Equal("ref1", p.ReferenceImageId);
            Assert.Equal(0.9, p.SimilarityThreshold, 3);
            Assert.Equal(0.5, p.Region.Width, 3);
        }

    [Fact(DisplayName="TextMatchTrigger RoundTrips")]
    public async Task TextMatchTriggerRoundTrips()
        {
            var root = CreateTempRoot();
            var repo = new FileProfileRepository(root);

            var profile = new AutomationProfile
            {
                Id = string.Empty,
                Name = "p4",
                GameId = "g4"
            };
            profile.Triggers.Add(new ProfileTrigger
            {
                Id = "t4",
                Type = TriggerType.TextMatch,
                Params = new TextMatchParams
                {
                    Target = "hello",
                    Region = new Region { X = 0.1, Y = 0.2, Width = 0.3, Height = 0.4 },
                    ConfidenceThreshold = 0.8,
                    Mode = "found"
                }
            });

            var added = await repo.AddAsync(profile);
            var fetched = await repo.GetAsync(added.Id);

            Assert.NotNull(fetched);
            Assert.Single(fetched!.Triggers);
            var p = Assert.IsType<TextMatchParams>(fetched.Triggers[0].Params);
            Assert.Equal("hello", p.Target);
            Assert.Equal(0.3, p.Region.Width, 3);
            Assert.Equal("found", p.Mode);
        }
    }
}
