using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class ConfigEndpointTests
{
    public ConfigEndpointTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        TestEnvironment.PrepareCleanDataDir();
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
        Environment.SetEnvironmentVariable("MY_SECRET_TOKEN", "supersecret");
        using var app = new WebApplicationFactory<Program>();
        using var client = CreateAuthedClient(app);

        var resp = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
        using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
        var parameters = doc!.RootElement.GetProperty("parameters");
        parameters.TryGetProperty("MY_SECRET_TOKEN", out var tokenParam).Should().BeTrue();
        tokenParam.GetProperty("isSecret").GetBoolean().Should().BeTrue();
        tokenParam.GetProperty("value").GetString().Should().Be("***");
    }

    [Fact]
    public async Task SavedConfigIsLoadedButEnvOverrides()
    {
        // Prepare a saved config file before service starts
        var dataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
        var cfgDir = Path.Combine(dataDir, "config");
        Directory.CreateDirectory(cfgDir);
        var cfgFile = Path.Combine(cfgDir, "config.json");
        var json = "{\n  \"parameters\": {\n    \"FOO\": { \"value\": \"file\" }\n  }\n}";
        await File.WriteAllTextAsync(cfgFile, json).ConfigureAwait(true);
        // Env overrides
        Environment.SetEnvironmentVariable("FOO", "env");

        using var app = new WebApplicationFactory<Program>();
        using var client = CreateAuthedClient(app);
        var resp = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
        using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
        var parameters = doc!.RootElement.GetProperty("parameters");
        parameters.TryGetProperty("FOO", out var foo).Should().BeTrue();
        foo.GetProperty("source").GetString().Should().Be("Environment");
        foo.GetProperty("value").GetString().Should().Be("env");
    }

    [Fact]
    public async Task RefreshReflectsUpdatedEnvironment()
    {
        Environment.SetEnvironmentVariable("MY_VALUE", "A");
        using var app = new WebApplicationFactory<Program>();
        using var client = CreateAuthedClient(app);

        var resp1 = await client.GetAsync(new Uri("/config/", UriKind.Relative)).ConfigureAwait(true);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK, await resp1.Content.ReadAsStringAsync().ConfigureAwait(true));

        Environment.SetEnvironmentVariable("MY_VALUE", "B");
        var refresh = await client.PostAsync(new Uri("/config/refresh", UriKind.Relative), content: null).ConfigureAwait(true);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK, await refresh.Content.ReadAsStringAsync().ConfigureAwait(true));
        using var doc = await refresh.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(true);
        var parameters = doc!.RootElement.GetProperty("parameters");
        parameters.TryGetProperty("MY_VALUE", out var myVar).Should().BeTrue();
        myVar.GetProperty("value").GetString().Should().Be("B");
    }
}
