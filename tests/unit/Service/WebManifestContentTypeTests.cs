using FluentAssertions;
using Microsoft.AspNetCore.StaticFiles;
using Xunit;

namespace GameBot.UnitTests.Service;

/// <summary>
/// The service serves the web UI with the default static file options. These options must return
/// the web app manifest with the content type that phones expect (feature 139, contract C-11).
/// </summary>
public sealed class WebManifestContentTypeTests {
  [Fact]
  public void WebManifestFileHasTheManifestContentType() {
    var provider = new FileExtensionContentTypeProvider();

    var found = provider.TryGetContentType("manifest.webmanifest", out var contentType);

    found.Should().BeTrue();
    contentType.Should().Be("application/manifest+json");
  }

  [Fact]
  public void IconFilesHaveThePngContentType() {
    var provider = new FileExtensionContentTypeProvider();

    var found = provider.TryGetContentType("icons/icon-192.png", out var contentType);

    found.Should().BeTrue();
    contentType.Should().Be("image/png");
  }
}
