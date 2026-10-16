using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable CA2007, CA1861, CA1707, CA1002, CA1819, CA2227, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>
/// A service host with its own temporary data folder for the gamepack contract tests (feature 143). The data folder is set
/// through environment variables, as in the image contract tests, so the tests do not touch the shared data folder.
/// </summary>
internal sealed class GamepackContractHost : IDisposable {
  private readonly WebApplicationFactory<Program> _factory;

  public GamepackContractHost(bool withThrowingGate = false, Action<IServiceCollection, string>? configure = null) {
    Root = Path.Combine(Path.GetTempPath(), "GameBotContract", "gp" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Root);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", Root);
    Environment.SetEnvironmentVariable("Service__Storage__Root", Root);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Gate = new ThrowingWriteGate();
    _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
      if (configure is not null) {
        builder.ConfigureServices(services => configure(services, Root));
      }

      if (withThrowingGate) {
        builder.ConfigureServices(services => {
          services.RemoveAll<IGamepackWriteGate>();
          services.AddSingleton<IGamepackWriteGate>(Gate);
        });
      }
    });
    Client = _factory.CreateClient();
    Client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
  }

  public string Root { get; }
  public ThrowingWriteGate Gate { get; }
  public HttpClient Client { get; }

  public IServiceProvider Services => _factory.Services;

  public void Dispose() {
    Client.Dispose();
    _factory.Dispose();
  }

  public Task<HttpResponseMessage> InstallAsync(string json, bool dryRun = false) =>
    Client.PostAsync(new Uri("/api/gamepacks/install" + (dryRun ? "?dryRun=true" : string.Empty), UriKind.Relative),
      new StringContent(json, Encoding.UTF8, "application/json"));

  /// <summary>Sends a multipart install request with the gamepack part and an optional signature part (feature 146).</summary>
  public Task<HttpResponseMessage> InstallSignedAsync(string json, string? signature, bool dryRun = false, HttpContent? signaturePart = null) {
    var content = new MultipartFormDataContent();
    content.Add(new StringContent(json, Encoding.UTF8, "application/json"), "gamepack", "gamepack.json");
    if (signaturePart is not null) {
      content.Add(signaturePart, "signature", "signature.json");
    }
    else if (signature is not null) {
      content.Add(new StringContent(signature, Encoding.UTF8, "application/json"), "signature", "signature.json");
    }

    return Client.PostAsync(new Uri("/api/gamepacks/install" + (dryRun ? "?dryRun=true" : string.Empty), UriKind.Relative), content);
  }

  public async Task SetPolicyAsync(string policy) {
    var response = await Client.PutAsJsonAsync(new Uri("/api/gamepacks/signature/policy", UriKind.Relative), new { policy });
    response.EnsureSuccessStatusCode();
  }

  public async Task AddKeyAsync(string label, string publicKey) {
    var response = await Client.PostAsJsonAsync(new Uri("/api/gamepacks/signature/keys", UriKind.Relative), new { label, publicKey });
    response.EnsureSuccessStatusCode();
  }

  /// <summary>Gives a client with no token.</summary>
  public HttpClient CreateAnonymousClient() => _factory.CreateClient();

  /// <summary>A text snapshot of the instance state: the lists of gamepacks, sequences, and commands and the settings file.</summary>
  public async Task<string> StateSnapshotAsync() {
    var parts = new List<string>();
    foreach (var path in new[] { "/api/gamepacks", "/api/sequences", "/api/commands" }) {
      parts.Add((await GetJsonAsync(path)).GetRawText());
    }

    var settings = Path.Combine(Root, "gamepack-signature.json");
    parts.Add(File.Exists(settings) ? await File.ReadAllTextAsync(settings) : "no-settings-file");
    return string.Join("|", parts);
  }

  public Task<HttpResponseMessage> RollbackAsync(string name) =>
    Client.PostAsync(new Uri($"/api/gamepacks/{name}/rollback", UriKind.Relative), content: null);

  public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
    await response.Content.ReadFromJsonAsync<JsonElement>();

  public async Task<JsonElement> GetJsonAsync(string path) {
    var response = await Client.GetAsync(new Uri(path, UriKind.Relative));
    response.EnsureSuccessStatusCode();
    return await ReadAsync(response);
  }

  /// <summary>Finds the stored id of a sequence by name through the list route.</summary>
  public async Task<string> SequenceIdAsync(string name) {
    var list = await GetJsonAsync("/api/sequences");
    foreach (var item in list.EnumerateArray()) {
      if (string.Equals(item.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)) {
        return item.GetProperty("id").GetString()!;
      }
    }

    throw new InvalidOperationException("No sequence named " + name);
  }

  public async Task<string> CommandIdAsync(string name) {
    var list = await GetJsonAsync("/api/commands");
    foreach (var item in list.EnumerateArray()) {
      if (string.Equals(item.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)) {
        return item.GetProperty("id").GetString()!;
      }
    }

    throw new InvalidOperationException("No command named " + name);
  }

  public static IReadOnlyList<string> ProblemCategories(JsonElement body) {
    var result = new List<string>();
    foreach (var problem in body.GetProperty("problems").EnumerateArray()) {
      result.Add(problem.GetProperty("category").GetString()!);
    }

    return result;
  }
}

/// <summary>An HTTP body that reports a large length and sends no data. Used to test the 413 answer without a 100 MB body.</summary>
internal sealed class FakeLengthContent : HttpContent {
  private readonly long _length;

  public FakeLengthContent(long length) {
    _length = length;
    Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
  }

  protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;

  protected override bool TryComputeLength(out long length) {
    length = _length;
    return true;
  }
}
