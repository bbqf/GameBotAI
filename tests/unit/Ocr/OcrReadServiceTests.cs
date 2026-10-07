using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Models;
using GameBot.Service.Services;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1416, CA2000

namespace GameBot.UnitTests.Ocr;

/// <summary>Feature 128: check order, both sources, region edge, parse step of the OCR read service.</summary>
public sealed class OcrReadServiceTests {
  private sealed class FakeSessions : ISessionManager {
    private readonly List<EmulatorSession> _sessions = new();
    public EmulatorSession Add(string id, string? serial, SessionStatus status = SessionStatus.Running) {
      var s = new EmulatorSession { Id = id, GameId = "g", DeviceSerial = serial, Status = status };
      _sessions.Add(s);
      return s;
    }
    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
    public EmulatorSession? GetSession(string id) => _sessions.FirstOrDefault(s => s.Id == id);
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.ToList();
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
  }

  private sealed class FakeFrames : ISessionFrameSource {
    private readonly int _w;
    private readonly int _h;
    private readonly bool _null;
    public List<string> Asked { get; } = new();
    public FakeFrames(int w = 540, int h = 960, bool returnNull = false) { _w = w; _h = h; _null = returnNull; }
    public Bitmap? Capture(string sessionId) {
      Asked.Add(sessionId);
      return _null ? null : new Bitmap(_w, _h);
    }
  }

  private sealed class FakeOcr : ITextOcr {
    private readonly string _text;
    private readonly bool _throw;
    public int Calls { get; private set; }
    public FakeOcr(string text, bool @throw = false) { _text = text; _throw = @throw; }
    public OcrResult Recognize(Bitmap image) {
      Calls++;
      return _throw ? throw new InvalidOperationException("boom") : new OcrResult(_text, 0.91);
    }
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  private static OcrReadRequest Req(string? serial = "emu-1", string? captureId = null, int x = 10, int y = 10, int w = 100, int h = 50, string? parser = null) =>
    new() { Serial = serial, CaptureId = captureId, Region = new OcrRegionDto { X = x, Y = y, Width = w, Height = h }, Parser = parser };

  private static OcrReadService Service(FakeSessions? sessions = null, FakeFrames? frames = null, ITextOcr? ocr = null, CaptureSessionStore? captures = null) =>
    new(captures ?? new CaptureSessionStore(), sessions, frames, ocr);

  private static FakeSessions OneSession(string serial = "emu-1") {
    var s = new FakeSessions();
    s.Add("sess-1", serial);
    return s;
  }

  private static byte[] Png(int w, int h) {
    using var bmp = new Bitmap(w, h);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  // ---- serial source ----

  [Fact]
  public void ValidSerialReadGivesTextAndConfidence() {
    var outcome = Service(OneSession(), new FakeFrames(), new FakeOcr("Refresh time")).Read(Req());

    outcome.Status.Should().Be(200);
    outcome.Response!.Text.Should().Be("Refresh time");
    outcome.Response.Confidence.Should().Be(0.91);
    outcome.Response.Source.Should().Be("serial");
    outcome.Response.FrameWidth.Should().Be(540);
    outcome.Response.FrameHeight.Should().Be(960);
    outcome.Response.Parser.Should().BeNull();
    outcome.Response.Parsed.Should().BeNull();
  }

  [Fact]
  public void FirstRunningSessionWithTheSerialWins() {
    var sessions = new FakeSessions();
    sessions.Add("stopped", "emu-1", SessionStatus.Stopped);
    sessions.Add("first", "emu-1");
    sessions.Add("second", "emu-1");
    var frames = new FakeFrames();

    Service(sessions, frames, new FakeOcr("t")).Read(Req()).Status.Should().Be(200);

    frames.Asked.Should().Equal("first");
  }

  [Fact]
  public void RegionThatEndsAtTheFrameEdgeIsValid() {
    var outcome = Service(OneSession(), new FakeFrames(540, 960), new FakeOcr("t")).Read(Req(x: 440, y: 910, w: 100, h: 50));
    outcome.Status.Should().Be(200);
  }

  [Fact]
  public void RegionOnePixelBeyondTheFrameEdgeIsInvalidAndNamesTheFrameSize() {
    var outcome = Service(OneSession(), new FakeFrames(540, 960), new FakeOcr("t")).Read(Req(x: 441, y: 910, w: 100, h: 50));

    outcome.Status.Should().Be(400);
    outcome.Code.Should().Be("invalid_region");
    outcome.Message.Should().Contain("540 x 960");
  }

  [Fact]
  public void NoCaptureServiceGives503CaptureUnavailable() {
    var outcome = Service(OneSession(), null, new FakeOcr("t")).Read(Req());
    outcome.Status.Should().Be(503);
    outcome.Code.Should().Be("capture_unavailable");
  }

  [Fact]
  public void FailedCaptureGives502CaptureFailed() {
    var outcome = Service(OneSession(), new FakeFrames(returnNull: true), new FakeOcr("t")).Read(Req());
    outcome.Status.Should().Be(502);
    outcome.Code.Should().Be("capture_failed");
  }

  [Fact]
  public void MissingEngineGives503OcrUnavailable() {
    var outcome = Service(OneSession(), new FakeFrames(), null).Read(Req());
    outcome.Status.Should().Be(503);
    outcome.Code.Should().Be("ocr_unavailable");
  }

  [Fact]
  public void EngineThatThrowsGives503OcrUnavailable() {
    var outcome = Service(OneSession(), new FakeFrames(), new FakeOcr("t", @throw: true)).Read(Req());
    outcome.Status.Should().Be(503);
    outcome.Code.Should().Be("ocr_unavailable");
  }

  [Fact]
  public void NullCropGives502CaptureFailed() {
    var service = Service(OneSession(), new FakeFrames(), new FakeOcr("t"));
    service.ReaderOverride = (_, _, _) => new OcrRegionReadResult(true, null, 0);

    var outcome = service.Read(Req());

    outcome.Status.Should().Be(502);
    outcome.Code.Should().Be("capture_failed");
  }

  [Fact]
  public void UnknownSerialGives404() {
    var outcome = Service(OneSession("other"), new FakeFrames(), new FakeOcr("t")).Read(Req());
    outcome.Status.Should().Be(404);
    outcome.Code.Should().Be("serial_not_found");
  }

  // ---- capture-id source ----

  [Fact]
  public void StoredCaptureReadTakesNoNewCapture() {
    var captures = new CaptureSessionStore();
    var capture = captures.Add(Png(300, 200));
    var frames = new FakeFrames();

    var outcome = Service(OneSession(), frames, new FakeOcr("stored"), captures).Read(Req(serial: null, captureId: capture.Id));

    outcome.Status.Should().Be(200);
    outcome.Response!.Text.Should().Be("stored");
    outcome.Response.Source.Should().Be("captureId");
    outcome.Response.FrameWidth.Should().Be(300);
    frames.Asked.Should().BeEmpty();
  }

  [Fact]
  public void UnknownCaptureIdGives404() {
    var outcome = Service(OneSession(), new FakeFrames(), new FakeOcr("t")).Read(Req(serial: null, captureId: "nope"));
    outcome.Status.Should().Be(404);
    outcome.Code.Should().Be("capture_not_found");
  }

  [Fact]
  public void StoredPngThatDoesNotDecodeGives502() {
    var captures = new CaptureSessionStore();
    // The store decodes a PNG when it adds one, so put a corrupt entry in the private map directly.
    var map = (System.Collections.Concurrent.ConcurrentDictionary<string, CaptureSession>)typeof(CaptureSessionStore)
      .GetField("_captures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
      .GetValue(captures)!;
    map["bad"] = new CaptureSession("bad", new byte[] { 1, 2, 3, 4 }, 10, 10, DateTimeOffset.UtcNow);

    var outcome = Service(null, null, new FakeOcr("t"), captures).Read(Req(serial: null, captureId: "bad"));

    outcome.Status.Should().Be(502);
    outcome.Code.Should().Be("capture_failed");
  }

  [Fact]
  public void StoredCaptureRegionAtTheEdgeIsValidAndOnePixelBeyondIsNot() {
    var captures = new CaptureSessionStore();
    var capture = captures.Add(Png(300, 200));
    var service = Service(null, null, new FakeOcr("t"), captures);

    service.Read(Req(serial: null, captureId: capture.Id, x: 200, y: 150, w: 100, h: 50)).Status.Should().Be(200);
    var beyond = service.Read(Req(serial: null, captureId: capture.Id, x: 201, y: 150, w: 100, h: 50));
    beyond.Status.Should().Be(400);
    beyond.Code.Should().Be("invalid_region");
    beyond.Message.Should().Contain("300 x 200");
  }

  // ---- parse step ----

  [Fact]
  public void ParseSuccessGivesParsedValue() {
    var outcome = Service(OneSession(), new FakeFrames(), new FakeOcr("Refresh time: 02:10:35")).Read(Req(parser: "HH:MM:SS"));

    outcome.Status.Should().Be(200);
    outcome.Response!.Parser.Should().Be("hh:mm:ss");
    outcome.Response.Parsed!.Value.Should().Be("02:10:35");
    outcome.Response.Parsed.TotalSeconds.Should().Be(7835);
    outcome.Response.ParseFailureReason.Should().BeNull();
  }

  [Fact]
  public void ParseFailureGivesRawTextAndReason() {
    var outcome = Service(OneSession(), new FakeFrames(), new FakeOcr("8 ODeIOs35")).Read(Req(parser: "hh:mm:ss"));

    outcome.Status.Should().Be(200);
    outcome.Response!.Text.Should().Be("8 ODeIOs35");
    outcome.Response.Parsed.Should().BeNull();
    outcome.Response.ParseFailureReason.Should().Be("text did not parse as hh:mm:ss");
  }

  // ---- validation (step 1) ----

  [Fact]
  public void RequestFaultsNeedNoLookup() {
    var service = Service(OneSession(), new FakeFrames(), new FakeOcr("t"));

    service.Read(null).Code.Should().Be("invalid_request");
    service.Read(Req(serial: null)).Code.Should().Be("invalid_request");
    service.Read(Req(serial: "emu-1", captureId: "abc")).Code.Should().Be("invalid_request");
    service.Read(new OcrReadRequest { Serial = "emu-1" }).Code.Should().Be("invalid_request");
    var unknown = service.Read(Req(parser: "nope"));
    unknown.Code.Should().Be("unknown_parser");
    unknown.Message.Should().Contain("hh:mm:ss");
    service.Read(Req(w: 0)).Code.Should().Be("invalid_region");
    service.Read(Req(h: -1)).Code.Should().Be("invalid_region");
  }
}
