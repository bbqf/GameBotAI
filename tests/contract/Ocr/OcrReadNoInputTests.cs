#pragma warning disable CA2007, CA1416, CA2000
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace GameBot.ContractTests.Ocr;

/// <summary>Feature 128: a read request sends no input (tap, swipe, or key) to the emulator.</summary>
public sealed class OcrReadNoInputTests {
  [Fact]
  public async Task SerialReadSendsNoInput() {
    using var host = new OcrReadTestHost();
    host.Sessions.Add("emu-ocr-2");
    host.Ocr.Text = "text";

    var resp = await host.PostAsync(new { serial = "emu-ocr-2", region = OcrReadTestHost.Region() });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    host.Sessions.DispatchCalls.Should().Be(0);
    host.Frames.Calls.Should().Be(1);
  }

  [Fact]
  public async Task CaptureIdReadTakesNoNewCaptureAndSendsNoInput() {
    using var host = new OcrReadTestHost();
    host.Sessions.Add("emu-ocr-2");
    host.Ocr.Text = "text";
    var capture = host.Captures.Add(OcrReadTestHost.Png(300, 200));

    var resp = await host.PostAsync(new { captureId = capture.Id, region = OcrReadTestHost.Region() });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    host.Frames.Calls.Should().Be(0);
    host.Sessions.DispatchCalls.Should().Be(0);
  }
}
