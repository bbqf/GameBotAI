using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public sealed class ConfigEndpointTests : IDisposable
{
    private readonly string? _prevUseAdb;
    private readonly string? _prevAuthToken;
    private readonly string? _prevDataDir;
    public ConfigEndpointTests()
    {
        _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
        _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        TestEnvironment.PrepareCleanDataDir();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
        Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
        GC.SuppressFinalize(this);
    }

    private static HttpClient CreateAuthedClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    [Fact]
    public async Task StartupGeneratesSnapshotFileAndEndpointWorks()
    {
        using var app = new WebApplicationFactory<Program>();
        using var client = CreateAuthedClient(app);

        var resp = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
        doc.Should().NotBeNull();
        var root = doc!.RootElement;
        root.TryGetProperty("parameters", out var parameters).Should().BeTrue();
        parameters.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task RequiresAuthWhenTokenConfigured()
    {
        using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        var resp = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task MasksSecretValuesFromEnvironment()
    {
        var prev = Environment.GetEnvironmentVariable("GAMEBOT_MY_SECRET_TOKEN");
        Environment.SetEnvironmentVariable("GAMEBOT_MY_SECRET_TOKEN", "supersecret");
        try
        {
            using var app = new WebApplicationFactory<Program>();
            using var client = CreateAuthedClient(app);

            var resp = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
            using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
            var parameters = doc!.RootElement.GetProperty("parameters");
            parameters.TryGetProperty("GAMEBOT_MY_SECRET_TOKEN", out var tokenParam).Should().BeTrue();
            tokenParam.GetProperty("isSecret").GetBoolean().Should().BeTrue();
            tokenParam.GetProperty("value").GetString().Should().Be("***");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAMEBOT_MY_SECRET_TOKEN", prev);
        }
    }

    [Fact]
    public async Task SavedConfigIsLoadedButEnvOverrides()
    {
        // Prepare a saved config file before service starts
        var dataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
        var cfgDir = Path.Combine(dataDir, "config");
        Directory.CreateDirectory(cfgDir);
        var cfgFile = Path.Combine(cfgDir, "config.json");
        var json = "{\n  \"parameters\": {\n    \"GAMEBOT_FOO\": { \"value\": \"file\" }\n  }\n}";
        await File.WriteAllTextAsync(cfgFile, json).ConfigureAwait(true);
        // Env overrides
        var prevFoo = Environment.GetEnvironmentVariable("GAMEBOT_FOO");
        Environment.SetEnvironmentVariable("GAMEBOT_FOO", "env");

        try
        {
            using var app = new WebApplicationFactory<Program>();
            using var client = CreateAuthedClient(app);
            var resp = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
            using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
            var parameters = doc!.RootElement.GetProperty("parameters");
            parameters.TryGetProperty("GAMEBOT_FOO", out var foo).Should().BeTrue();
            foo.GetProperty("source").GetString().Should().Be("Environment");
            foo.GetProperty("value").GetString().Should().Be("env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAMEBOT_FOO", prevFoo);
        }
    }

    [Fact]
    public async Task RefreshReflectsUpdatedEnvironment()
    {
        var prev = Environment.GetEnvironmentVariable("GAMEBOT_MY_VALUE");
        Environment.SetEnvironmentVariable("GAMEBOT_MY_VALUE", "A");
        using var app = new WebApplicationFactory<Program>();
        using var client = CreateAuthedClient(app);

        var resp1 = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK, await resp1.Content.ReadAsStringAsync().ConfigureAwait(true));

        Environment.SetEnvironmentVariable("GAMEBOT_MY_VALUE", "B");
        var refresh = await client.PostAsync(new Uri("/config/refresh", UriKind.Relative), content: null).ConfigureAwait(true);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK, await refresh.Content.ReadAsStringAsync().ConfigureAwait(true));
        using var doc = await refresh.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
        var parameters = doc!.RootElement.GetProperty("parameters");
        parameters.TryGetProperty("GAMEBOT_MY_VALUE", out var myVar).Should().BeTrue();
        myVar.GetProperty("value").GetString().Should().Be("B");
        Environment.SetEnvironmentVariable("GAMEBOT_MY_VALUE", prev);
    }

    [Fact]
    public async Task RefreshReadsModifiedSavedConfigAndPersistsIt()
    {
        // Use a fresh, isolated data directory and ensure no env override
        var dataDir = TestEnvironment.PrepareCleanDataDir();
        var prevLang = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_LANG");
        Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_LANG", null);

        using var app = new WebApplicationFactory<Program>();
        using var client = CreateAuthedClient(app);

        // Bootstrap initial snapshot and file
        var first = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync().ConfigureAwait(true));

        // Modify saved config on disk to a new value
        var cfgDir = Path.Combine(dataDir, "config");
        var cfgFile = Path.Combine(cfgDir, "config.json");
        Directory.CreateDirectory(cfgDir);
        var newJson = "{\n  \"parameters\": {\n    \"GAMEBOT_TESSERACT_LANG\": { \"value\": \"deu\" }\n  }\n}";
        await File.WriteAllTextAsync(cfgFile, newJson).ConfigureAwait(true);

        // Refresh and verify new value is applied from file
        var refresh = await client.PostAsync(new Uri("/config/refresh", UriKind.Relative), content: null).ConfigureAwait(true);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK, await refresh.Content.ReadAsStringAsync().ConfigureAwait(true));
        using var doc = await refresh.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
        var parameters = doc!.RootElement.GetProperty("parameters");
        parameters.TryGetProperty("GAMEBOT_TESSERACT_LANG", out var lang).Should().BeTrue();
        lang.GetProperty("source").GetString().Should().Be("File");
        lang.GetProperty("value").GetString().Should().Be("deu");

        // Verify the persisted snapshot on disk also contains the updated value
        string savedText = string.Empty;
        JsonDocument? savedDoc = null;
        JsonElement savedValueEl = default;
        for (int i = 0; i < 10; i++)
        {
            savedText = await File.ReadAllTextAsync(cfgFile).ConfigureAwait(true);
            savedDoc?.Dispose();
            savedDoc = JsonDocument.Parse(savedText);
            var savedParams = savedDoc.RootElement.GetProperty("parameters");
            savedParams.TryGetProperty("GAMEBOT_TESSERACT_LANG", out var savedLang).Should().BeTrue();
            savedValueEl = savedLang.TryGetProperty("value", out var savedVal) ? savedVal : savedLang;
            if (savedValueEl.ValueKind == JsonValueKind.String && savedValueEl.GetString() == "deu")
            {
                break;
            }
            await Task.Delay(50).ConfigureAwait(true);
        }
        savedValueEl.GetString().Should().Be("deu");
        Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_LANG", prevLang);
    }
}
