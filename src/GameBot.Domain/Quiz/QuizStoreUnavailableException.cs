namespace GameBot.Domain.Quiz;

/// <summary>
/// A quiz store cannot be used, for example its file is damaged or cannot be written (feature 142).
/// The quiz handlers turn it into the outcome <c>service_unavailable</c>.
/// </summary>
public sealed class QuizStoreUnavailableException : Exception {
  /// <summary>Creates the exception with no message.</summary>
  public QuizStoreUnavailableException() {
  }

  /// <summary>Creates the exception with a message.</summary>
  public QuizStoreUnavailableException(string message) : base(message) {
  }

  /// <summary>Creates the exception with a message and the cause.</summary>
  public QuizStoreUnavailableException(string message, Exception innerException) : base(message, innerException) {
  }
}
