using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Triggers.Evaluators;

[SupportedOSPlatform("windows")]
public sealed class MemoryReferenceImageStore : IReferenceImageStore {
  private readonly ConcurrentDictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
  public void AddOrUpdate(string id, Bitmap bitmap) => _images.AddOrUpdate(id, bitmap, (_, __) => bitmap);
  public bool TryGet(string id, out Bitmap bitmap) => _images.TryGetValue(id, out bitmap!);
  public bool Exists(string id) => _images.ContainsKey(id);
  public bool Delete(string id) => _images.TryRemove(id, out _);
}
