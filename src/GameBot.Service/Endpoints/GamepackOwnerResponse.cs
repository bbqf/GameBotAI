using GameBot.Domain.Gamepacks;
using GameBot.Service.Endpoints.Dto;

namespace GameBot.Service.Endpoints;

/// <summary>Adds the read-only <c>owner</c> member to the read responses of sequences, commands, and images (feature 143).</summary>
internal static class GamepackOwnerResponse {
  /// <summary>Gets the owner member of an existing object.</summary>
  /// <param name="owners">The owner resolver for the request.</param>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="id">The stored id of the object.</param>
  public static GamepackOwnerDto For(GamepackOwnerResolver owners, string type, string id) {
    ArgumentNullException.ThrowIfNull(owners);
    return GamepackOwnerDto.From(owners.Resolve(type, id));
  }

  /// <summary>Copies the members of an anonymous or typed response and adds <c>owner</c>.</summary>
  /// <param name="response">The response object.</param>
  /// <param name="owner">The owner member.</param>
  public static object With(object response, GamepackOwnerDto owner) {
    ArgumentNullException.ThrowIfNull(response);
    var members = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var property in response.GetType().GetProperties()) {
      members[property.Name] = property.GetValue(response);
    }

    members["owner"] = owner;
    return members;
  }
}
