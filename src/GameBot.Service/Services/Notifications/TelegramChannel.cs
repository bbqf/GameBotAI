using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameBot.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.Notifications;

/// <summary>
/// Sends a message with the Telegram Bot API (feature 120). The body has <c>chat_id</c> and
/// <c>text</c> and no <c>parse_mode</c>. Each send has 2 attempts. One attempt has a time limit and
/// the pause between attempts is short. A 4xx answer is final. The token is never in a reason, in a
/// log line or in an exception text, and the code never logs the request URI.
/// </summary>
internal sealed partial class TelegramChannel : INotificationChannel {
  public const string TypeName = "telegram";
  public const string BotTokenKey = "botToken";
  public const string ChatIdKey = "chatId";
  public const string HttpClientName = "telegram-notifications";

  private const int MaxAttempts = 2;

  private static readonly Regex TokenShape = new(@"^\d{3,}:[A-Za-z0-9_-]{20,}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly Regex ChatIdShape = new(@"^(-?\d+|@[A-Za-z0-9_]{3,})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly NotificationField[] FieldList = {
    new(BotTokenKey, "Bot token", Secret: true, Required: true),
    new(ChatIdKey, "Chat ID", Secret: false, Required: true)
  };

  private readonly IHttpClientFactory _httpClients;
  private readonly NotificationOptions _options;
  private readonly ILogger<TelegramChannel> _logger;

  public TelegramChannel(IHttpClientFactory httpClients, IOptions<NotificationOptions> options, ILogger<TelegramChannel> logger) {
    _httpClients = httpClients;
    _options = options.Value;
    _logger = logger;
  }

  public string Type => TypeName;

  public string DisplayName => "Telegram";

  public IReadOnlyList<NotificationField> Fields => FieldList;

  public string? Validate(NotificationTarget target) {
    ArgumentNullException.ThrowIfNull(target);
    var allowed = new HashSet<string>(FieldList.Select(f => f.Key), StringComparer.Ordinal);
    foreach (var key in target.Settings.Keys) {
      if (!allowed.Contains(key)) return $"Unknown setting '{NotificationMessageFormatter.SafeKey(key)}' for type telegram.";
    }

    target.Settings.TryGetValue(BotTokenKey, out var token);
    if (string.IsNullOrWhiteSpace(token)) return "The setting 'botToken' is required.";
    if (!TokenShape.IsMatch(token.Trim())) return "The setting 'botToken' has a wrong format. Copy the token from @BotFather.";

    target.Settings.TryGetValue(ChatIdKey, out var chatId);
    if (string.IsNullOrWhiteSpace(chatId)) return "The setting 'chatId' is required.";
    if (!ChatIdShape.IsMatch(chatId.Trim())) return "The setting 'chatId' has a wrong format. Use a whole number or a name that starts with @.";
    return null;
  }

  public async Task<NotificationSendResult> SendAsync(NotificationTarget target, string text, CancellationToken ct) {
    try {
      target.Settings.TryGetValue(BotTokenKey, out var token);
      target.Settings.TryGetValue(ChatIdKey, out var chatId);
      if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId)) {
        return NotificationSendResult.Failed("The target has no bot token or no chat ID.");
      }

      var baseUrl = _options.TelegramBaseUrl.TrimEnd('/');
      var uri = new Uri($"{baseUrl}/bot{token.Trim()}/sendMessage");
      var body = new Dictionary<string, string> { ["chat_id"] = chatId.Trim(), ["text"] = text };

      NotificationSendResult last = NotificationSendResult.Failed("Telegram did not answer.");
      for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
        ct.ThrowIfCancellationRequested();
        var (result, final) = await AttemptAsync(uri, body, token.Trim(), ct).ConfigureAwait(false);
        last = result;
        if (result.Succeeded || final) return result;
        if (attempt < MaxAttempts) {
          await Task.Delay(_options.TelegramRetryPause, ct).ConfigureAwait(false);
        }
      }

      Log.SendFailed(_logger, target.Id, target.Name, last.Reason ?? "unknown");
      return last;
    }
    catch (OperationCanceledException) {
      return NotificationSendResult.Failed("The target did not answer in time.");
    }
    catch (Exception ex) {
      // Never use ex.Message: some messages hold the request URL and so the token.
      return NotificationSendResult.Failed($"Telegram did not answer: {ex.GetType().Name}");
    }
  }

  private async Task<(NotificationSendResult Result, bool Final)> AttemptAsync(Uri uri, Dictionary<string, string> body, string token, CancellationToken ct) {
    using var attemptLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
    attemptLimit.CancelAfter(_options.TelegramAttemptTimeout);
    try {
      var client = _httpClients.CreateClient(HttpClientName);
      using var response = await client.PostAsJsonAsync(uri, body, attemptLimit.Token).ConfigureAwait(false);
      if (response.IsSuccessStatusCode) return (NotificationSendResult.Ok(), true);

      var status = (int)response.StatusCode;
      var description = await ReadDescriptionAsync(response, token, attemptLimit.Token).ConfigureAwait(false);
      var reason = string.IsNullOrEmpty(description)
        ? $"Telegram answered {status}."
        : $"Telegram answered {status}: {description}";
      // A 4xx answer is final. A 5xx answer can work on the second attempt.
      return (NotificationSendResult.Failed(reason), status is >= 400 and < 500);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
      return (NotificationSendResult.Failed("Telegram did not answer in time."), false);
    }
    catch (HttpRequestException ex) {
      return (NotificationSendResult.Failed($"Telegram did not answer: {ex.GetType().Name}"), false);
    }
  }

  private static async Task<string> ReadDescriptionAsync(HttpResponseMessage response, string token, CancellationToken ct) {
    try {
      var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      using var doc = JsonDocument.Parse(text);
      if (doc.RootElement.ValueKind == JsonValueKind.Object
          && doc.RootElement.TryGetProperty("description", out var description)
          && description.ValueKind == JsonValueKind.String) {
        var value = description.GetString() ?? string.Empty;
        if (value.Length > 200) value = value[..200];
        return value.Replace(token, "***", StringComparison.Ordinal);
      }
    }
    catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException) {
      // No readable description: the reason has the status only.
    }

    return string.Empty;
  }

  private static partial class Log {
    [LoggerMessage(EventId = 12010, Level = LogLevel.Warning, Message = "Telegram send failed for target {TargetId} ({TargetName}): {Reason}")]
    public static partial void SendFailed(ILogger logger, string targetId, string targetName, string reason);
  }
}
