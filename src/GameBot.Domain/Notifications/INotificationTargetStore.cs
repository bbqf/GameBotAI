using System.Collections.Generic;

namespace GameBot.Domain.Notifications {
  /// <summary>
  /// Storage of notification targets (feature 120). The REST API, the UI and a hand edit of the file
  /// all use the same store. Every method returns copies, so a caller cannot change the stored list.
  /// </summary>
  public interface INotificationTargetStore {
    /// <summary>All targets. Reloads the file first when it changed. Never throws.</summary>
    IReadOnlyList<NotificationTarget> List();

    /// <summary>One target, or null when the ID is not known.</summary>
    NotificationTarget? Find(string id);

    /// <summary>Adds a target. Sets the ID and the times.</summary>
    NotificationTarget Create(NotificationTarget target);

    /// <summary>Replaces a target. Returns null when the ID is not known.</summary>
    NotificationTarget? Update(NotificationTarget target);

    /// <summary>Removes a target. Returns false when the ID is not known.</summary>
    bool Delete(string id);
  }
}
