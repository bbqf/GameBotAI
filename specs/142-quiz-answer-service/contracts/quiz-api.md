# Contract: Quiz record API

All routes use the existing access rules of the service API. The service shows the schemas in OpenAPI.

## GET /api/quiz/records

Query (all optional): `store`, `game`, `category`. `store` and `category` match without case. `game`
is an exact match. Filters combine with AND. Each record shows its `store` name.

200 response: a JSON array of records.

```json
[
  {
    "id": "3f2a...",
    "store": "pns-quiz",
    "game": "pns",
    "category": "radio",
    "question": "which planet is red",
    "answer": "mars",
    "createdAt": "2026-10-14T10:00:00Z",
    "updatedAt": "2026-10-14T10:00:00Z"
  }
]
```

A damaged store file does not fail the list. The response skips that store and the service logs a warning.

## GET /api/quiz/records/{id}

- 200: one record (same shape).
- 404: problem response, "Quiz record not found."

## DELETE /api/quiz/records/{id}

- 204: record removed.
- 404: problem response, "Quiz record not found."

## Errors

- A bad `id` shape (not a valid file-safe value) gives 404.
- No route creates or changes a record. Only the `quiz-learn` step does.
