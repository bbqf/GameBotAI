# Data Model: Quiz Answer Service

## QuizRecord (stored)

| Field | Type | Rule |
|-------|------|------|
| id | string (GUID, "N" format) | Made on create. Unique across stores. |
| store | string | Required. Free text. Compared without case. |
| game | string | Required. Game identifier. Exact match (case-sensitive). |
| category | string | Required. No default. Compared without case. |
| question | string | The normalized question text. Not empty. |
| answer | string | The normalized correct answer text. Not empty. |
| createdAt | UTC timestamp | Set on create. |
| updatedAt | UTC timestamp | Set on create and on each change. |

Key: (store, game, category, question). At most one record for one key.

## Store file

`<dataRoot>/quiz/<hash>.json` with `{ "name": "<store name>", "records": [ QuizRecord ... ] }`.
Hash = first 32 hex characters of SHA-256 of the lower-case store name. The service writes a temp
file, then replaces the target.

## QuizLookupResult (in memory, per run)

| Field | Type |
|-------|------|
| stepId | string |
| store, game, category | string |
| question | string (normalized) |
| answers | list of string (normalized, in region order) |
| similarity | number (the limit that the lookup used) |
| outcome | string |

Valid only when the outcome is `pick_N`. A lookup that gave `unreadable` or `service_unavailable`,
or that did not run, keeps no result, so a later learn gives `skipped_no_match` and the log says why.
A region is unreadable when its text is empty or only white space after normalization, or when the
OCR engine fails.

## Step payloads

`quiz-lookup` parameters:

| Name | Type | Rule |
|------|------|------|
| game | string | Required. |
| category | string | Required. Not blank. |
| store | string | Required. Not blank. |
| question | region (x, y, width, height) | Required. |
| answers | list of region | Required. Two or more. |
| similarity | number | Optional. From 0 to 1. Default 0.85. |

`quiz-learn` parameters:

| Name | Type | Rule |
|------|------|------|
| lookupStepId | string | Required. Must name a `quiz-lookup` step that is before this step. |
| correctRegion | region | One of `correctRegion` or `correctPosition`. |
| correctPosition | integer, 1 to N | One of the two. Not both. |

## Outcomes

- Lookup: `pick_1` to `pick_N`, `unreadable`, `service_unavailable`.
- Learn: `stored`, `skipped_no_match`, `skipped_ambiguous`, `service_unavailable`.

## State transitions

- Learn first matches the correct text to the lookup answers by similarity. Exactly one match is
  required, or nothing is stored.
- Learn on a new key: create record.
- Learn on a known key, same answer: update `updatedAt`.
- Learn on a known key, new answer: replace `answer`, update `updatedAt`.
- Delete: remove record. The next lookup of the question is a guess.
