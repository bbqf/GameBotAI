using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Images;
using GameBot.Domain.Parameters;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Builds the synchronous "does this image exist" check that parameter validation needs (feature 168).
/// The image store is asynchronous, so the check reads the store one time for each candidate ID before
/// validation starts.
/// </summary>
internal static class ParameterImageLookup {
  /// <summary>
  /// Looks up every image ID that the declarations list as allowed and every value that a binding sets.
  /// </summary>
  /// <param name="images">The image store.</param>
  /// <param name="declarations">Declarations with allowed images.</param>
  /// <param name="bindings">Bindings whose values may be image IDs.</param>
  /// <param name="ct">Cancellation token.</param>
  public static async Task<System.Func<string, bool>> BuildAsync(
      IImageRepository images,
      IEnumerable<ParameterDeclaration> declarations,
      IEnumerable<ParameterBinding?> bindings,
      CancellationToken ct) {
    var candidates = new HashSet<string>(System.StringComparer.Ordinal);
    foreach (var declaration in declarations) {
      if (declaration.AllowedImages is null) continue;
      foreach (var id in declaration.AllowedImages) candidates.Add(id);
    }

    foreach (var binding in bindings) {
      if (!string.IsNullOrWhiteSpace(binding?.Value) && !ParameterNameRules.IsWholePlaceholder(binding.Value)) {
        candidates.Add(binding.Value);
      }
    }

    var existing = new HashSet<string>(System.StringComparer.Ordinal);
    foreach (var id in candidates.Where(c => !string.IsNullOrWhiteSpace(c))) {
      if (await images.ExistsAsync(id, ct).ConfigureAwait(false)) existing.Add(id);
    }

    return existing.Contains;
  }
}
