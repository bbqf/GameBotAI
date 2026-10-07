#pragma warning disable CA2007, CA1416, CA2000
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace GameBot.ContractTests.Ocr;

/// <summary>Feature 128: status codes, answers, and check order of <c>POST /api/ocr/read</c>.</summary>
public sealed class OcrReadContractTests {
  private static OcrReadTestHost HostWithSession(string serial = "emu-ocr-1", bool withFrameSource = true, bool withEngine = true) {
    var host = new OcrReadTestHost(withFrameSource, withEngine);
    host.Sessions.Add(serial);
    return host;
  }

  // ---- US1: serial source ----

  [Fact]
  public async Task SerialReadGivesTextAndConfidence() {
    using var host = HostWithSession();
    host.Ocr.Text = "Refresh time: 02:10:35";

    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region() });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await OcrReadTestHost.ReadJsonAsync(resp);
    json.GetProperty("text").GetString().Should().Be("Refresh time: 02:10:35");
    json.GetProperty("confidence").GetDouble().Should().Be(0.91);
    json.GetProperty("source").GetString().Should().Be("serial");
    json.GetProperty("frameWidth").GetInt32().Should().Be(540);
    json.GetProperty("frameHeight").GetInt32().Should().Be(960);
  }

  [Fact]
  public async Task EmptyRegionTextGivesEmptyTextWithNoError() {
    using var host = HostWithSession();
    host.Ocr.Text = string.Empty;

    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region() });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    (await OcrReadTestHost.ReadJsonAsync(resp)).GetProperty("text").GetString().Should().BeEmpty();
  }

  [Fact]
  public async Task RegionThatHasTheFrameSizeIsValid() {
    using var host = HostWithSession();

    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(0, 0, 540, 960) });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  // ---- US2: capture-id source ----

  [Fact]
  public async Task TwoReadsOfOneCaptureGiveTheSameText() {
    using var host = new OcrReadTestHost();
    host.Ocr.Text = "same";
    var capture = host.Captures.Add(OcrReadTestHost.Png(300, 200));

    var first = await OcrReadTestHost.ReadJsonAsync(await host.PostAsync(new { captureId = capture.Id, region = OcrReadTestHost.Region() }));
    var second = await OcrReadTestHost.ReadJsonAsync(await host.PostAsync(new { captureId = capture.Id, region = OcrReadTestHost.Region() }));

    first.GetProperty("text").GetString().Should().Be("same");
    second.GetProperty("text").GetString().Should().Be(first.GetProperty("text").GetString());
    first.GetProperty("source").GetString().Should().Be("captureId");
    first.GetProperty("frameWidth").GetInt32().Should().Be(300);
  }

  [Fact]
  public async Task UnknownCaptureIdGives404() {
    using var host = new OcrReadTestHost();

    var resp = await host.PostAsync(new { captureId = "nope", region = OcrReadTestHost.Region() });

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await OcrReadTestHost.ReadJsonAsync(resp)).GetProperty("code").GetString().Should().Be("capture_not_found");
  }

  // ---- US3: parser ----

  [Fact]
  public async Task ParseSuccessGivesTheParsedValue() {
    using var host = HostWithSession();
    host.Ocr.Text = "Refresh time: 02:10:35";

    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(), parser = "hh:mm:ss" });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await OcrReadTestHost.ReadJsonAsync(resp);
    json.GetProperty("parser").GetString().Should().Be("hh:mm:ss");
    json.GetProperty("parsed").GetProperty("value").GetString().Should().Be("02:10:35");
    json.GetProperty("parsed").GetProperty("totalSeconds").GetDouble().Should().Be(7835);
  }

  [Fact]
  public async Task ParseFailureIs200WithRawTextAndReason() {
    using var host = HostWithSession();
    host.Ocr.Text = "8 ODeIOs35";

    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(), parser = "hh:mm:ss" });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await OcrReadTestHost.ReadJsonAsync(resp);
    json.GetProperty("text").GetString().Should().Be("8 ODeIOs35");
    json.GetProperty("parsed").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    json.GetProperty("parseFailureReason").GetString().Should().Be("text did not parse as hh:mm:ss");
  }

  // ---- US4: errors ----

  private static async Task AssertError(System.Net.Http.HttpResponseMessage resp, HttpStatusCode status, string code) {
    resp.StatusCode.Should().Be(status);
    var json = await OcrReadTestHost.ReadJsonAsync(resp);
    json.GetProperty("code").GetString().Should().Be(code);
    json.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task NoSourceGives400() {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { region = OcrReadTestHost.Region() }), HttpStatusCode.BadRequest, "invalid_request");
  }

  [Fact]
  public async Task TwoSourcesGive400() {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", captureId = "x", region = OcrReadTestHost.Region() }), HttpStatusCode.BadRequest, "invalid_request");
  }

  [Fact]
  public async Task UnknownParserGives400AndNamesTheSupportedParsers() {
    using var host = HostWithSession();
    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(), parser = "nope" });
    await AssertError(resp, HttpStatusCode.BadRequest, "unknown_parser");
    (await OcrReadTestHost.ReadJsonAsync(resp)).GetProperty("message").GetString().Should().Contain("hh:mm:ss");
  }

  [Theory]
  [InlineData(0, 10)]
  [InlineData(10, 0)]
  [InlineData(-5, 10)]
  public async Task RegionSizeNotAboveZeroGives400(int width, int height) {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(width: width, height: height) }), HttpStatusCode.BadRequest, "invalid_region");
  }

  [Theory]
  [InlineData("")]
  [InlineData("{ not json")]
  [InlineData("[]")]
  public async Task MissingOrBadBodyGives400(string raw) {
    using var host = HostWithSession();
    await AssertError(await host.PostRawAsync(raw), HttpStatusCode.BadRequest, "invalid_request");
  }

  [Fact]
  public async Task RegionOutsideTheFrameGives400AndNamesTheFrameSize() {
    using var host = HostWithSession();
    var resp = await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(441, 0, 100, 50) });
    await AssertError(resp, HttpStatusCode.BadRequest, "invalid_region");
    (await OcrReadTestHost.ReadJsonAsync(resp)).GetProperty("message").GetString().Should().Contain("540 x 960");
  }

  [Fact]
  public async Task MissingRegionObjectGives400() {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1" }), HttpStatusCode.BadRequest, "invalid_request");
  }

  [Fact]
  public async Task UnknownSerialGives404() {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { serial = "other", region = OcrReadTestHost.Region() }), HttpStatusCode.NotFound, "serial_not_found");
  }

  [Fact]
  public async Task NoLookupFaultBeatsUnknownSerial() {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { serial = "other", region = OcrReadTestHost.Region(width: 0) }), HttpStatusCode.BadRequest, "invalid_region");
  }

  [Fact]
  public async Task UnknownSerialBeatsRegionOutsideTheFrame() {
    using var host = HostWithSession();
    await AssertError(await host.PostAsync(new { serial = "other", region = OcrReadTestHost.Region(10000, 0, 10, 10) }), HttpStatusCode.NotFound, "serial_not_found");
  }

  [Fact]
  public async Task MissingEngineBeatsRegionOutsideTheFrame() {
    using var host = HostWithSession(withEngine: false);
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region(10000, 0, 10, 10) }), HttpStatusCode.ServiceUnavailable, "ocr_unavailable");
  }

  [Fact]
  public async Task FailedCaptureGives502() {
    using var host = HostWithSession();
    host.Frames.ReturnNull = true;
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region() }), HttpStatusCode.BadGateway, "capture_failed");
  }

  [Fact]
  public async Task MissingCaptureServiceGives503() {
    using var host = HostWithSession(withFrameSource: false);
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region() }), HttpStatusCode.ServiceUnavailable, "capture_unavailable");
  }

  [Fact]
  public async Task MissingEngineGives503() {
    using var host = HostWithSession(withEngine: false);
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region() }), HttpStatusCode.ServiceUnavailable, "ocr_unavailable");
  }

  [Fact]
  public async Task EngineThatThrowsGives503NotFiveHundred() {
    using var host = HostWithSession();
    host.Ocr.Throws = true;
    await AssertError(await host.PostAsync(new { serial = "emu-ocr-1", region = OcrReadTestHost.Region() }), HttpStatusCode.ServiceUnavailable, "ocr_unavailable");
  }
}
