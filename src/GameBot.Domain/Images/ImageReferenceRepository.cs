using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Domain.Images
{
    public interface IImageReferenceRepository
    {
        Task<IReadOnlyCollection<string>> FindReferencingTriggerIdsAsync(string imageId, CancellationToken ct = default);
    }

    public sealed class TriggerImageReferenceRepository : IImageReferenceRepository
    {
        private readonly ITriggerRepository _triggers;

        public TriggerImageReferenceRepository(ITriggerRepository triggers)
        {
            _triggers = triggers;
        }

        public async Task<IReadOnlyCollection<string>> FindReferencingTriggerIdsAsync(string imageId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(imageId);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceImageIdValidator.IsValid(imageId)) return Array.Empty<string>();

            var all = await _triggers.ListAsync(ct).ConfigureAwait(false);
            var ids = all
                .Where(t => t.Params is ImageMatchParams p && string.Equals(p.ReferenceImageId, imageId, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return ids;
        }
    }
}
