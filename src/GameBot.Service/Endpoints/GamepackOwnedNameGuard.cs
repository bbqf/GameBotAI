using System.Text.Json;
using GameBot.Domain.Gamepacks;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The narrow guard for a hand create (feature 143, US2 scenario 5). A create whose type and name match an object
/// that a gamepack owns gives 409. A create with any other name works as before.
/// </summary>
internal static class GamepackOwnedNameGuard {
  /// <summary>Checks a create request body. Returns a 409 result, or null when the create can go on.</summary>
  /// <param name="gamepacks">The gamepack service.</param>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="body">The request body.</param>
  /// <param name="member">The member that holds the name.</param>
  /// <param name="ct">The cancellation token.</param>
  public static async Task<IResult?> CheckAsync(GamepackService gamepacks, string type, JsonElement body, string member, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(gamepacks);
    if (body.ValueKind != JsonValueKind.Object) {
      return null;
    }

    foreach (var property in body.EnumerateObject()) {
      if (string.Equals(property.Name, member, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String) {
        return await CheckNameAsync(gamepacks, type, property.Value.GetString(), ct).ConfigureAwait(false);
      }
    }

    return null;
  }

  /// <summary>Checks a name. Returns a 409 result, or null when the create can go on.</summary>
  /// <param name="gamepacks">The gamepack service.</param>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="name">The name of the new object.</param>
  /// <param name="ct">The cancellation token.</param>
  public static async Task<IResult?> CheckNameAsync(GamepackService gamepacks, string type, string? name, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(gamepacks);
    if (string.IsNullOrWhiteSpace(name) || !await gamepacks.IsNameOwnedAsync(type, name.Trim(), ct).ConfigureAwait(false)) {
      return null;
    }

    return Results.Conflict(new {
      error = new {
        code = "owned_by_gamepack",
        message = $"A {type} with this name exists and a gamepack owns it.",
        hint = "Edit the existing object, or use another name."
      }
    });
  }
}
