namespace GameBot.Domain.Quiz;

/// <summary>
/// Makes the id of the running quiz step known to the quiz handler (feature 142). The action dispatcher gets only
/// the action, not the step, but a <c>quiz-lookup</c> step must keep its result under its step id, so that a
/// later <c>quiz-learn</c> step can find it. The sequence runner pushes the id around the dispatch of a quiz step.
/// </summary>
public static class QuizStepScope {
  private static readonly AsyncLocal<string?> CurrentStep = new();

  /// <summary>The id of the quiz step that runs now, or null.</summary>
  public static string? Current => CurrentStep.Value;

  /// <summary>Sets the current step id until the returned object is disposed.</summary>
  public static IDisposable Push(string? stepId) {
    var previous = CurrentStep.Value;
    CurrentStep.Value = stepId;
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly string? _previous;

    public Restore(string? previous) {
      _previous = previous;
    }

    public void Dispose() => CurrentStep.Value = _previous;
  }
}
