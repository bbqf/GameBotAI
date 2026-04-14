using System;
using System.Net;
using System.Net.Http;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class EmulatorImageEndpointsCaptureTests : IDisposable
{
    private readonly string? _prevUseAdb;
    private readonly string? _prevDynamicPort;
    private readonly string? _prevAuthToken;
    private readonly string? _prevDataDir;

    public EmulatorImageEndpointsCaptureTests()
    {
        _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
        _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
        _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        TestEnvironment.PrepareCleanDataDir();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
        Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ScreenshotEndpointReturns503WhenNoCachedFrame()
    {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var response = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void ScreenSourceResolvesToExpectedTypeWhenAdbDisabled()
    {
        // When ADB is disabled, IScreenSource should still be registered (as stub)
        using var app = new WebApplicationFactory<Program>();
        using var scope = app.Services.CreateScope();
        var screenSource = scope.ServiceProvider.GetService<IScreenSource>();
        screenSource.Should().NotBeNull();
        // When ADB is off, it should NOT be BackgroundCaptureScreenSource
        screenSource.Should().NotBeOfType<BackgroundCaptureScreenSource>();
    }
}
