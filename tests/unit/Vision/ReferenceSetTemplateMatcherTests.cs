#pragma warning disable CA2007, CA1861, CA1859, CA2000, CA1849
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Vision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GameBot.UnitTests.Vision {
  /// <summary>
  /// Feature 097 (issue #192): the reference-set decorator merges one match run per reference into a
  /// single, deterministically ordered result.
  /// </summary>
  public sealed class ReferenceSetTemplateMatcherTests : IDisposable {
    private readonly Mat _screen = new(100, 100, MatType.CV_8UC3, Scalar.All(0));
    private readonly Mat _primary = new(10, 10, MatType.CV_8UC3, Scalar.All(1));
    private readonly Mat _night1 = new(10, 10, MatType.CV_8UC3, Scalar.All(2));
    private readonly Mat _night2 = new(10, 10, MatType.CV_8UC3, Scalar.All(3));

    public void Dispose() {
      _screen.Dispose();
      _primary.Dispose();
      _night1.Dispose();
      _night2.Dispose();
    }

    private sealed class FakeMatcher : ITemplateMatcher {
      public Dictionary<Mat, TemplateMatchResult> Results { get; } = new(ReferenceEqualityComparer.Instance);
      public List<TemplateMatcherConfig> Configs { get; } = new();

      public Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        Configs.Add(config);
        return Task.FromResult(Results.TryGetValue(templateMat, out var r)
          ? r
          : new TemplateMatchResult(Array.Empty<TemplateMatch>(), false));
      }
    }

    internal sealed class ListLogger : ILogger {
      public List<(LogLevel Level, string Message)> Entries { get; } = new();
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
    }

    private static TemplateMatchResult Result(bool limitsHit, params (int X, int Y, double Score)[] matches) =>
      new(matches.Select(m => new TemplateMatch(new BoundingBox(m.X, m.Y, 10, 10), m.Score)).ToList(), limitsHit);

    private static TemplateMatcherConfig Config(int maxResults = 10, double overlap = 0.3) => new(0.8, maxResults, overlap);

    private ReferenceSetTemplateMatcher Create(FakeMatcher inner, ILogger? logger = null) =>
      new(inner, "primary", new[] { ("night1", _night1), ("night2", _night2) }, logger);

    [Fact]
    public async Task UnionIsOrderedByConfidence() {
      var inner = new FakeMatcher();
      inner.Results[_primary] = Result(false, (0, 0, 0.86));
      inner.Results[_night1] = Result(false, (50, 50, 0.95));
      inner.Results[_night2] = Result(false, (80, 0, 0.90));

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.Matches.Select(m => (m.ReferenceId, m.Confidence)).Should().Equal(
        ("night1", 0.95), ("night2", 0.90), ("primary", 0.86));
    }

    [Fact]
    public async Task EqualScoresPreferPrimaryThenRegisteredOrder() {
      var inner = new FakeMatcher();
      inner.Results[_night2] = Result(false, (0, 0, 0.9));
      inner.Results[_night1] = Result(false, (0, 0, 0.9));
      inner.Results[_primary] = Result(false, (0, 0, 0.9));

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.Matches.Should().ContainSingle().Which.ReferenceId.Should().Be("primary");
    }

    [Fact]
    public async Task EqualScoresWithoutPrimaryPreferEarlierAlternate() {
      var inner = new FakeMatcher();
      inner.Results[_night2] = Result(false, (0, 0, 0.9));
      inner.Results[_night1] = Result(false, (0, 0, 0.9));

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.Matches.Should().ContainSingle().Which.ReferenceId.Should().Be("night1");
    }

    [Fact]
    public async Task OverlappingMatchesAcrossReferencesAreDeduplicated() {
      var inner = new FakeMatcher();
      inner.Results[_primary] = Result(false, (10, 10, 0.86));
      inner.Results[_night1] = Result(false, (11, 10, 0.97));
      inner.Results[_night2] = Result(false, (60, 60, 0.88));

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.Matches.Select(m => m.ReferenceId).Should().Equal("night1", "night2");
    }

    [Fact]
    public async Task MergedResultIsCappedAndReportsLimits() {
      var inner = new FakeMatcher();
      inner.Results[_primary] = Result(false, (0, 0, 0.86));
      inner.Results[_night1] = Result(false, (50, 50, 0.95));
      inner.Results[_night2] = Result(false, (80, 0, 0.90));

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config(maxResults: 2));

      result.Matches.Should().HaveCount(2);
      result.LimitsHit.Should().BeTrue();
    }

    [Fact]
    public async Task LimitsHitPropagatesFromAnyReference() {
      var inner = new FakeMatcher();
      inner.Results[_night2] = Result(true, (0, 0, 0.9));

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.LimitsHit.Should().BeTrue();
    }

    [Fact]
    public async Task NoReferenceMatchingYieldsEmpty() {
      var inner = new FakeMatcher();

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.Matches.Should().BeEmpty();
      result.LimitsHit.Should().BeFalse();
      inner.Configs.Should().HaveCount(3).And.OnlyContain(c => c == Config(10, 0.3));
    }

    [Fact]
    public async Task MaskDiagnosticsComeFromPrimaryAndNoInformationIsSummed() {
      var inner = new FakeMatcher();
      inner.Results[_primary] = new TemplateMatchResult(Array.Empty<TemplateMatch>(), false) {
        Masked = true, RetainedPixelCount = 42, NoInformationPositionCount = 3
      };
      inner.Results[_night1] = new TemplateMatchResult(Array.Empty<TemplateMatch>(), false) {
        Masked = false, RetainedPixelCount = 0, NoInformationPositionCount = 0
      };
      inner.Results[_night2] = new TemplateMatchResult(Array.Empty<TemplateMatch>(), false) {
        Masked = true, RetainedPixelCount = 7, NoInformationPositionCount = 5
      };

      var result = await Create(inner).MatchAllAsync(_screen, _primary, Config());

      result.Masked.Should().BeTrue();
      result.RetainedPixelCount.Should().Be(42);
      result.NoInformationPositionCount.Should().Be(8);
    }

    [Fact]
    public async Task CancellationPropagates() {
      var inner = new FakeMatcher();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => Create(inner).MatchAllAsync(_screen, _primary, Config(), cts.Token);

      await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AWinningAlternateIsLogged() {
      var inner = new FakeMatcher();
      inner.Results[_night1] = Result(false, (0, 0, 0.93));
      var logger = new ListLogger();

      await Create(inner, logger).MatchAllAsync(_screen, _primary, Config());

      logger.Entries.Should().Contain(e => e.Level == LogLevel.Information
                                           && e.Message.Contains("primary", StringComparison.Ordinal)
                                           && e.Message.Contains("night1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APrimaryWinIsNotLogged() {
      var inner = new FakeMatcher();
      inner.Results[_primary] = Result(false, (0, 0, 0.93));
      var logger = new ListLogger();

      await Create(inner, logger).MatchAllAsync(_screen, _primary, Config());

      logger.Entries.Should().BeEmpty();
    }
  }
}
