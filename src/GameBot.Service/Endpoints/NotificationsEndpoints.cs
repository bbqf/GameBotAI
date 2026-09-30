using System.Text.Json;
using GameBot.Domain.Notifications;
using GameBot.Service.Contracts.Notifications;
using GameBot.Service.Services.Notifications;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Notification targets, the test send and the target types (feature 120). A bad body gives 400 and
/// never 500. The response never has a secret value, and no error text repeats one.
/// </summary>
internal static class NotificationsEndpoints {
  private const int MaxNameLength = 100;
  private static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(15);
  private const string TimeoutReason = "The target did not answer in time.";

  public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.NotificationTargets).WithTags("Notifications");

    group.MapGet("", (INotificationTargetStore store, IEnumerable<INotificationChannel> channels) => {
      var byType = ChannelsByType(channels);
      return Results.Ok(store.List().Select(t => View(t, byType)).ToList());
    }).WithName("ListNotificationTargets");

    group.MapPost("", async (HttpRequest request, INotificationTargetStore store, IEnumerable<INotificationChannel> channels) => {
      var (body, bodyError) = await ReadBodyAsync(request).ConfigureAwait(false);
      if (bodyError is not null) return bodyError;
      var byType = ChannelsByType(channels);
      var (target, channel, error) = BuildTarget(body!, existing: null, byType);
      if (error is not null) return error;
      var created = store.Create(target!);
      return Results.Created($"{ApiRoutes.NotificationTargets}/{created.Id}", View(created, byType));
    }).WithName("CreateNotificationTarget");

    group.MapPut("{id}", async (string id, HttpRequest request, INotificationTargetStore store, IEnumerable<INotificationChannel> channels) => {
      var existing = store.Find(id);
      if (existing is null) return NotFound();
      var (body, bodyError) = await ReadBodyAsync(request).ConfigureAwait(false);
      if (bodyError is not null) return bodyError;
      var byType = ChannelsByType(channels);
      var (target, _, error) = BuildTarget(body!, existing, byType);
      if (error is not null) return error;
      target!.Id = existing.Id;
      var updated = store.Update(target);
      return updated is null ? NotFound() : Results.Ok(View(updated, byType));
    }).WithName("UpdateNotificationTarget");

    group.MapDelete("{id}", (string id, INotificationTargetStore store) =>
      store.Delete(id) ? Results.NoContent() : NotFound()
    ).WithName("DeleteNotificationTarget");

    // Sends one test message. It ignores the enabled flag, the queue levels and the failure streaks.
    group.MapPost("{id}/test", async (string id, INotificationTargetStore store, IEnumerable<INotificationChannel> channels, CancellationToken requestAborted) => {
      var target = store.Find(id);
      if (target is null) return NotFound();
      var byType = ChannelsByType(channels);
      if (!byType.TryGetValue(target.Type, out var channel)) {
        return Results.Ok(new NotificationTestResponse { Ok = false, Reason = "The target type is not known." });
      }

      using var limit = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
      limit.CancelAfter(TestLimit);
      try {
        var result = await channel.SendAsync(target, NotificationMessageFormatter.TestMessageText, limit.Token)
          .WaitAsync(TestLimit, requestAborted).ConfigureAwait(false);
        return Results.Ok(new NotificationTestResponse { Ok = result.Succeeded, Reason = result.Succeeded ? null : result.Reason });
      }
      catch (TimeoutException) {
        await limit.CancelAsync().ConfigureAwait(false);
        return Results.Ok(new NotificationTestResponse { Ok = false, Reason = TimeoutReason });
      }
      catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested) {
        return Results.Ok(new NotificationTestResponse { Ok = false, Reason = TimeoutReason });
      }
    }).WithName("TestNotificationTarget");

    app.MapGet(ApiRoutes.NotificationTypes, (IEnumerable<INotificationChannel> channels) =>
      Results.Ok(channels.Select(c => new NotificationTypeView {
        Type = c.Type,
        DisplayName = c.DisplayName,
        Fields = c.Fields.Select(f => new NotificationFieldView { Key = f.Key, Label = f.Label, Secret = f.Secret, Required = f.Required }).ToList()
      }).ToList())
    ).WithName("ListNotificationTypes").WithTags("Notifications");

    return app;
  }

  private static Dictionary<string, INotificationChannel> ChannelsByType(IEnumerable<INotificationChannel> channels) =>
    channels.ToDictionary(c => c.Type, StringComparer.OrdinalIgnoreCase);

  private static async Task<(NotificationTargetRequest? Body, IResult? Error)> ReadBodyAsync(HttpRequest request) {
    try {
      var body = await request.ReadFromJsonAsync<NotificationTargetRequest>().ConfigureAwait(false);
      return body is null ? (null, Error(400, "invalid_request", "A JSON body is required.")) : (body, null);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException) {
      return (null, Error(400, "invalid_request", "The body is not valid JSON for a notification target."));
    }
  }

  // Builds the stored target from a request. On an update, an empty secret keeps the stored one.
  private static (NotificationTarget? Target, INotificationChannel? Channel, IResult? Error) BuildTarget(
    NotificationTargetRequest body,
    NotificationTarget? existing,
    Dictionary<string, INotificationChannel> byType) {
    var type = body.Type?.Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(type) || !byType.TryGetValue(type, out var channel)) {
      var known = string.Join(", ", byType.Keys.OrderBy(k => k, StringComparer.Ordinal));
      return (null, null, Error(400, "invalid_request", $"type is required. Known types: {known}."));
    }

    if (existing is not null && !string.Equals(existing.Type, type, StringComparison.OrdinalIgnoreCase)) {
      return (null, null, Error(400, "invalid_request", "The type of a saved target cannot change."));
    }

    var name = body.Name?.Trim();
    if (string.IsNullOrEmpty(name)) return (null, null, Error(400, "invalid_request", "name is required."));
    if (name.Length > MaxNameLength) return (null, null, Error(400, "invalid_request", $"name must have at most {MaxNameLength} characters."));

    var secretKeys = channel.Fields.Where(f => f.Secret).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
    var settings = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (key, value) in body.Settings ?? new Dictionary<string, string>()) {
      if (secretKeys.Contains(key)) return (null, null, Error(400, "invalid_request", $"'{NotificationMessageFormatter.SafeKey(key)}' is a secret. Send it in secrets."));
      settings[key] = value ?? string.Empty;
    }

    foreach (var (key, value) in body.Secrets ?? new Dictionary<string, string>()) {
      if (!secretKeys.Contains(key)) return (null, null, Error(400, "invalid_request", $"Unknown secret '{NotificationMessageFormatter.SafeKey(key)}' for type {type}."));
      if (!string.IsNullOrWhiteSpace(value)) settings[key] = value.Trim();
    }

    // An absent or empty secret keeps the stored secret.
    if (existing is not null) {
      foreach (var key in secretKeys) {
        if (!settings.ContainsKey(key) && existing.Settings.TryGetValue(key, out var stored)) settings[key] = stored;
      }
    }

    var target = new NotificationTarget {
      Type = type,
      Name = name,
      Enabled = body.Enabled ?? existing?.Enabled ?? true
    };
    foreach (var (key, value) in settings) target.Settings[key] = value;
    var validation = channel.Validate(target);
    return validation is null
      ? (target, channel, null)
      : (null, null, Error(400, "invalid_request", validation));
  }

  private static NotificationTargetView View(NotificationTarget target, Dictionary<string, INotificationChannel> byType) {
    var view = new NotificationTargetView {
      Id = target.Id,
      Type = target.Type,
      Name = target.Name,
      Enabled = target.Enabled,
      CreatedAt = target.CreatedAt,
      UpdatedAt = target.UpdatedAt
    };
    // A target of an unknown type shows no settings, so an unknown secret cannot leak.
    if (!byType.TryGetValue(target.Type, out var channel)) return view;

    foreach (var field in channel.Fields) {
      if (!target.Settings.TryGetValue(field.Key, out var value)) continue;
      if (!field.Secret) {
        view.Settings[field.Key] = value;
      }
      else if (!string.IsNullOrEmpty(value)) {
        view.HasSecret = true;
        view.SecretHint = Hint(value);
      }
    }

    return view;
  }

  private static string Hint(string secret) {
    var tail = secret.Length <= 4 ? string.Empty : secret[^4..];
    return "••••" + tail;
  }

  private static IResult NotFound() => Error(404, "not_found", "Notification target not found");

  private static IResult Error(int status, string code, string message) =>
    Results.Json(new { error = new { code, message, hint = (string?)null } }, statusCode: status);
}
