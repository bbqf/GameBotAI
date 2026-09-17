#pragma warning disable CA2007, CA1861, CA1859, CA2000, CA1849
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Images;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;
using GameBot.UnitTests.Vision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GameBot.UnitTests.Images {
  public sealed class ReferenceImageSetLoaderTests {
    internal sealed class MemoryAlternates : IImageAlternatesRepository {
      private readonly Dictionary<string, IReadOnlyList<string>> _lists = new(StringComparer.OrdinalIgnoreCase);
      public IReadOnlyList<string> GetAlternates(string primaryId) => _lists.TryGetValue(primaryId, out var l) ? l : Array.Empty<string>();
      public void SetAlternates(string primaryId, IReadOnlyList<string> alternates) => _lists[primaryId] = alternates.ToArray();
      public bool DeleteAlternates(string primaryId) => _lists.Remove(primaryId);
    }

    private static MemoryReferenceImageStore StoreWith(params string[] ids) {
      var store = new MemoryReferenceImageStore();
      foreach (var id in ids) store.AddOrUpdate(id, new Bitmap(4, 4));
      return store;
    }

    [Fact]
    public void MissingPrimaryLoadsNothing() {
      var ok = ReferenceImageSetLoader.TryLoad(StoreWith("a"), new MemoryAlternates(), "primary", out var set);

      ok.Should().BeFalse();
      set.Should().BeNull();
    }

    [Fact]
    public void PrimaryWithoutAlternatesHasNone() {
      ReferenceImageSetLoader.TryLoad(StoreWith("primary"), new MemoryAlternates(), "primary", out var set).Should().BeTrue();

      set!.PrimaryId.Should().Be("primary");
      set.Alternates.Should().BeEmpty();
      set.MissingAlternateIds.Should().BeEmpty();
    }

    [Fact]
    public void NullRepositoryMeansNoAlternates() {
      ReferenceImageSetLoader.TryLoad(StoreWith("primary"), null, "primary", out var set).Should().BeTrue();

      set!.Alternates.Should().BeEmpty();
    }

    [Fact]
    public void AlternatesLoadInOrderAndMissingOnesAreReported() {
      var alts = new MemoryAlternates();
      alts.SetAlternates("primary", new[] { "c", "gone", "b" });

      ReferenceImageSetLoader.TryLoad(StoreWith("primary", "b", "c"), alts, "primary", out var set).Should().BeTrue();

      set!.Alternates.Select(a => a.Id).Should().Equal("c", "b");
      set.MissingAlternateIds.Should().Equal("gone");
    }

    [Fact]
    public void AlternatesAreNotExpandedTransitively() {
      var alts = new MemoryAlternates();
      alts.SetAlternates("primary", new[] { "b" });
      alts.SetAlternates("b", new[] { "c" });

      ReferenceImageSetLoader.TryLoad(StoreWith("primary", "b", "c"), alts, "primary", out var set).Should().BeTrue();

      set!.Alternates.Select(a => a.Id).Should().Equal("b");
    }

    [Fact]
    public void MissingAlternatesAreLoggedOnce() {
      var alts = new MemoryAlternates();
      alts.SetAlternates("primary", new[] { "gone1", "gone2" });
      ReferenceImageSetLoader.TryLoad(StoreWith("primary"), alts, "primary", out var set);
      var logger = new ReferenceSetTemplateMatcherTests.ListLogger();

      set!.LogMissingAlternates(logger);

      logger.Entries.Should().ContainSingle().Which.Should().Match<(LogLevel Level, string Message)>(e =>
        e.Level == LogLevel.Warning && e.Message.Contains("primary") && e.Message.Contains("gone1") && e.Message.Contains("gone2"));
    }

    [Fact]
    public void NothingIsLoggedWhenNoAlternateIsMissing() {
      ReferenceImageSetLoader.TryLoad(StoreWith("primary"), new MemoryAlternates(), "primary", out var set);
      var logger = new ReferenceSetTemplateMatcherTests.ListLogger();

      set!.LogMissingAlternates(logger);

      logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void WithoutAlternatesTheInnerMatcherIsReturnedAsIs() {
      ReferenceImageSetLoader.TryLoad(StoreWith("primary"), new MemoryAlternates(), "primary", out var set);
      var inner = new TemplateMatcher();

      using var matcher = set!.CreateMatcher(inner, _ => throw new InvalidOperationException("must not convert"), null);

      matcher.Matcher.Should().BeSameAs(inner);
    }

    [Fact]
    public void WithAlternatesADecoratorIsReturned() {
      var alts = new MemoryAlternates();
      alts.SetAlternates("primary", new[] { "b" });
      ReferenceImageSetLoader.TryLoad(StoreWith("primary", "b"), alts, "primary", out var set);

      using var matcher = set!.CreateMatcher(new TemplateMatcher(), _ => new Mat(4, 4, MatType.CV_8UC3), null);

      matcher.Matcher.Should().BeOfType<ReferenceSetTemplateMatcher>();
    }
  }
}
