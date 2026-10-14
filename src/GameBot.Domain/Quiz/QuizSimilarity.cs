namespace GameBot.Domain.Quiz;

/// <summary>
/// Text similarity from 0 to 1 (feature 142, research R3): <c>1 - levenshtein / max(length)</c> on text that
/// is already normalized with <see cref="QuizTextNormalizer"/>.
/// </summary>
public static class QuizSimilarity {
  /// <summary>
  /// The similarity of two normalized texts. Two equal non-empty texts give 1. An empty text never matches:
  /// if one text or both are empty the result is 0.
  /// </summary>
  public static double Score(string? left, string? right) => Score(left, right, 0);

  /// <summary>
  /// Like <see cref="Score(string?, string?)"/>, with a lower limit. When the similarity is at least
  /// <paramref name="minScore"/>, the result is the exact similarity. When it is lower, the result is 0 and the
  /// code stops early: it only looks at the edits that the limit allows, so a scan of many records is fast.
  /// </summary>
  public static double Score(string? left, string? right, double minScore) {
    if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) {
      return 0;
    }

    if (string.Equals(left, right, StringComparison.Ordinal)) {
      return 1;
    }

    var longest = Math.Max(left.Length, right.Length);
    var limit = Math.Clamp(minScore, 0, 1);
    var allowed = (int)Math.Floor(((1.0 - limit) * longest) + 1e-9);
    if (Math.Abs(left.Length - right.Length) > allowed) {
      return 0;
    }

    var distance = BoundedLevenshtein(left, right, allowed);
    if (distance > allowed) {
      return 0;
    }

    var score = 1.0 - ((double)distance / longest);
    // A score that is lower than the limit only by a rounding error still counts as the limit.
    return score < limit ? limit : score;
  }

  // Levenshtein distance with a band of width 2 * maxDistance + 1. It gives maxDistance + 1 when the distance is
  // more than maxDistance.
  private static int BoundedLevenshtein(string a, string b, int maxDistance) {
    var n = a.Length;
    var m = b.Length;
    var exceeded = maxDistance + 1;
    var previous = new int[m + 1];
    var current = new int[m + 1];
    for (var j = 0; j <= m; j++) {
      previous[j] = j <= maxDistance ? j : exceeded;
    }

    for (var i = 1; i <= n; i++) {
      var from = Math.Max(1, i - maxDistance);
      var to = Math.Min(m, i + maxDistance);
      current[0] = i <= maxDistance ? i : exceeded;
      if (from > 1) {
        current[from - 1] = exceeded;
      }

      var rowMinimum = from == 1 ? current[0] : exceeded;
      for (var j = from; j <= to; j++) {
        var cost = a[i - 1] == b[j - 1] ? 0 : 1;
        var value = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
        value = Math.Min(value, exceeded);
        current[j] = value;
        if (value < rowMinimum) {
          rowMinimum = value;
        }
      }

      if (to < m) {
        current[to + 1] = exceeded;
      }

      if (rowMinimum > maxDistance) {
        return exceeded;
      }

      (previous, current) = (current, previous);
    }

    return Math.Min(previous[m], exceeded);
  }
}
