# Research: Reject unknown command step fields

## D-1: How to find unknown fields

- Decision: Write a scanner that walks the raw `JsonElement` and compares each name with the public
  properties of the DTO type (reflection, cached).
- Rationale: The DTOs are the source of truth for the published schema. The scanner needs no schema
  copy, so schema and check cannot drift. It reports all paths in one pass (clarification Q1).
- Alternatives considered:
  - `JsonUnmappedMemberHandling.Disallow` in the serializer options: it stops at the first field and
    its message gives a short path in some cases. It is also case-insensitive-safe, but it cannot list
    all fields and it changes the options that other code shares. Rejected.
  - Validate the body against the generated OpenAPI JSON schema: needs a new dependency and a second
    source of truth. Rejected.
  - `[JsonExtensionData]` on each DTO: changes the DTOs and can change the OpenAPI schema. Rejected (FR-006).

## D-2: Path format

- Decision: Dotted names with `[i]` for arrays, from the body root, in the spelling the client used.
- Rationale: Matches the issue example and clarification Q2.
- Alternatives considered: JSON Pointer (`/steps/0/...`). Rejected: the spec asks for dotted form.

## D-3: Root-level fields and the `detectionTarget` alias

- Decision: Unknown root fields give 400 (clarification Q3), with one exception: the undocumented
  root alias `detectionTarget`, read by `TryReadDetection` on the create endpoint, stays accepted
  and the scanner does not flag it (spec edge case, loop 1 clarification).
- Rationale: FR-004. Existing valid requests must not change.
- Alternatives considered: reject the alias. Rejected: it could break an existing client.
  Implementation still searches for real callers and records the result here.

## D-4: PATCH order of checks

- Decision: Keep the order 404 for a missing command, then the scan, then any change. The scan
  happens before the first change to the stored command, so FR-003 holds.
- Rationale: Today the 404 check runs first. Keeping it avoids a change for valid requests (FR-004).
  Binding the body by hand needs the body to be read first, so parse the body, then do the lookup
  (404 first), then scan.
- Alternatives considered: scan before lookup. Would change the answer for a missing command with a
  bad body. Rejected.

## D-5: Dictionary and ignored properties

- Decision: Do not recurse into `Dictionary<string,string>` (`fieldTemplates`). Treat `[JsonIgnore]`
  properties as unknown in a body.
- Rationale: Dictionary keys are data. `DetectionSpecified` is not in the schema.
- Alternatives considered: none needed.

## D-6: Case handling

- Decision: Compare names with OrdinalIgnoreCase. This matches `JsonSerializerDefaults.Web`.
- Rationale: Clarification Q4 and FR-004.

## D-7: PATCH answers that must not change

- Decision: The setup task records today's PATCH answers: valid update, empty body, malformed JSON,
  and malformed JSON on a missing command. Tests then assert the same answers after the change.
- Rationale: The spec (loop 2 clarification) says keep today's answers and do not guess them.
  Manual body binding must give the same status and body as the framework binding gave.
- Alternatives considered: Assume the framework answers. Rejected: a guess can change valid behaviour.

No NEEDS CLARIFICATION items remain.
