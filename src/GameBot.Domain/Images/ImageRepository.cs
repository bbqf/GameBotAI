using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Domain.Images
{
    public interface IImageRepository
    {
        Task<IReadOnlyCollection<string>> ListIdsAsync(CancellationToken ct = default);
        Task<ImageAsset?> GetAsync(string id, CancellationToken ct = default);
        Task<Stream?> OpenReadAsync(string id, CancellationToken ct = default);
        Task<ImageAsset> SaveAsync(string id, Stream content, string contentType, string? filename, bool overwrite, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
        Task<bool> ExistsAsync(string id, CancellationToken ct = default);
    }

    public sealed record ImageAsset(
        string Id,
        string ContentType,
        long SizeBytes,
        string? Filename,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    [SupportedOSPlatform("windows")]
    public sealed class FileImageRepository : IImageRepository
    {
        private const int MaxImageBytes = 10_000_000;
        private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".png",
        };

        private readonly string _root;

        public FileImageRepository(string root)
        {
            _root = root;
            Directory.CreateDirectory(_root);
        }

        public Task<IReadOnlyCollection<string>> ListIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var ids = Directory.EnumerateFiles(_root, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsSupportedExtension(Path.GetExtension(f)))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(id => !string.IsNullOrWhiteSpace(id) && ReferenceImageIdValidator.IsValid(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<string>>(ids);
        }

        public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceImageIdValidator.IsValid(id)) return Task.FromResult(false);
            var path = ResolveExistingPath(id);
            return Task.FromResult(path is not null && File.Exists(path));
        }

        public Task<ImageAsset?> GetAsync(string id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceImageIdValidator.IsValid(id)) return Task.FromResult<ImageAsset?>(null);
            var path = ResolveExistingPath(id);
            if (path is null || !File.Exists(path)) return Task.FromResult<ImageAsset?>(null);
            var info = new FileInfo(path);
            var asset = new ImageAsset(
                id,
                MapContentType(Path.GetExtension(path)),
                info.Length,
                Filename: Path.GetFileName(path),
                info.CreationTimeUtc,
                info.LastWriteTimeUtc);
            return Task.FromResult<ImageAsset?>(asset);
        }

        public Task<Stream?> OpenReadAsync(string id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceImageIdValidator.IsValid(id)) return Task.FromResult<Stream?>(null);
            var path = ResolveExistingPath(id);
            if (path is null || !File.Exists(path)) return Task.FromResult<Stream?>(null);
            Stream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<Stream?>(stream);
        }

        public async Task<ImageAsset> SaveAsync(string id, Stream content, string contentType, string? filename, bool overwrite, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(contentType);
            ct.ThrowIfCancellationRequested();

            if (!ReferenceImageIdValidator.IsValid(id)) throw new ArgumentException("invalid id", nameof(id));

            var normalizedContentType = MapContentType(contentType);
            var targetPath = ResolveTargetPath(id, normalizedContentType);
            var existingPath = ResolveExistingPath(id);
            if (!overwrite && existingPath is not null && File.Exists(existingPath))
            {
                throw new InvalidOperationException($"Image '{id}' already exists.");
            }

            Directory.CreateDirectory(_root);
            var tmpDir = Path.Combine(_root, ".tmp");
            Directory.CreateDirectory(tmpDir);
            var tmpFile = Path.Combine(tmpDir, $"{id}.{Guid.NewGuid():N}.tmp");

            using (var fs = File.Open(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            var infoTmp = new FileInfo(tmpFile);
            if (infoTmp.Length <= 0)
            {
                File.Delete(tmpFile);
                throw new InvalidOperationException("Uploaded image is empty.");
            }
            if (infoTmp.Length > MaxImageBytes)
            {
                File.Delete(tmpFile);
                throw new InvalidOperationException("Uploaded image exceeds size limit.");
            }

            // Ensure consistent extension; remove old files with different extensions when overwriting
            if (existingPath is not null && !string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(existingPath))
            {
                File.Delete(existingPath);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(tmpFile, targetPath, null);
            }
            else
            {
                File.Move(tmpFile, targetPath);
            }

            var info = new FileInfo(targetPath);
            return new ImageAsset(
                id,
                normalizedContentType,
                info.Length,
                Filename: filename,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceImageIdValidator.IsValid(id)) return Task.FromResult(false);
            var path = ResolveExistingPath(id);
            if (path is null || !File.Exists(path)) return Task.FromResult(false);
            File.Delete(path);
            return Task.FromResult(true);
        }

        private string? ResolveExistingPath(string id)
        {
            foreach (var ext in ExtensionByContentType.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var p = Path.Combine(_root, id + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private string ResolveTargetPath(string id, string contentType)
        {
            var ext = ExtensionByContentType.TryGetValue(contentType, out var mapped) ? mapped : ExtensionByContentType["image/png"];
            return Path.Combine(_root, id + ext);
        }

        private static bool IsSupportedExtension(string ext) => ExtensionByContentType.Values.Contains(ext, StringComparer.OrdinalIgnoreCase);

        private static string MapContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return "image/png";
                if (ExtensionByContentType.ContainsKey(contentType)) return contentType;
            // Attempt to map common short names
            return contentType.Equals("png", StringComparison.OrdinalIgnoreCase) ? "image/png" :
                   contentType.Equals("jpeg", StringComparison.OrdinalIgnoreCase) || contentType.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                       ? "image/jpeg" : "image/png";
        }
    }
}
