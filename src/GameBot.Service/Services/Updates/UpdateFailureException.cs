using GameBot.Domain.Updates;

namespace GameBot.Service.Services.Updates;

/// <summary>An update step failed. The error holds a code, a message, and a fix hint for the user.</summary>
internal sealed class UpdateFailureException : Exception {
  public UpdateFailureException() : this(new UpdateError("update_failed", "The update failed.", null)) { }

  public UpdateFailureException(string message) : this(new UpdateError("update_failed", message, null)) { }

  public UpdateFailureException(string message, Exception innerException)
    : base(message, innerException) {
    Error = new UpdateError("update_failed", message, null);
  }

  public UpdateFailureException(UpdateError error) : base(error?.Message) {
    Error = error ?? new UpdateError("update_failed", "The update failed.", null);
  }

  public UpdateFailureException(UpdateError error, Exception innerException) : base(error?.Message, innerException) {
    Error = error ?? new UpdateError("update_failed", "The update failed.", null);
  }

  public UpdateError Error { get; }
}
