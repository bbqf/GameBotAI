using System.Net;
using System.Text;
using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.UnitTests.Updates;

/// <summary>A message handler that answers with a function. It records each request URL.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler {
  private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

  public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) {
    _respond = respond;
  }

  public List<Uri> Requests { get; } = new();

  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
    Requests.Add(request.RequestUri!);
    return Task.FromResult(_respond(request));
  }

  public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
    new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

  public static HttpResponseMessage Redirect(string location) {
    var response = new HttpResponseMessage(HttpStatusCode.Found);
    response.Headers.Location = new Uri(location);
    return response;
  }
}

internal sealed class FakeReleaseClient : IReleaseClient {
  public ReleaseInfo? Release { get; set; }
  public UpdateFailureException? Failure { get; set; }
  public int Calls { get; private set; }

  public Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct) {
    Calls++;
    if (Failure is not null) {
      throw Failure;
    }

    return Task.FromResult(Release!);
  }
}

internal sealed class FakeInstalledVersion : IInstalledVersionProvider {
  public FakeInstalledVersion(string version) {
    Version = SemanticVersion.Parse(version);
  }

  public SemanticVersion Version { get; set; }

  public SemanticVersion GetInstalledVersion() => Version;
}

internal sealed class FakeDownloader : IUpdateDownloader {
  public List<string> Steps { get; set; } = new();
  public UpdateFailureException? DownloadFailure { get; set; }
  public UpdateFailureException? VerifyFailure { get; set; }
  public Func<Task>? BeforeDownload { get; set; }

  public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct) {
    Steps.Add("download");
    if (BeforeDownload is not null) {
      await BeforeDownload().ConfigureAwait(false);
    }
    if (DownloadFailure is not null) {
      throw DownloadFailure;
    }

    return "C:\\data\\updates\\GameBot-" + release.Version + ".msi";
  }

  public Task VerifyAsync(string msiPath, ReleaseInfo release, CancellationToken ct) {
    Steps.Add("verify");
    return VerifyFailure is null ? Task.CompletedTask : Task.FromException(VerifyFailure);
  }
}

internal sealed class FakeLauncher : IUpdaterLauncher {
  public List<UpdaterLaunchRequest> Requests { get; } = new();
  public List<string>? Steps { get; set; }
  public UpdateFailureException? Failure { get; set; }

  public void Launch(UpdaterLaunchRequest request) {
    Steps?.Add("launch");
    if (Failure is not null) {
      throw Failure;
    }

    Requests.Add(request);
  }
}

internal sealed class FakeResultStore : IUpdateResultStore {
  public UpdateAttempt? Stored { get; set; }
  public Queue<UpdateAttempt?> ReadQueue { get; } = new();
  public int MarkReportedCalls { get; private set; }
  public int CleanupCalls { get; private set; }

  public Task<UpdateAttempt?> ReadAsync(CancellationToken ct) =>
    Task.FromResult(ReadQueue.Count > 0 ? ReadQueue.Dequeue() : Stored);

  public Task WriteAsync(UpdateAttempt attempt, CancellationToken ct) {
    Stored = attempt;
    return Task.CompletedTask;
  }

  public void MarkReported() {
    MarkReportedCalls++;
    Stored = null;
  }

  public void Cleanup(DateTimeOffset now) {
    CleanupCalls++;
  }
}

internal sealed class FakeQueueStopper : IUpdateQueueStopper {
  public int Calls { get; private set; }
  public List<string>? Steps { get; set; }
  public Exception? Failure { get; set; }

  public Task StopAllAsync(CancellationToken ct) {
    Calls++;
    Steps?.Add("stopQueues");
    return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
  }
}

internal sealed class FakeFreeSpace : IFreeSpaceProvider {
  public long Free { get; set; } = long.MaxValue;

  public long GetFreeBytes(string path) => Free;
}

internal sealed class FakeLifetime : IHostApplicationLifetime, IDisposable {
  private readonly CancellationTokenSource _stopping = new();

  public CancellationToken ApplicationStarted => CancellationToken.None;
  public CancellationToken ApplicationStopping => _stopping.Token;
  public CancellationToken ApplicationStopped => CancellationToken.None;
  public List<string>? Steps { get; set; }
  public int StopCalls { get; private set; }

  public void Dispose() => _stopping.Dispose();

  public void StopApplication() {
    StopCalls++;
    Steps?.Add("stopApplication");
    _stopping.Cancel();
  }
}

internal static class UpdateTestData {
  public const string Sha = "0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a0f3a";

  public static ReleaseInfo Release(string version = "1.7.0.430", long size = 1000, string? notes = "Notes", string sha = Sha) =>
    new(
      SemanticVersion.Parse(version),
      "v" + version,
      new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero),
      notes,
      new Uri("https://github.com/bbqf/GameBotAI/releases/download/v" + version + "/GameBot.msi"),
      size,
      sha);

  public static string ReleaseJson(string tag = "v1.7.0.430", string? manifestUrl = null, string? msiUrl = null, string body = "Release text") {
    manifestUrl ??= "https://github.com/bbqf/GameBotAI/releases/download/" + tag + "/update-manifest.json";
    msiUrl ??= "https://github.com/bbqf/GameBotAI/releases/download/" + tag + "/GameBot.msi";
    return "{\"tag_name\":\"" + tag + "\",\"published_at\":\"2026-10-09T10:00:00Z\",\"body\":\"" + body + "\","
      + "\"assets\":[{\"name\":\"update-manifest.json\",\"browser_download_url\":\"" + manifestUrl + "\"},"
      + "{\"name\":\"GameBot.msi\",\"browser_download_url\":\"" + msiUrl + "\"}]}";
  }

  public static string ManifestJson(string version = "1.7.0.430", long size = 48211968, string sha = Sha, int schema = 1) =>
    "{\"schemaVersion\":" + schema + ",\"version\":\"" + version + "\",\"msi\":{\"fileName\":\"GameBot.msi\",\"sizeBytes\":" + size + ",\"sha256\":\"" + sha + "\"}}";

  public static string NewTempDirectory() {
    var path = Path.Combine(Path.GetTempPath(), "gamebot-update-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }

  public static UpdateCheckService CheckService(FakeReleaseClient client, string installed) =>
    new(client, new FakeInstalledVersion(installed), TimeProvider.System, NullLogger<UpdateCheckService>.Instance);
}
