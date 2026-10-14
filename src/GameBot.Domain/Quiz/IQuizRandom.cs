namespace GameBot.Domain.Quiz;

/// <summary>
/// The random pick of a quiz lookup for an unknown question (feature 142, research R5). It is an interface so
/// that tests are deterministic.
/// </summary>
public interface IQuizRandom {
  /// <summary>Returns a number from <paramref name="minInclusive"/> up to, not including, <paramref name="maxExclusive"/>.</summary>
  int Pick(int minInclusive, int maxExclusive);
}

/// <summary>The default <see cref="IQuizRandom"/>: it uses <see cref="Random.Shared"/>.</summary>
public sealed class SystemQuizRandom : IQuizRandom {
  /// <inheritdoc />
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security", "CA5394:Do not use insecure randomness",
    Justification = "A guess for a quiz answer needs no secure random number.")]
  public int Pick(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);
}
