# Quickstart: Revalidate an image after an overwrite

1. Upload an image: `POST /api/images` with `{"id":"demo","data":"<base64 PNG>"}`.
2. `GET /api/images/demo`. Check that the response has `Cache-Control: no-cache` and an `ETag`. Keep the ETag value.
3. `GET /api/images/demo` with `If-None-Match: <ETag>`. The answer is 304.
4. `GET /api/images/demo` with `If-Modified-Since: <Last-Modified>`. The answer is 304.
5. `PUT /api/images/demo` with `{"data":"<base64 of other PNG bytes>"}`.
6. `GET /api/images/demo` with `If-None-Match: <old ETag>`. The answer is 200 with the new bytes and a new ETag.
7. Open the web UI that showed the image before. The new picture shows with no hard refresh.

Run the tests:

```text
dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Images"
```
