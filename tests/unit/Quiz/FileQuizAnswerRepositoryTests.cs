using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Quiz;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Quiz;

/// <summary>Feature 142: the file store of quiz answers (persistence, key, isolation, damaged files).</summary>
public sealed class FileQuizAnswerRepositoryTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-quiz-" + Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  private FileQuizAnswerRepository Create() => new(_root);

  [Fact]
  public async Task StoredRecordSurvivesARestart() {
    var first = Create();
    var created = await first.UpsertAsync("pns-quiz", "pns", "radio", "Which planet is red?", "Mars");

    var second = Create();
    var found = await second.FindBestAsync("pns-quiz", "pns", "radio", "which planet is red", 0.85);

    created.Kind.Should().Be(QuizUpsertKind.Created);
    found.Should().NotBeNull();
    found!.Id.Should().Be(created.Record.Id);
    found.Question.Should().Be("which planet is red");
    found.Answer.Should().Be("mars");
    Directory.GetFiles(Path.Combine(_root, "quiz"), "*.json").Should().HaveCount(1);
  }

  [Fact]
  public async Task FileNameIsAHashNotTheStoreName() {
    await Create().UpsertAsync("../evil\\name", "pns", "radio", "q one", "a one");

    var files = Directory.GetFiles(Path.Combine(_root, "quiz"));
    files.Should().HaveCount(1);
    Path.GetFileNameWithoutExtension(files[0]).Should().MatchRegex("^[0-9a-f]{32}$");
  }

  [Fact]
  public async Task SameKeyWithSameAnswerGivesOneRecordAndNewUpdatedAt() {
    var repo = Create();
    var first = await repo.UpsertAsync("s", "pns", "radio", "Question one", "Alpha");
    await Task.Delay(15);
    var second = await repo.UpsertAsync("S", "pns", "RADIO", "question one!", "alpha");

    second.Kind.Should().Be(QuizUpsertKind.Unchanged);
    second.Record.Id.Should().Be(first.Record.Id);
    second.Record.UpdatedAt.Should().BeAfter(first.Record.UpdatedAt);
    (await repo.ListAsync(null, null, null)).Should().HaveCount(1);
  }

  [Fact]
  public async Task SameKeyWithNewAnswerReplacesTheAnswer() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "q one", "alpha");

    var result = await repo.UpsertAsync("s", "pns", "radio", "q one", "beta");

    result.Kind.Should().Be(QuizUpsertKind.Replaced);
    result.PreviousAnswer.Should().Be("alpha");
    result.Record.Answer.Should().Be("beta");
    (await repo.ListAsync(null, null, null)).Should().ContainSingle().Which.Answer.Should().Be("beta");
  }

  [Fact]
  public async Task ConcurrentWritesKeepEveryRecord() {
    var repo = Create();

    await Task.WhenAll(Enumerable.Range(0, 25).Select(i =>
      repo.UpsertAsync("s", "pns", "radio", $"question number {i} text", $"answer {i}")));

    var records = await repo.ListAsync("s", null, null);
    records.Should().HaveCount(25);
    Directory.GetFiles(Path.Combine(_root, "quiz")).Should().OnlyContain(f => f.EndsWith(".json", StringComparison.Ordinal));
  }

  [Fact]
  public async Task DamagedFileMakesThatStoreUnavailableButNotOthers() {
    var repo = Create();
    await repo.UpsertAsync("good", "pns", "radio", "q good", "a good");
    await repo.UpsertAsync("bad", "pns", "radio", "q bad", "a bad");
    var badFile = Path.Combine(_root, "quiz", FileQuizAnswerRepository.StoreKey("bad") + ".json");
    await File.WriteAllTextAsync(badFile, "{ this is not json");

    var fresh = Create();
    Func<Task> findBad = () => fresh.FindBestAsync("bad", "pns", "radio", "q bad", 0.85);
    Func<Task> writeBad = () => fresh.UpsertAsync("bad", "pns", "radio", "q new", "a new");

    await findBad.Should().ThrowAsync<QuizStoreUnavailableException>();
    await writeBad.Should().ThrowAsync<QuizStoreUnavailableException>();
    (await fresh.FindBestAsync("good", "pns", "radio", "q good", 0.85)).Should().NotBeNull();
  }

  [Fact]
  public async Task ListSkipsADamagedStore() {
    var repo = Create();
    await repo.UpsertAsync("good", "pns", "radio", "q good", "a good");
    await repo.UpsertAsync("bad", "pns", "radio", "q bad", "a bad");
    await File.WriteAllTextAsync(Path.Combine(_root, "quiz", FileQuizAnswerRepository.StoreKey("bad") + ".json"), "not json");

    var all = await Create().ListAsync(null, null, null);
    var onlyBad = await Create().ListAsync("bad", null, null);

    all.Should().ContainSingle().Which.Store.Should().Be("good");
    onlyBad.Should().BeEmpty();
  }

  [Fact]
  public async Task EmptyFileMakesTheStoreUnavailable() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "q one", "a one");
    await File.WriteAllTextAsync(Path.Combine(_root, "quiz", FileQuizAnswerRepository.StoreKey("s") + ".json"), "null");

    Func<Task> act = () => Create().FindBestAsync("s", "pns", "radio", "q one", 0.85);

    await act.Should().ThrowAsync<QuizStoreUnavailableException>();
  }

  [Fact]
  public async Task GetAndDeleteWorkByIdAcrossStores() {
    var repo = Create();
    var a = await repo.UpsertAsync("one", "pns", "radio", "q one", "a one");
    var b = await repo.UpsertAsync("two", "pns", "radio", "q two", "a two");

    (await repo.GetAsync(b.Record.Id))!.Question.Should().Be("q two");
    (await repo.DeleteAsync(a.Record.Id)).Should().BeTrue();
    (await repo.DeleteAsync(a.Record.Id)).Should().BeFalse();
    (await repo.GetAsync(a.Record.Id)).Should().BeNull();
    (await repo.ListAsync(null, null, null)).Should().ContainSingle().Which.Id.Should().Be(b.Record.Id);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("not-a-guid")]
  [InlineData("../../etc/passwd")]
  [InlineData("3f2a")]
  public async Task BadIdGivesNullOrFalse(string id) {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "q one", "a one");

    (await repo.GetAsync(id)).Should().BeNull();
    (await repo.DeleteAsync(id)).Should().BeFalse();
  }

  [Fact]
  public async Task UnknownIdGivesNull() {
    var repo = Create();

    (await repo.GetAsync(Guid.NewGuid().ToString("N"))).Should().BeNull();
    (await repo.DeleteAsync(Guid.NewGuid().ToString("N"))).Should().BeFalse();
  }

  // ---- User Story 4: isolation by game, category, and store (T038) ----

  [Fact]
  public async Task SameQuestionInTwoCategoriesGivesTwoAnswers() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "Capital of France?", "Paris");
    await repo.UpsertAsync("s", "pns", "history", "Capital of France?", "Lutetia");

    var radio = await repo.FindBestAsync("s", "pns", "radio", "capital of france", 0.85);
    var history = await repo.FindBestAsync("s", "pns", "history", "capital of france", 0.85);

    radio!.Answer.Should().Be("paris");
    history!.Answer.Should().Be("lutetia");
  }

  [Fact]
  public async Task LookupFindsNothingInAnotherGameCategoryOrStore() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "q one", "a one");

    (await repo.FindBestAsync("s", "other-game", "radio", "q one", 0.85)).Should().BeNull();
    (await repo.FindBestAsync("s", "pns", "other-category", "q one", 0.85)).Should().BeNull();
    (await repo.FindBestAsync("other-store", "pns", "radio", "q one", 0.85)).Should().BeNull();
  }

  [Fact]
  public async Task CategoryAndStoreIgnoreCaseButGameIsExact() {
    var repo = Create();
    await repo.UpsertAsync("Pns-Quiz", "pns", "Radio", "q one", "a one");

    (await repo.FindBestAsync("PNS-QUIZ", "pns", "RADIO", "q one", 0.85)).Should().NotBeNull();
    (await repo.FindBestAsync("pns-quiz", "PNS", "radio", "q one", 0.85)).Should().BeNull();
    (await repo.ListAsync("pns-QUIZ", null, "rAdIo")).Should().HaveCount(1);
    (await repo.ListAsync(null, "PNS", null)).Should().BeEmpty();
    (await repo.ListAsync(null, "pns", null)).Should().HaveCount(1);
  }

  [Theory]
  [InlineData("", "radio")]
  [InlineData("pns", "")]
  [InlineData("pns", "   ")]
  public async Task UpsertRejectsABlankGameOrCategory(string game, string category) {
    Func<Task> act = () => Create().UpsertAsync("s", game, category, "q one", "a one");

    await act.Should().ThrowAsync<ArgumentException>();
    Directory.Exists(Path.Combine(_root, "quiz")).Should().BeFalse();
  }

  [Theory]
  [InlineData("", "a one")]
  [InlineData("?!", "a one")]
  [InlineData("q one", "")]
  public async Task UpsertRejectsAnEmptyQuestionOrAnswer(string question, string answer) {
    Func<Task> act = () => Create().UpsertAsync("s", "pns", "radio", question, answer);

    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task FindWithBlankGameOrCategoryOrQuestionGivesNull() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "q one", "a one");

    (await repo.FindBestAsync("s", "", "radio", "q one", 0.85)).Should().BeNull();
    (await repo.FindBestAsync("s", "pns", " ", "q one", 0.85)).Should().BeNull();
    (await repo.FindBestAsync("s", "pns", "radio", "?!", 0.85)).Should().BeNull();
  }

  [Fact]
  public async Task FindToleratesASmallOcrErrorAndPicksTheBestScore() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "Which planet is the closest to the sun", "Mercury");
    await repo.UpsertAsync("s", "pns", "radio", "Which planet is the closest to the moon", "None");

    var found = await repo.FindBestAsync("s", "pns", "radio", "Whlch planet is the closest to the sun", 0.85);

    found!.Answer.Should().Be("mercury");
  }

  [Fact]
  public async Task OnATieTheNewestRecordWins() {
    var repo = Create();
    // Two different categories cannot tie, so use two records of one key space that score the same.
    await repo.UpsertAsync("s", "pns", "radio", "abcdefgh one", "first");
    await Task.Delay(15);
    await repo.UpsertAsync("s", "pns", "radio", "abcdefgh two", "second");

    var found = await repo.FindBestAsync("s", "pns", "radio", "abcdefgh xxx", 0.5);

    found!.Answer.Should().Be("second");
  }

  [Fact]
  public async Task ListIsSortedAndFiltersCombine() {
    var repo = Create();
    await repo.UpsertAsync("b-store", "pns", "radio", "q two", "a");
    await repo.UpsertAsync("a-store", "pns", "radio", "q one", "a");
    await repo.UpsertAsync("a-store", "pns", "history", "q three", "a");

    var all = await repo.ListAsync(null, null, null);
    var filtered = await repo.ListAsync("a-store", "pns", "radio");

    all.Select(r => r.Store).Should().Equal("a-store", "a-store", "b-store");
    filtered.Should().ContainSingle().Which.Question.Should().Be("q one");
  }

  [Fact]
  public async Task WriteFailureGivesStoreUnavailable() {
    var repo = Create();
    await repo.UpsertAsync("s", "pns", "radio", "q one", "a one");
    // A directory with the name of the store file makes the replace fail.
    var file = Path.Combine(_root, "quiz", FileQuizAnswerRepository.StoreKey("blocked") + ".json");
    Directory.CreateDirectory(file);

    Func<Task> act = () => repo.UpsertAsync("blocked", "pns", "radio", "q one", "a one");

    await act.Should().ThrowAsync<QuizStoreUnavailableException>();
    Directory.GetFiles(Path.Combine(_root, "quiz"), "*.tmp").Should().BeEmpty();
  }

  [Fact]
  public void ExceptionConstructorsKeepMessageAndCause() {
    var inner = new InvalidOperationException("inner");

    new QuizStoreUnavailableException().Should().NotBeNull();
    new QuizStoreUnavailableException("m").Message.Should().Be("m");
    new QuizStoreUnavailableException("m", inner).InnerException.Should().BeSameAs(inner);
  }

  [Fact]
  public void SystemRandomStaysInRange() {
    var random = new SystemQuizRandom();

    var values = Enumerable.Range(0, 50).Select(_ => random.Pick(1, 4)).ToList();

    values.Should().OnlyContain(v => v >= 1 && v <= 3);
  }
}
