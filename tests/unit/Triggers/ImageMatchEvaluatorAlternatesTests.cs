#pragma warning disable CA2007, CA1861, CA1859, CA2000
using System;
using System.Drawing;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;
using GameBot.Service.Services.Conditions;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.UnitTests.Images;
using GameBot.UnitTests.Vision;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GameBot.Tests.Unit.Triggers {
  /// <summary>
  /// Feature 097 (issue #192): image-match triggers, sequence image conditions and the readiness gate
  /// all treat an image's alternates as matches for it.
  /// </summary>
  public sealed class ImageMatchEvaluatorAlternatesTests {
    private const int ScreenWidth = 120;
    private const int ScreenHeight = 90;
    private const int PatchSize = 24;
    private const int PatchX = 40;
    private const int PatchY = 30;
    private const uint NightSeed = 0xA5A5A5u;
    private const uint DaySeed = 0x5A5A5Au;

    private static Color Texture(int x, int y, uint seed) {
      var v = (int)(Hash(x + 1, y + 1, seed) % 256);
      return Color.FromArgb(v, 255 - v, (v * 7) % 256);
    }

    private static uint Hash(int x, int y, uint seed) {
      unchecked {
        var h = ((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ seed;
        h ^= h >> 13;
        h *= 0x5BD1E995u;
        return h ^ (h >> 15);
      }
    }

    private static Bitmap Patch(uint seed) {
      var bmp = new Bitmap(PatchSize, PatchSize);
      for (var y = 0; y < PatchSize; y++)
        for (var x = 0; x < PatchSize; x++)
          bmp.SetPixel(x, y, Texture(x, y, seed));
      return bmp;
    }

    private static Bitmap NightScreen() {
      var bmp = new Bitmap(ScreenWidth, ScreenHeight);
      for (var y = 0; y < ScreenHeight; y++)
        for (var x = 0; x < ScreenWidth; x++) {
          var v = 96 + (int)(Hash(x, y, 0x1234567u) % 24);
          bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
        }
      for (var y = 0; y < PatchSize; y++)
        for (var x = 0; x < PatchSize; x++)
          bmp.SetPixel(PatchX + x, PatchY + y, Texture(x, y, NightSeed));
      return bmp;
    }

    private static MemoryReferenceImageStore Store(bool withNight = true) {
      var store = new MemoryReferenceImageStore();
      store.AddOrUpdate("anchor", Patch(DaySeed));
      if (withNight) store.AddOrUpdate("anchor-night", Patch(NightSeed));
      return store;
    }

    private static ReferenceImageSetLoaderTests.MemoryAlternates Alternates(params string[] ids) {
      var alts = new ReferenceImageSetLoaderTests.MemoryAlternates();
      alts.SetAlternates("anchor", ids);
      return alts;
    }

    private static Trigger AnchorTrigger() => new() {
      Id = "t",
      Enabled = true,
      Type = TriggerType.ImageMatch,
      Params = new ImageMatchParams {
        ReferenceImageId = "anchor",
        SimilarityThreshold = 0.85,
        Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
      }
    };

    [Fact]
    public void WithoutAlternatesTheDayImageDoesNotMatchTheNightScreen() {
      var eval = new ImageMatchEvaluator(Store(), new StubScreenSource(NightScreen()), new TemplateMatcher());

      var result = eval.Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow);

      result.Status.Should().Be(TriggerStatus.Pending);
    }

    [Fact]
    public void AnAlternateSuppliesTheMaximumSimilarity() {
      var eval = new ImageMatchEvaluator(Store(), new StubScreenSource(NightScreen()), new TemplateMatcher(), null, Alternates("anchor-night"));

      var result = eval.Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow);

      result.Status.Should().Be(TriggerStatus.Satisfied);
      result.Similarity.Should().BeGreaterThan(0.99);
    }

    [Fact]
    public void AnEmptyAlternatesListLeavesTheSimilarityUnchanged() {
      var screen = NightScreen();
      var plain = new ImageMatchEvaluator(Store(), new StubScreenSource(screen), new TemplateMatcher());
      var withRepo = new ImageMatchEvaluator(Store(), new StubScreenSource(screen), new TemplateMatcher(), null, Alternates());

      withRepo.Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow).Similarity
        .Should().Be(plain.Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow).Similarity);
    }

    [Fact]
    public void AMissingAlternateIsIgnored() {
      var eval = new ImageMatchEvaluator(Store(withNight: false), new StubScreenSource(NightScreen()), new TemplateMatcher(), null, Alternates("anchor-night"));

      var result = eval.Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow);

      result.Status.Should().Be(TriggerStatus.Pending);
    }

    [Fact]
    public void AWinningAlternateIsLogged() {
      var logger = new CapturingLogger<ImageMatchEvaluator>();
      var eval = new ImageMatchEvaluator(Store(), new StubScreenSource(NightScreen()), new TemplateMatcher(), logger, Alternates("anchor-night"));

      eval.Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow);

      logger.Inner.Entries.Should().Contain(e => e.Level == LogLevel.Information
                                                && e.Message.Contains("anchor-night", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("present", true)]
    [InlineData("absent", false)]
    public async Task SequenceImageConditionSeesTheAlternate(string expectedState, bool expected) {
      var eval = new ImageMatchEvaluator(Store(), new StubScreenSource(NightScreen()), new TemplateMatcher(), null, Alternates("anchor-night"));
      var adapter = new ImageDetectionConditionAdapter(new TriggerEvaluationService(new ITriggerEvaluator[] { eval }));

      var result = await adapter.EvaluateAsync(new ConditionOperand {
        OperandType = ConditionOperandType.ImageDetection,
        TargetRef = "anchor",
        ExpectedState = expectedState,
        Threshold = 0.85
      });

      result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Present", true)]
    [InlineData("Absent", false)]
    public async Task SequenceImageVisibleConditionSeesTheAlternate(string mode, bool expected) {
      var eval = new ImageMatchEvaluator(Store(), new StubScreenSource(NightScreen()), new TemplateMatcher(), null, Alternates("anchor-night"));
      var adapter = new ImageVisibleConditionAdapter(new TriggerEvaluationService(new ITriggerEvaluator[] { eval }));

      var result = await adapter.EvaluateAsync(new GameBot.Domain.Commands.Blocks.Condition {
        Source = "image",
        TargetId = "anchor",
        Mode = mode,
        ConfidenceThreshold = 0.85
      });

      result.Should().Be(expected);
    }

    [Fact]
    public async Task ReadinessGateIsSatisfiedByTheAlternate() {
      var probe = new GameReadinessProbe(new StubScreenSource(NightScreen()), Store(), new TemplateMatcher(),
        new AppConfig { CaptureIntervalMs = 1 }, null, Alternates("anchor-night"));

      var result = await probe.WaitUntilReadyAsync(new DetectionTarget("anchor", 0.85), timeoutMs: 0);

      result.Ready.Should().BeTrue();
    }

    [Fact]
    public async Task ReadinessGateWithoutAlternatesIsNotSatisfied() {
      var probe = new GameReadinessProbe(new StubScreenSource(NightScreen()), Store(), new TemplateMatcher(),
        new AppConfig { CaptureIntervalMs = 1 });

      var result = await probe.WaitUntilReadyAsync(new DetectionTarget("anchor", 0.85), timeoutMs: 0);

      result.Ready.Should().BeFalse();
    }

    [Fact]
    public async Task ReadinessGateStillReportsAMissingPrimary() {
      var probe = new GameReadinessProbe(new StubScreenSource(NightScreen()), new MemoryReferenceImageStore(), new TemplateMatcher(),
        new AppConfig { CaptureIntervalMs = 1 }, null, Alternates("anchor-night"));

      var result = await probe.WaitUntilReadyAsync(new DetectionTarget("anchor", 0.85), timeoutMs: 0);

      result.ImageLoadStatus.Should().Be("missing");
    }

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T> {
      public ReferenceSetTemplateMatcherTests.ListLogger Inner { get; } = new();
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Inner.Log(logLevel, eventId, state, exception, formatter);
    }
  }
}
