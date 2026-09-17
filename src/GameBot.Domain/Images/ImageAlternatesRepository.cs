using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Domain.Images {
  /// <summary>
  /// Stores, per reference image, the ordered list of alternate reference images that also count as a
  /// match for it — e.g. night-lit crops of daylight art (feature 097, issue #192).
  /// </summary>
  public interface IImageAlternatesRepository {
    /// <summary>Returns the alternates of <paramref name="primaryId"/> in registered order; empty when none or the id is invalid.</summary>
    IReadOnlyList<string> GetAlternates(string primaryId);

    /// <summary>Replaces the whole list atomically. An empty list removes it.</summary>
    /// <exception cref="ArgumentException"><paramref name="primaryId"/> is not a valid image id.</exception>
    void SetAlternates(string primaryId, IReadOnlyList<string> alternates);

    /// <summary>Removes the list. Returns false when there was none.</summary>
    bool DeleteAlternates(string primaryId);
  }

  /// <summary>
  /// File-backed alternates: one <c>{id}.json</c> sidecar per primary under <c>&lt;root&gt;\.alternates</c>.
  /// </summary>
  /// <remarks>
  /// The image repository lists top-directory image files only, so the dot-folder never surfaces as an
  /// image. Writes go through <c>&lt;root&gt;\.tmp</c> and a replace, so a reader sees either the old
  /// list or the new one, and concurrent writers resolve as last write wins.
  /// </remarks>
  public sealed class FileImageAlternatesRepository : IImageAlternatesRepository {
    private const string FolderName = ".alternates";
    private readonly string _root;
    private readonly string _dir;

    public FileImageAlternatesRepository(string root) {
      ArgumentNullException.ThrowIfNull(root);
      _root = root;
      _dir = Path.Combine(root, FolderName);
    }

    private sealed class Document {
      public List<string> Alternates { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private string PathFor(string primaryId) => Path.Combine(_dir, primaryId + ".json");

    public IReadOnlyList<string> GetAlternates(string primaryId) {
      if (!ReferenceImageIdValidator.IsValid(primaryId)) return Array.Empty<string>();
      var path = PathFor(primaryId);
      if (!File.Exists(path)) return Array.Empty<string>();
      var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), JsonOpts);
      return doc?.Alternates?.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray() ?? Array.Empty<string>();
    }

    public void SetAlternates(string primaryId, IReadOnlyList<string> alternates) {
      ArgumentNullException.ThrowIfNull(alternates);
      if (!ReferenceImageIdValidator.IsValid(primaryId)) throw new ArgumentException("invalid id", nameof(primaryId));

      if (alternates.Count == 0) {
        DeleteAlternates(primaryId);
        return;
      }

      Directory.CreateDirectory(_dir);
      var tmpDir = Path.Combine(_root, ".tmp");
      Directory.CreateDirectory(tmpDir);
      var tmp = Path.Combine(tmpDir, $"{primaryId}.alternates.{Guid.NewGuid():N}.tmp");
      File.WriteAllText(tmp, JsonSerializer.Serialize(new Document { Alternates = alternates.ToList() }, JsonOpts));

      var target = PathFor(primaryId);
      if (File.Exists(target)) {
        File.Replace(tmp, target, null);
      }
      else {
        File.Move(tmp, target, overwrite: true);
      }
    }

    public bool DeleteAlternates(string primaryId) {
      if (!ReferenceImageIdValidator.IsValid(primaryId)) return false;
      var path = PathFor(primaryId);
      if (!File.Exists(path)) return false;
      File.Delete(path);
      return true;
    }
  }
}
