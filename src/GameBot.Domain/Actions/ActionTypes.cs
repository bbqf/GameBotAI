namespace GameBot.Domain.Actions;

/// <summary>
/// Canonical action type keys shared across authoring, persistence, and execution.
/// </summary>
public static class ActionTypes {
  public const string Command = "command";
  public const string Tap = "tap";
  public const string Swipe = "swipe";
  public const string Key = "key";
  public const string ConnectToGame = "connect-to-game";
  public const string WaitForImage = "WaitForImage";
  public const string EnsureGameRunning = "ensure-game-running";

  /// <summary>
  /// Go-to-home-screen action (feature 069): presses the Android HOME button so the device returns
  /// to its home/main screen, leaving the game running in the background. The leave-game counterpart
  /// to <see cref="ConnectToGame"/>.
  /// </summary>
  public const string GoToHomeScreen = "go-to-home-screen";

  /// <summary>
  /// Ensure-emulator-running action (feature 070): verifies the target LDPlayer emulator instance is
  /// running and responsive (not hanging) and starts/restarts it when it is not. The
  /// emulator-lifecycle sibling of the app-lifecycle <see cref="EnsureGameRunning"/> action.
  /// </summary>
  public const string EnsureEmulatorRunning = "ensure-emulator-running";

  /// <summary>
  /// Self-reschedule action (feature 065): schedules one additional firing of the current
  /// sequence into its originating queue run. No-op success when not started from a queue.
  /// </summary>
  public const string RescheduleSelf = "reschedule-self";

  /// <summary>
  /// Notify action (feature 087): raises an outbound alert carrying an author-written message, so
  /// escalation can live inside a committed, reviewable sequence instead of in service
  /// configuration. Performs no device interaction — which is the point: a guard that has detected
  /// an unusable screen must still be able to say so.
  /// <para>
  /// Always succeeds. A delivery failure is recorded on the run's health and in the application
  /// log, but never fails the step or the enclosing sequence.
  /// </para>
  /// </summary>
  public const string Notify = "notify";

  /// <summary>
  /// Quiz-lookup action (feature 142): reads a quiz question and its answers from the screen, looks the
  /// question up in a quiz answer store, and gives the outcome <c>pick_N</c> (the position to tap),
  /// <c>unreadable</c>, or <c>service_unavailable</c>. A service-level action: it never taps.
  /// </summary>
  public const string QuizLookup = "quiz-lookup";

  /// <summary>
  /// Quiz-learn action (feature 142): stores the correct answer of an earlier <see cref="QuizLookup"/>
  /// step in the quiz answer store. A service-level action.
  /// </summary>
  public const string QuizLearn = "quiz-learn";
}
