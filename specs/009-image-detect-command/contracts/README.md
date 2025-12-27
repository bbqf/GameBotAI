# Contracts: Commands Based on Detected Image

No new service endpoints are introduced by this feature. The command schema is extended to include `detectionTarget` parameters.

Artifacts:
- `command.schema.fragment.json`: JSON Schema fragment describing `DetectionTarget`.

If future endpoints expose command creation/update via API, merge this fragment into the public command schema in the service OpenAPI.
