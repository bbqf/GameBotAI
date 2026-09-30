using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Service.Services.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA2000, CA1051, CA1002, CA1819

namespace GameBot.ContractTests.Notifications;

/// <summary>A fake Telegram server. It records each request and answers as the test says.</summary>
internal sealed class FakeTelegramHandler : HttpMessageHandler {
  private readonly ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _answers = new();
  private readonly ConcurrentQueue<(Uri Uri, string Body)> _requests = new();

  public IReadOnlyList<(Uri Uri, string Body)> Requests => _requests.ToList();

  public void Answer(HttpStatusCode status, string body = "{\"ok\":true}") =>
    _answers.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

  public void Hang() => _answers.Enqueue(async (_, ct) => {
    await Task.Delay(Timeout.Infinite, ct);
    return new HttpResponseMessage(HttpStatusCode.OK);
  });

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
    var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
    _requests.Enqueue((request.RequestUri!, body));
    if (_answers.TryDequeue(out var answer)) return await answer(request, cancellationToken);
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
  }
}

/// <summary>A logger provider that keeps every formatted line.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider {
  private readonly ConcurrentQueue<string> _lines = new();

  public IReadOnlyList<string> Lines => _lines.ToList();

  public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

  public void Dispose() { }

  private sealed class Capture : ILogger {
    private readonly string _category;
    private readonly ConcurrentQueue<string> _lines;

    public Capture(string category, ConcurrentQueue<string> lines) { _category = category; _lines = lines; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      _lines.Enqueue($"{_category}: {formatter(state, exception)} {exception}");
    }
  }
}

/// <summary>A dispatcher that records each call (feature 120).</summary>
internal sealed class RecordingDispatcher : INotificationDispatcher {
  private readonly ConcurrentQueue<QueueNotificationJob> _jobs = new();
  private readonly ConcurrentQueue<string> _resets = new();

  public IReadOnlyList<QueueNotificationJob> Jobs => _jobs.ToList();

  public IReadOnlyList<string> Resets => _resets.ToList();

  public void Enqueue(QueueNotificationJob job) => _jobs.Enqueue(job);

  public void ResetStreaks(string queueId) => _resets.Enqueue(queueId);

  private readonly ConcurrentQueue<QueueAlert> _alerts = new();

  public IReadOnlyList<QueueAlert> Alerts => _alerts.ToList();

  public void SendAlert(QueueAlert alert) => _alerts.Enqueue(alert);
}

/// <summary>
/// A test host with its own data folder, a fake Telegram server, captured logs and, when asked, a
/// dispatcher that records calls. The data folder keeps the tests apart from the shared data folder of the
/// other contract tests.
/// </summary>
internal sealed class NotificationContractHost : IDisposable {
  public const string Token = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcd-Xy9z";

  private readonly string? _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
  private readonly string? _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
  private readonly string? _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
  private readonly string? _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
  private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "gamebot-notify-contract-" + Guid.NewGuid().ToString("N"));

  public NotificationContractHost(bool recordDispatcher = false, TimeSpan? attemptTimeout = null) {
    Directory.CreateDirectory(_dataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);
    if (recordDispatcher) Dispatcher = new RecordingDispatcher();
    App = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => {
      s.AddHttpClient(TelegramChannel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Telegram);
      s.Configure<NotificationOptions>(o => {
        o.TelegramRetryPause = TimeSpan.FromMilliseconds(10);
        if (attemptTimeout is { } timeout) o.TelegramAttemptTimeout = timeout;
      });
      s.AddSingleton<ILoggerProvider>(Logs);
      if (Dispatcher is not null) {
        s.RemoveAll<INotificationDispatcher>();
        s.AddSingleton<INotificationDispatcher>(Dispatcher);
      }
    }));
    Client = App.CreateClient();
    Client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
  }

  public WebApplicationFactory<Program> App { get; }

  public HttpClient Client { get; }

  public FakeTelegramHandler Telegram { get; } = new();

  public CapturingLoggerProvider Logs { get; } = new();

  public RecordingDispatcher? Dispatcher { get; }

  public string DataDir => _dataDir;

  public static Uri Rel(string path) => new(path, UriKind.Relative);

  public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
    var text = await response.Content.ReadAsStringAsync();
    return JsonDocument.Parse(text).RootElement.Clone();
  }

  /// <summary>Creates a telegram target through the API and returns its JSON view.</summary>
  public async Task<JsonElement> CreateTargetAsync(string name = "Phone", string chatId = "-1001234567890", string token = Token, bool enabled = true) {
    var response = await Client.PostAsJsonAsync(Rel("/api/notifications/targets"), new {
      type = "telegram", name, enabled,
      settings = new { chatId },
      secrets = new { botToken = token }
    });
    if (response.StatusCode != HttpStatusCode.Created) throw new InvalidOperationException("The target was not created: " + (int)response.StatusCode);
    return await ReadJsonAsync(response);
  }

  public async Task<string> CreateQueueAsync(string name = "Farm-1") {
    var response = await Client.PostAsJsonAsync(Rel("/api/queues"), new { name, emulatorSerial = "emu-1" });
    var json = await ReadJsonAsync(response);
    return json.GetProperty("id").GetString()!;
  }

  public void Dispose() {
    Client.Dispose();
    App.Dispose();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("Service__Storage__Root", null);
    try { Directory.Delete(_dataDir, recursive: true); }
    catch (IOException) { /* best effort */ }
    catch (UnauthorizedAccessException) { /* best effort */ }
  }
}
