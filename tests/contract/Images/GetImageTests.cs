using System;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

public sealed class GetImageTests : IDisposable
{
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private readonly string? _prevDynPort;
  private readonly string? _prevToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDataDir;
  private readonly string? _prevStorageRoot;
  private readonly string _dataDir;

  public GetImageTests()
  {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevStorageRoot = Environment.GetEnvironmentVariable("Service__Storage__Root");

    _dataDir = Path.Combine(Path.GetTempPath(), $"gamebot-contract-getimg-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_dataDir);

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _prevStorageRoot);

    try
    {
      if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
    catch { }

    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task GetReturnsSavedImageWithContentType()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var upload = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "get-me", data = OneByOnePngBase64 });
    upload.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.GetAsync(new Uri("/api/images/get-me", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Content.Headers.ContentType.Should().Be(new MediaTypeHeaderValue("image/png"));
    var bytes = await resp.Content.ReadAsByteArrayAsync();
    bytes.Should().NotBeNull();
    Convert.ToBase64String(bytes).Should().Be(OneByOnePngBase64);
  }
}
