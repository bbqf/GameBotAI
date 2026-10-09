using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Service.Services.Updates;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace GameBot.UnitTests.Updates;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test helper. The HttpClient only wraps a fake handler that holds no resources.")]
public sealed class UpdateDownloaderTests : IDisposable {
  private static readonly byte[] Content = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

  private readonly string _root = UpdateTestData.NewTempDirectory();
  private readonly UpdatePaths _paths;

  public UpdateDownloaderTests() {
    _paths = new UpdatePaths(_root);
  }

  public void Dispose() {
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private static string ContentHash() => Convert.ToHexStringLower(SHA256.HashData(Content));

  private static ReleaseInfo Release(long size = 5000, string? sha = null) =>
    UpdateTestData.Release(size: size, sha: sha ?? ContentHash());

  private UpdateDownloader Downloader(FakeHttpHandler handler) => new(new HttpClient(handler), _paths);

  private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

  [Fact]
  public async Task DownloadWritesTheFileToTheUpdatesFolder() {
    using var handler = new FakeHttpHandler(_ => Bytes(Content));

    var path = await Downloader(handler).DownloadAsync(Release(), CancellationToken.None).ConfigureAwait(true);

    path.Should().Be(_paths.MsiPath("1.7.0.430"));
    (await File.ReadAllBytesAsync(path).ConfigureAwait(true)).Should().Equal(Content);
  }

  [Fact]
  public async Task RedirectToAnAllowedHostIsFollowed() {
    using var handler = new FakeHttpHandler(request => request.RequestUri!.Host == "github.com"
      ? FakeHttpHandler.Redirect("https://objects.githubusercontent.com/file")
      : Bytes(Content));

    var path = await Downloader(handler).DownloadAsync(Release(), CancellationToken.None).ConfigureAwait(true);

    File.Exists(path).Should().BeTrue();
    handler.Requests.Should().HaveCount(2);
  }

  [Fact]
  public async Task RedirectToABadHostFailsAndLeavesNoFile() {
    using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Redirect("https://evil.example.com/file"));

    var act = () => Downloader(handler).DownloadAsync(Release(), CancellationToken.None);

    var failure = (await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true)).Which;
    failure.Error.Code.Should().Be("update_download_failed");
    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse();
  }

  [Fact]
  public async Task HttpErrorFailsTheDownload() {
    using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

    var act = () => Downloader(handler).DownloadAsync(Release(), CancellationToken.None);

    var failure = (await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true)).Which;
    failure.Error.Code.Should().Be("update_download_failed");
    failure.Error.Hint.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task NetworkStopInTheMiddleDeletesThePartialFile() {
    using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
      Content = new StreamContent(new BrokenStream(Content, 1000))
    });

    var act = () => Downloader(handler).DownloadAsync(Release(), CancellationToken.None);

    var failure = (await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true)).Which;
    failure.Error.Code.Should().Be("update_download_failed");
    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse();
  }

  [Fact]
  public async Task FileWithTheWrongSizeFailsTheDownload() {
    using var handler = new FakeHttpHandler(_ => Bytes(Content));

    var act = () => Downloader(handler).DownloadAsync(Release(size: 9999), CancellationToken.None);

    var failure = (await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true)).Which;
    failure.Error.Code.Should().Be("update_download_failed");
    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse();
  }

  [Fact]
  public async Task CancelDeletesThePartialFileAndRethrows() {
    using var cts = new CancellationTokenSource();
    using var handler = new FakeHttpHandler(_ => {
      cts.Cancel();
      throw new OperationCanceledException(cts.Token);
    });

    var act = () => Downloader(handler).DownloadAsync(Release(), cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse();
  }

  [Fact]
  public async Task OldFileOfTheSameVersionIsReplaced() {
    Directory.CreateDirectory(_paths.Directory);
    await File.WriteAllTextAsync(_paths.MsiPath("1.7.0.430"), "old").ConfigureAwait(true);
    using var handler = new FakeHttpHandler(_ => Bytes(Content));

    var path = await Downloader(handler).DownloadAsync(Release(), CancellationToken.None).ConfigureAwait(true);

    (await File.ReadAllBytesAsync(path).ConfigureAwait(true)).Should().Equal(Content);
  }

  [Fact]
  public async Task VerifyAcceptsTheRightChecksum() {
    Directory.CreateDirectory(_paths.Directory);
    var path = _paths.MsiPath("1.7.0.430");
    await File.WriteAllBytesAsync(path, Content).ConfigureAwait(true);
    using var handler = new FakeHttpHandler(_ => Bytes(Content));

    await Downloader(handler).VerifyAsync(path, Release(), CancellationToken.None).ConfigureAwait(true);

    File.Exists(path).Should().BeTrue();
  }

  [Fact]
  public async Task VerifyDeletesTheFileOnAMismatch() {
    Directory.CreateDirectory(_paths.Directory);
    var path = _paths.MsiPath("1.7.0.430");
    await File.WriteAllBytesAsync(path, Content).ConfigureAwait(true);
    using var handler = new FakeHttpHandler(_ => Bytes(Content));

    var act = () => Downloader(handler).VerifyAsync(path, Release(sha: new string('b', 64)), CancellationToken.None);

    var failure = (await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true)).Which;
    failure.Error.Code.Should().Be("update_checksum_mismatch");
    File.Exists(path).Should().BeFalse();
  }

  /// <summary>A stream that gives some bytes, then fails like a dropped connection.</summary>
  private sealed class BrokenStream : Stream {
    private readonly byte[] _data;
    private readonly int _failAt;
    private int _position;

    public BrokenStream(byte[] data, int failAt) {
      _data = data;
      _failAt = failAt;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) {
      if (_position >= _failAt) {
        throw new IOException("connection lost");
      }

      var n = Math.Min(Math.Min(count, _failAt - _position), 500);
      Array.Copy(_data, _position, buffer, offset, n);
      _position += n;
      return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
