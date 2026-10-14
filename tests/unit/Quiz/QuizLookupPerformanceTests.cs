using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Quiz;
using GameBot.Service.Services.Quiz;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Quiz;

/// <summary>Feature 142 (SC-005): one lookup on a store with 200 records is fast.</summary>
public sealed class QuizLookupPerformanceTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-quiz-perf-" + Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  [Fact]
  public async Task OneLookupOn200RecordsIsFast() {
    var file = new FileQuizAnswerRepository(_root);
    var memory = new InMemoryQuizRepository();
    for (var i = 0; i < 200; i++) {
      var question = $"What is the capital city number {i} of the very large quiz country";
      await file.UpsertAsync("pns-quiz", "pns", "radio", question, $"city {i}");
      await memory.UpsertAsync("pns-quiz", "pns", "radio", question, $"city {i}");
    }

    FakeQuizScreenReader Reader() {
      var reader = new FakeQuizScreenReader();
      reader.Enqueue("What is the capital city number 150 of the very large quiz country?", "city 1", "city 150", "city 2");
      return reader;
    }

    // The file store: under 1 second for the whole step, with the disk read.
    var fileHandler = new QuizLookupHandler(file, Reader(), new FixedQuizRandom(1), new QuizLookupResultKeeper());
    var fileWatch = Stopwatch.StartNew();
    var fileResult = await fileHandler.ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "s", "run-1", CancellationToken.None);
    fileWatch.Stop();

    // The in-memory store (the same scan, no disk): under 50 ms. The file run above warmed the code. The best of
    // three runs counts, so a pause of the test host (for example a coverage run) does not fail the test.
    var memoryResult = default(GameBot.Domain.Services.ActionDispatchResult);
    var memoryBest = TimeSpan.MaxValue;
    for (var run = 0; run < 3; run++) {
      var memoryHandler = new QuizLookupHandler(memory, Reader(), new FixedQuizRandom(1), new QuizLookupResultKeeper());
      var memoryWatch = Stopwatch.StartNew();
      memoryResult = await memoryHandler.ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "s", "run-1", CancellationToken.None);
      memoryWatch.Stop();
      if (memoryWatch.Elapsed < memoryBest) memoryBest = memoryWatch.Elapsed;
    }

    fileResult.Outcome.Should().Be("pick_2");
    memoryResult!.Outcome.Should().Be("pick_2");
    fileWatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    memoryBest.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
  }
}
