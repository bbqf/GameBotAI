using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.Notifications {
  /// <summary>
  /// Delivers failure notifications over HTTP (feature 087, issue #181).
  /// <para>
  /// Every public path is wrapped so that <b>nothing escapes as an exception</b> — see
  /// <see cref="IFailureNotifier"/> for why that contract is load-bearing. Faults are returned as a
  /// <see cref="FailureNotificationResult"/> and recorded in the application log.
  /// </para>
  /// <para>
  /// The receiver is untrusted: only its status code is read. Its response body is never parsed,
  /// never logged, and never allowed to influence queue behaviour.
  /// </para>
  /// </summary>
  internal sealed class HttpFailureNotifier : IFailureNotifier {
    /// <summary>Fixed backoff between attempts. Short, because the run is already failing.</summary>
    internal static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly FailureNotificationOptions _options;
    private readonly ILogger<HttpFailureNotifier> _logger;
    private readonly TimeProvider _timeProvider;

    public HttpFailureNotifier(
      HttpClient http,
      IOptions<FailureNotificationOptions> options,
      ILogger<HttpFailureNotifier> logger,
      TimeProvider? timeProvider = null) {
      _http = http;
      _options = options.Value;
      _logger = logger;
      _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt,
      string? overrideUrl,
      CancellationToken ct = default) {
      try {
        return await DeliverAsync(evt, overrideUrl, ct).ConfigureAwait(false);
      }
      catch (Exception ex) {
        // The outer net. Reaching here means a fault escaped DeliverAsync's own handling, which
        // would be a bug — but a bug in the alerting path must still not reach the run loop.
        return Failed(evt.QueueId, "unknown", 0, ex.GetType().Name);
      }
    }

    private async Task<FailureNotificationResult> DeliverAsync(
      FailureNotificationEvent evt,
      string? overrideUrl,
      CancellationToken ct) {
      var url = string.IsNullOrWhiteSpace(overrideUrl) ? _options.DefaultUrl : overrideUrl;
      if (string.IsNullOrWhiteSpace(url)) {
        _logger.LogNotificationNoDestination(evt.QueueId);
        return new FailureNotificationResult(false, _timeProvider.GetLocalNow(), "no destination configured");
      }

      // Host only — a full URL can carry a token in its query string, and this string reaches logs.
      var host = SafeHost(url!);
      var attempts = _options.EffectiveMaxAttempts;
      string lastError = "unknown";

      for (var attempt = 1; attempt <= attempts; attempt++) {
        if (ct.IsCancellationRequested) {
          lastError = "cancelled";
          break;
        }

        var outcome = await AttemptAsync(evt, url!, ct).ConfigureAwait(false);
        if (outcome is null) {
          _logger.LogNotificationDelivered(evt.QueueId, host, evt.EventType);
          return new FailureNotificationResult(true, _timeProvider.GetLocalNow(), null);
        }

        lastError = outcome;
        if (attempt < attempts) {
          try {
            await Task.Delay(RetryBackoff, ct).ConfigureAwait(false);
          }
          catch (OperationCanceledException) {
            lastError = "cancelled";
            break;
          }
        }
      }

      return Failed(evt.QueueId, host, attempts, lastError);
    }

    /// <summary>
    /// One delivery attempt. Returns null on success, or a short failure reason. Never throws:
    /// every transport fault is a reason string, because a retry decision is not an exceptional
    /// circumstance here.
    /// </summary>
    private async Task<string?> AttemptAsync(FailureNotificationEvent evt, string url, CancellationToken ct) {
      try {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) {
          Content = JsonContent.Create(evt, options: SerializerOptions)
        };
        if (_options.HasAuthHeader) {
          request.Headers.TryAddWithoutValidation(_options.AuthHeaderName!, _options.AuthHeaderValue!);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.EffectiveTimeoutSeconds));

        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return null;
        return $"HTTP {(int)response.StatusCode}";
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
        return $"timed out after {_options.EffectiveTimeoutSeconds}s";
      }
      catch (OperationCanceledException) {
        return "cancelled";
      }
      catch (HttpRequestException ex) {
        return ex.Message;
      }
      catch (UriFormatException) {
        return "destination is not a valid URL";
      }
      catch (InvalidOperationException ex) {
        return ex.Message;
      }
    }

    private FailureNotificationResult Failed(string? queueId, string host, int attempts, string reason) {
      _logger.LogNotificationAbandoned(queueId, host, attempts, reason);
      return new FailureNotificationResult(false, _timeProvider.GetLocalNow(), reason);
    }

    /// <summary>Host of a destination for logging; falls back to a placeholder rather than echoing a bad URL.</summary>
    private static string SafeHost(string url) =>
      Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "invalid-url";
  }
}
