using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007, CA1861, CA1707, CA1002, CA1819, CA2227, CA2000, CA1305, CA1307, CA1310, CA1812, CA1849

namespace GameBot.UnitTests.Gamepacks;

/// <summary>One answer of the test catalog server (feature 147).</summary>
internal sealed class CatalogReply {
  public int Status { get; init; } = 200;

  public byte[] Body { get; init; } = Array.Empty<byte>();

  public string? Location { get; init; }

  /// <summary>The server waits this long before it answers.</summary>
  public TimeSpan Stall { get; init; }
}

/// <summary>A local web server for the catalog tests (feature 147). It records each request path and the authorization header.</summary>
internal sealed class CatalogTestServer : IAsyncDisposable, IDisposable {
  private WebApplication? _app;

  private CatalogTestServer() {
  }

  /// <summary>The base address with no trailing slash, for example <c>http://127.0.0.1:5000</c>.</summary>
  public string BaseUrl { get; private set; } = string.Empty;

  /// <summary>The answers by absolute path, for example <c>/index.json</c>.</summary>
  public ConcurrentDictionary<string, CatalogReply> Routes { get; } = new();

  /// <summary>All requests: the path and the authorization header.</summary>
  public ConcurrentQueue<(string Path, string? Authorization)> Requests { get; } = new();

  public IReadOnlyList<string> Paths => Requests.Select(r => r.Path).ToList();

  public static async Task<CatalogTestServer> StartAsync() {
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    var server = new CatalogTestServer();
    var app = builder.Build();
    app.Run(server.HandleAsync);
    await app.StartAsync();
    server._app = app;
    server.BaseUrl = app.Urls.First().TrimEnd('/');
    return server;
  }

  public void Set(string path, string text, int status = 200) =>
    Routes[path] = new CatalogReply { Status = status, Body = Encoding.UTF8.GetBytes(text) };

  public void Set(string path, CatalogReply reply) => Routes[path] = reply;

  private async Task HandleAsync(HttpContext context) {
    var path = context.Request.Path.Value ?? "/";
    Requests.Enqueue((path, context.Request.Headers.Authorization.ToString() is { Length: > 0 } auth ? auth : null));
    if (!Routes.TryGetValue(path, out var reply)) {
      context.Response.StatusCode = 404;
      return;
    }

    if (reply.Stall > TimeSpan.Zero) {
      try {
        await Task.Delay(reply.Stall, context.RequestAborted);
      }
      catch (OperationCanceledException) {
        return;
      }
    }

    context.Response.StatusCode = reply.Status;
    if (reply.Location is not null) {
      context.Response.Headers.Location = reply.Location;
    }

    if (reply.Body.Length > 0) {
      context.Response.ContentLength = reply.Body.Length;
      await context.Response.Body.WriteAsync(reply.Body);
    }
  }

  public async ValueTask DisposeAsync() {
    if (_app is null) {
      return;
    }

    await _app.StopAsync();
    await _app.DisposeAsync();
  }

  public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

/// <summary>Builds a catalog in memory: an index and the files. It writes them to a folder or to a test server.</summary>
internal sealed class CatalogFixture {
  private readonly List<object> _gamepacks = new();
  private readonly Dictionary<string, List<object>> _versions = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>The files by relative path.</summary>
  public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

  public static string FilePath(string name, string version) => $"{name}/{version}/{name}.gamepack.json";

  public static string SignaturePath(string name, string version) => $"{name}/{version}/{name}.sig.json";

  public static string Sha256Hex(string text, bool upper = false) {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
    return upper ? Convert.ToHexString(bytes) : Convert.ToHexStringLower(bytes);
  }

  /// <summary>Adds a version. The file is the gamepack text. The hash is the real hash unless <paramref name="sha256"/> is set.</summary>
  public CatalogFixture Add(string name, string version, string pack, string changelog = "", string? signature = null, bool withHash = false, string? sha256 = null) {
    var file = FilePath(name, version);
    Files[file] = Encoding.UTF8.GetBytes(pack);
    var entry = new Dictionary<string, object?> { ["version"] = version, ["changelog"] = changelog, ["file"] = file };
    if (signature is not null) {
      var sigPath = SignaturePath(name, version);
      Files[sigPath] = Encoding.UTF8.GetBytes(signature);
      entry["signature"] = sigPath;
    }

    if (sha256 is not null) {
      entry["sha256"] = sha256;
    }
    else if (withHash) {
      entry["sha256"] = Sha256Hex(pack);
    }

    if (!_versions.TryGetValue(name, out var list)) {
      list = new List<object>();
      _versions[name] = list;
      _gamepacks.Add(new Dictionary<string, object> { ["name"] = name, ["versions"] = list });
    }

    list.Add(entry);
    return this;
  }

  public string IndexJson() => JsonSerializer.Serialize(new { formatVersion = 1, gamepacks = _gamepacks });

  public void WriteTo(string folder) {
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "index.json"), IndexJson());
    foreach (var (path, bytes) in Files) {
      var full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
      Directory.CreateDirectory(Path.GetDirectoryName(full)!);
      File.WriteAllBytes(full, bytes);
    }
  }

  public void ServeFrom(CatalogTestServer server, string prefix = "") {
    server.Set(prefix + "/index.json", IndexJson());
    foreach (var (path, bytes) in Files) {
      server.Set(prefix + "/" + path, new CatalogReply { Body = bytes });
    }
  }

  /// <summary>A simple gamepack file text with one sequence, one command, and one image.</summary>
  public static string SimplePack(string name, string version, string sequenceName = "Help All") =>
    GamepackTestKit.Pack(name, version,
      sequences: new[] { GamepackTestKit.Seq("a1", sequenceName, "c1") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
      images: new[] { GamepackTestKit.Img("help-icon") });
}
