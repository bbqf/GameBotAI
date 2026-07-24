using System;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// Persisted configuration of an emulator execution queue.
  /// Only configuration is durable; the ordered sequence entries and the execution
  /// status live in <see cref="IQueueRuntimeStore"/> and are intentionally NOT persisted
  /// (they reset on every service restart).
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The domain concept is an execution queue; 'Queue' is the correct noun here.")]
  public class ExecutionQueue {
    /// <summary>Stable identifier (GUID "N"); generated on create when absent.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Required, non-empty display name. Need not be unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ADB device serial this queue is bound to. Immutable after creation.</summary>
    public string EmulatorSerial { get; set; } = string.Empty;

    /// <summary>
    /// Stored-only flag; its runtime semantics are deferred until a real execution
    /// engine exists. Persisted and surfaced to the UI but has no runtime effect.
    /// </summary>
    public bool CycleExecution { get; set; }

    /// <summary>
    /// When true, the run backs the game out to the device home screen during idle gaps that
    /// exceed <see cref="IdleThresholdSeconds"/> and foregrounds it again when the next firing is
    /// due (feature 073). Opt-in; <c>false</c> preserves today's behavior (game left untouched
    /// between firings). Read once per run at run start.
    /// </summary>
    public bool PauseWhenIdle { get; set; }

    /// <summary>
    /// Minimum gap in seconds to the next scheduled firing that triggers an idle pause when
    /// <see cref="PauseWhenIdle"/> is enabled. Defaults to 30; the initializer default also gives
    /// JSON back-compat for queues stored before this field existed. Values below 1 are coerced to
    /// the default at the API boundary. Ignored while <see cref="PauseWhenIdle"/> is false.
    /// </summary>
    public int IdleThresholdSeconds { get; set; } = 30;

    /// <summary>
    /// Optional LDPlayer instance name to cold-start at queue run start (feature 074). When set (or
    /// <see cref="EmulatorInstanceIndex"/> is set), the queue verifies/starts this emulator instance —
    /// reusing the feature-070 ensure-emulator-running capability with <see cref="EmulatorSerial"/> as
    /// the responsiveness probe — BEFORE it binds its device session, so a queue can self-start from a
    /// backend-only cold state. Null/blank ⇒ no emulator management (behaves exactly as before).
    /// When both name and index are supplied, the name takes precedence.
    /// </summary>
    public string? EmulatorInstanceName { get; set; }

    /// <summary>
    /// Optional LDPlayer instance index for the feature-074 pre-session cold-start (see
    /// <see cref="EmulatorInstanceName"/>). Null ⇒ not used; when supplied it MUST be ≥ 0.
    /// </summary>
    public int? EmulatorInstanceIndex { get; set; }

    /// <summary>
    /// Optional link to a single queue template by its stable template ID
    /// (0..1). Persisted. References by ID, so renaming the template keeps the link intact;
    /// only deleting the template makes it unresolvable. Null when the queue is unlinked.
    /// When set, the linked template's entries are auto-loaded into the queue's runtime on
    /// the first display after a service start.
    /// </summary>
    public string? LinkedTemplateId { get; set; }

    /// <summary>
    /// Optional link to a single game by its stable ID (0..1). Persisted.
    /// When set, game-aware actions in this queue's sequences resolve the target game automatically.
    /// Null when the queue is unlinked.
    /// </summary>
    public string? LinkedGameId { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
  }
}
