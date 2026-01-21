using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Json;
using System.Linq;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameBot.IntegrationTests;

public class EmulatorImageCropTests
{
    public EmulatorImageCropTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        TestEnvironment.PrepareCleanDataDir();
    }

    [Fact]
    public async Task CaptureAndCropFlowSucceeds()
    {
        var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
        using var baseFactory = new WebApplicationFactory<Program>();
        using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_ => new FakeSessionManager());
                services.RemoveAll<GameBot.Domain.Images.ImageStorageOptions>();
                services.AddSingleton(new GameBot.Domain.Images.ImageStorageOptions(Path.Combine(dataRoot, "images")));
            });
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var screenshotResp = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative)).ConfigureAwait(true);
        screenshotResp.StatusCode.Should().Be(HttpStatusCode.OK);
        screenshotResp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        screenshotResp.Headers.TryGetValues("X-Capture-Id", out var captureIds).Should().BeTrue();
        var captureId = captureIds!.First();

        var cropResp = await client.PostAsJsonAsync(new Uri("/api/images/crop", UriKind.Relative), new
        {
            name = "crop-test",
            overwrite = true,
            bounds = new { x = 0, y = 0, width = 16, height = 16 },
            sourceCaptureId = captureId
        }).ConfigureAwait(true);

        cropResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await cropResp.Content.ReadFromJsonAsync<Dictionary<string, object?>>().ConfigureAwait(true);
        payload.Should().NotBeNull();
        var storagePath = payload!["storagePath"]!.ToString();
        File.Exists(storagePath!).Should().BeTrue();

        using var saved = new Bitmap(storagePath!);
        saved.Width.Should().Be(16);
        saved.Height.Should().Be(16);
    }

    [Fact]
    public async Task CropFailsWhenBoundsOutsideCapture()
    {
        var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
        using var baseFactory = new WebApplicationFactory<Program>();
        using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_ => new FakeSessionManager());
                services.RemoveAll<GameBot.Domain.Images.ImageStorageOptions>();
                services.AddSingleton(new GameBot.Domain.Images.ImageStorageOptions(Path.Combine(dataRoot, "images")));
            });
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var screenshotResp = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative)).ConfigureAwait(true);
        screenshotResp.Headers.TryGetValues("X-Capture-Id", out var captureIds).Should().BeTrue();
        var captureId = captureIds!.First();

        var cropResp = await client.PostAsJsonAsync(new Uri("/api/images/crop", UriKind.Relative), new
        {
            name = "out-of-range",
            overwrite = true,
            bounds = new { x = 48, y = 48, width = 20, height = 20 },
            sourceCaptureId = captureId
        }).ConfigureAwait(true);

        cropResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await cropResp.Content.ReadFromJsonAsync<Dictionary<string, object?>>()!.ConfigureAwait(true);
        payload.Should().NotBeNull();
        payload!["error"].Should().Be("bounds_out_of_range");
        var sizeElement = (JsonElement)payload["captureSize"]!;
        sizeElement.GetProperty("width").GetInt32().Should().Be(64);
        sizeElement.GetProperty("height").GetInt32().Should().Be(64);
    }

    [Fact]
    public async Task CropFailsWhenCaptureMissing()
    {
        var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
        using var baseFactory = new WebApplicationFactory<Program>();
        using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_ => new FakeSessionManager());
                services.RemoveAll<GameBot.Domain.Images.ImageStorageOptions>();
                services.AddSingleton(new GameBot.Domain.Images.ImageStorageOptions(Path.Combine(dataRoot, "images")));
            });
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var cropResp = await client.PostAsJsonAsync(new Uri("/api/images/crop", UriKind.Relative), new
        {
            name = "missing",
            overwrite = true,
            bounds = new { x = 0, y = 0, width = 16, height = 16 },
            sourceCaptureId = "missing-id"
        }).ConfigureAwait(true);

        cropResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await cropResp.Content.ReadFromJsonAsync<Dictionary<string, object?>>()!.ConfigureAwait(true);
        payload.Should().NotBeNull();
        payload!["error"].Should().Be("capture_missing");
        payload!["hint"].Should().NotBeNull();
    }

    [Fact]
    public async Task CaptureFailsWhenNoEmulatorSession()
    {
        var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
        using var baseFactory = new WebApplicationFactory<Program>();
        using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_ => new NoSessionManager());
                services.RemoveAll<GameBot.Domain.Images.ImageStorageOptions>();
                services.AddSingleton(new GameBot.Domain.Images.ImageStorageOptions(Path.Combine(dataRoot, "images")));
            });
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var screenshotResp = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative)).ConfigureAwait(true);
        screenshotResp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await screenshotResp.Content.ReadFromJsonAsync<Dictionary<string, string>>()!.ConfigureAwait(true);
        payload!["error"].Should().Be("emulator_unavailable");
        payload!["hint"].Should().NotBeNull();
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly EmulatorSession _session;
        private readonly byte[] _png;

        public FakeSessionManager()
        {
            _session = new EmulatorSession
            {
                Id = "sess-test",
                GameId = "test-game",
                Status = SessionStatus.Running,
                Health = SessionHealth.Ok,
                LastActivity = DateTimeOffset.UtcNow
            };

            using var bmp = new Bitmap(64, 64, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.DarkSlateGray);
            g.FillRectangle(Brushes.Blue, new Rectangle(0, 0, 32, 32));
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            _png = ms.ToArray();
        }

        public int ActiveCount => 1;

        public bool CanCreateSession => true;

        public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => _session;

        public EmulatorSession? GetSession(string id) => _session.Id == id ? _session : null;

        public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };

        public bool StopSession(string id) => false;

        public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);

        public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default)
        {
            if (id != _session.Id) throw new KeyNotFoundException("Session not found");
            return Task.FromResult(_png);
        }
    }

    private sealed class NoSessionManager : ISessionManager
    {
        public int ActiveCount => 0;
        public bool CanCreateSession => false;
        public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new InvalidOperationException("No sessions available");
        public EmulatorSession? GetSession(string id) => null;
        public IReadOnlyCollection<EmulatorSession> ListSessions() => Array.Empty<EmulatorSession>();
        public bool StopSession(string id) => false;
        public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
        public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => throw new InvalidOperationException("No sessions available");
    }
}
