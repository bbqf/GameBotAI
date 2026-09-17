# Research: Alternate Reference Images (097)

## R-001 Lighting-robust matching vs. multi-reference target

- **Decision**: Multi-reference target (alternates).
- **Rationale**: `TemplateMatcher` already scores with `TemplateMatchModes.CCoeffNormed`, which subtracts
  the window mean and divides by its standard deviation — i.e. it is already invariant to a global
  luma offset and contrast gain. The issue's night scores (0.81–0.87) therefore come from a non-global
  change (tint, local shading, glow) that another global normalisation cannot remove. Any scoring
  change would also shift every calibrated threshold in live queues (non-goal; the matcher's own comment
  forbids even routing unmasked templates through an algebraically equal formulation).
- **Alternatives**: CLAHE/histogram equalisation pre-pass (changes all scores, unproven on the failing
  case); per-channel/HSV matching (same drift problem); new "image set" entity with its own id namespace
  (every sequence would have to be edited to point at the set — contrary to "property of the image").

## R-002 Where the fan-out lives

- **Decision**: `ReferenceSetTemplateMatcher`, an `ITemplateMatcher` decorator constructed per detection
  with the alternates' Mats; the `templateMat` argument is the primary.
- **Rationale**: `DetectionCoordinateResolver`, `ActionExecutionAdapter`, `CommandRunner` and the detect
  endpoint all consume `ITemplateMatcher.MatchAllAsync(screen, template, config)`. Wrapping the matcher
  leaves their selection logic (highest confidence, offsets, clamping) intact and shared.
- **Alternatives**: overloads taking `IReadOnlyList<Mat>` through adapter → runner → resolver (touches
  4 layers, duplicates selection logic).

## R-003 Merge semantics

- **Decision**: Run inner matcher per reference with the caller's config; tag matches with `ReferenceId`;
  concatenate; stable sort by confidence desc, then reference index asc, then bbox (x, y, w, h) asc;
  apply `Nms.Apply(candidates, config.Overlap, config.MaxResults)`. `LimitsHit` = any inner LimitsHit or
  merged candidate count > kept count at the cap. `Masked`/`RetainedPixelCount` come from the primary;
  `NoInformationPositionCount` is summed.
- **Rationale**: Identical to single-template ordering and NMS; ties prefer primary (clarification Q1).
- **Zero alternates**: decorator not constructed — the raw inner result is returned untouched.

## R-004 Similarity for triggers/conditions

- **Decision**: `ImageMatchEvaluator` computes its existing per-template similarity for each reference
  and returns the maximum; logs the reference when an alternate supplies the max.
- **Rationale**: The evaluator has its own constant-template / same-size fallback paths; reusing its
  per-template function keeps those semantics per reference.

## R-005 Persistence

- **Decision**: Sidecar `<imagesRoot>\.alternates\{primaryId}.json` → `{ "alternates": ["id", ...] }`,
  written to `<imagesRoot>\.tmp` then `File.Replace`/`File.Move` (atomic replace, last write wins).
  An empty list deletes the sidecar. A per-repository `SemaphoreSlim`-free approach is sufficient: the
  file replace is atomic, and concurrent writers resolve last-write-wins (clarification Q4).
- **Rationale**: `FileImageRepository.ListIdsAsync` enumerates top-directory `.png` files only, so a
  dot-folder of `.json` files never appears as an image. One file per primary makes delete trivial.
- **Alternatives**: one `alternates.json` map (needs a lock and whole-file rewrite); PNG text chunks
  (rewrite image bytes, breaks overwrite semantics).

## R-006 Missing alternates

- **Decision**: `ReferenceImageSetLoader` skips alternates whose image is absent and returns their ids in
  `MissingAlternateIds`; consumers log a warning once per detection. `GET .../alternates` returns
  `exists: false` for them.
- **Rationale**: FR-008; deleting an image must not break detections of another image.

## R-007 Backup / restore

- **Decision**: For every image already selected for backup, also add its existing alternates' images,
  and write `image-alternates/{primaryId}.json` for primaries that have alternates. Restore applies each
  sidecar after images are saved, filtering ids to those present afterwards. Manifest version stays `1.0`
  (additive entries; older archives restore unchanged; older readers ignore unknown entries).
- **Rationale**: FR-010 with no format break.

## R-008 OpenAPI

- **Decision**: New endpoints declare `.Produces<ImageAlternatesResponse>()`, `.ProducesProblem`-style
  400/404 and `.WithDescription(...)`; request/response are named record types so Swashbuckle emits
  schemas. `MatchResult.MatchedReferenceId` gets a description via a small schema filter
  (`ImageAlternatesSchemaFilter`), mirroring `QueueHealthSchemaFilter`, because XML comments are not fed
  to Swagger.
