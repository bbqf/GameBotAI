#pragma warning disable CA2007, CA1416, CA2000
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameBot.ContractTests.Sessions;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameBot.ContractTests.Ocr;

/// <summary>Feature 128: a test host for <c>POST /api/ocr/read</c> with a fake frame source and a fake OCR engine.</summary>
internal sealed class OcrReadTestHost : IDisposable {
  internal sealed class FakeFrames : ISessionFrameSource {
    public int Width { get; set; } = 540;
    public int Height { get; set; } = 960;
    public bool ReturnNull { get; set; }
    public int Calls { get; private set; }
    public Bitmap? Capture(string sessionId) {
      Calls++;
      return ReturnNull ? null : new Bitmap(Width, Height);
    }
  }

  internal sealed class FakeOcr : ITextOcr {
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; } = 0.91;
    public bool Throws { get; set; }
    public OcrResult Recognize(Bitmap image) =>
      Throws ? throw new InvalidOperationException("boom") : new OcrResult(Text, Confidence);
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly WebApplicationFactory<Program> _base;
  private readonly WebApplicationFactory<Program> _app;

  public LivenessFakeSessionManager Sessions { get; } = new();
  public FakeFrames Frames { get; } = new();
  public FakeOcr Ocr { get; } = new();
  public HttpClient Client { get; }
  public CaptureSessionStore Captures => _app.Services.GetRequiredService<CaptureSessionStore>();

  public OcrReadTestHost(bool withFrameSource = true, bool withEngine = true) {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");

    _base = new WebApplicationFactory<Program>();
    _app = _base.WithWebHostBuilder(b => b.ConfigureTestServices(services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(Sessions);
      // Other services need an ITextOcr, so it stays registered. An absent engine is built into the read service.
      services.RemoveAll<ITextOcr>();
      services.AddSingleton<ITextOcr>(Ocr);
      if (withFrameSource) {
        services.RemoveAll<ISessionFrameSource>();
        services.AddSingleton<ISessionFrameSource>(Frames);
      }
      if (!withEngine) {
        services.RemoveAll<OcrReadService>();
        services.AddSingleton(sp => new OcrReadService(
          sp.GetRequiredService<CaptureSessionStore>(),
          sp.GetRequiredService<ISessionManager>(),
          sp.GetService<ISessionFrameSource>(),
          null));
      }
    }));
    Client = _app.CreateClient();
    Client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
  }

  public static byte[] Png(int width, int height) {
    using var bmp = new Bitmap(width, height);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  public Task<HttpResponseMessage> PostAsync(object body) =>
    Client.PostAsJsonAsync(new Uri("/api/ocr/read", UriKind.Relative), body);

  public Task<HttpResponseMessage> PostRawAsync(string raw) =>
    Client.PostAsync(new Uri("/api/ocr/read", UriKind.Relative), new StringContent(raw, Encoding.UTF8, "application/json"));

  public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp) {
    var text = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(text);
    return doc.RootElement.Clone();
  }

  public static object Region(int x = 10, int y = 10, int width = 100, int height = 50) => new { x, y, width, height };

  public void Dispose() {
    Client.Dispose();
    _app.Dispose();
    _base.Dispose();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
  }
}
