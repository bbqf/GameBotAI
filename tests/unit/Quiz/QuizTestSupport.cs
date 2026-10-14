using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Quiz;
using GameBot.Service.Services.Quiz;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA1002, CA1819, CA2227

namespace GameBot.UnitTests.Quiz;

/// <summary>A screen reader that returns the texts it was given, in order. Null gives a failed read.</summary>
internal sealed class FakeQuizScreenReader : IQuizScreenReader {
  private readonly Queue<IReadOnlyList<string?>?> _reads = new();

  public int Calls { get; private set; }

  public IReadOnlyList<OcrOffsetRegion>? LastRegions { get; private set; }

  public string? LastSessionId { get; private set; }

  public void Enqueue(params string?[] texts) => _reads.Enqueue(texts);

  public void EnqueueFailure() => _reads.Enqueue(null);

  public IReadOnlyList<string?>? ReadRegions(string? sessionId, IReadOnlyList<OcrOffsetRegion> regions) {
    Calls++;
    LastRegions = regions;
    LastSessionId = sessionId;
    return _reads.Count > 0 ? _reads.Dequeue() : null;
  }
}

/// <summary>A random source that returns fixed values.</summary>
internal sealed class FixedQuizRandom : IQuizRandom {
  private readonly int _value;

  public FixedQuizRandom(int value) {
    _value = value;
  }

  public int Calls { get; private set; }

  public int Pick(int minInclusive, int maxExclusive) {
    Calls++;
    return Math.Clamp(_value, minInclusive, maxExclusive - 1);
  }
}

/// <summary>A repository in memory, with the same key and match rules as the file repository.</summary>
internal sealed class InMemoryQuizRepository : IQuizAnswerRepository {
  private readonly List<QuizRecord> _records = new();

  public bool Unavailable { get; set; }

  public IReadOnlyList<QuizRecord> Records => _records;

  public Task<QuizRecord?> FindBestAsync(string store, string game, string category, string question, double similarity, CancellationToken ct = default) {
    if (Unavailable) throw new QuizStoreUnavailableException("down");
    var normalized = QuizTextNormalizer.Normalize(question);
    return Task.FromResult(_records
      .Where(r => Same(r.Store, store) && r.Game == game && Same(r.Category, category))
      .Select(r => (Record: r, Score: QuizSimilarity.Score(normalized, r.Question, similarity)))
      .Where(x => x.Score >= similarity)
      .OrderByDescending(x => x.Score)
      .ThenByDescending(x => x.Record.UpdatedAt)
      .Select(x => x.Record)
      .FirstOrDefault());
  }

  public Task<QuizUpsertResult> UpsertAsync(string store, string game, string category, string question, string answer, CancellationToken ct = default) {
    if (Unavailable) throw new QuizStoreUnavailableException("down");
    var q = QuizTextNormalizer.Normalize(question);
    var a = QuizTextNormalizer.Normalize(answer);
    var existing = _records.FirstOrDefault(r => Same(r.Store, store) && r.Game == game && Same(r.Category, category) && r.Question == q);
    var now = DateTimeOffset.UtcNow;
    if (existing is null) {
      var created = new QuizRecord { Id = Guid.NewGuid().ToString("N"), Store = store, Game = game, Category = category, Question = q, Answer = a, CreatedAt = now, UpdatedAt = now };
      _records.Add(created);
      return Task.FromResult(new QuizUpsertResult(QuizUpsertKind.Created, created, null));
    }

    if (existing.Answer == a) {
      existing.UpdatedAt = now;
      return Task.FromResult(new QuizUpsertResult(QuizUpsertKind.Unchanged, existing, null));
    }

    var previous = existing.Answer;
    existing.Answer = a;
    existing.UpdatedAt = now;
    return Task.FromResult(new QuizUpsertResult(QuizUpsertKind.Replaced, existing, previous));
  }

  public Task<IReadOnlyList<QuizRecord>> ListAsync(string? store, string? game, string? category, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<QuizRecord>>(_records.ToList());

  public Task<QuizRecord?> GetAsync(string id, CancellationToken ct = default) =>
    Task.FromResult(_records.FirstOrDefault(r => r.Id == id));

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) =>
    Task.FromResult(_records.RemoveAll(r => r.Id == id) > 0);

  private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Builds quiz step payloads for the tests.</summary>
internal static class QuizSteps {
  public static OcrOffsetRegion Region(int y) => new(40, y, 400, 60);

  public static object RegionJson(int y) => new { x = 40, y, width = 400, height = 60 };

  public static SequenceActionPayload Lookup(
      string game = "pns",
      string category = "radio",
      string store = "pns-quiz",
      int answers = 3,
      double? similarity = null) {
    var action = new SequenceActionPayload { Type = "quiz-lookup" };
    action.Parameters["game"] = game;
    action.Parameters["category"] = category;
    action.Parameters["store"] = store;
    action.Parameters["question"] = RegionJson(200);
    action.Parameters["answers"] = Enumerable.Range(0, answers).Select(i => RegionJson(320 + (i * 80))).ToList();
    if (similarity is { } s) action.Parameters["similarity"] = s;
    return action;
  }

  public static SequenceActionPayload LearnByPosition(string lookupStepId = "lookup-1", int position = 2) {
    var action = new SequenceActionPayload { Type = "quiz-learn" };
    action.Parameters["lookupStepId"] = lookupStepId;
    action.Parameters["correctPosition"] = position;
    return action;
  }

  public static SequenceActionPayload LearnByRegion(string lookupStepId = "lookup-1") {
    var action = new SequenceActionPayload { Type = "quiz-learn" };
    action.Parameters["lookupStepId"] = lookupStepId;
    action.Parameters["correctRegion"] = RegionJson(500);
    return action;
  }
}
