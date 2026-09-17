# Quickstart: Resume Queues After a Service Restart

## Opt a queue in

```powershell
$h = @{ Authorization = "Bearer <token>" }
Invoke-RestMethod -Method Put -Uri http://localhost:8080/api/queues/<id> -Headers $h -ContentType application/json `
  -Body '{"name":"PNS Daily 5558","cycleExecution":false,"pauseWhenIdle":true,"idleThresholdSeconds":30,"resumeOnServiceStart":true}'
```

(or tick **Resume after service restart** in the queue form; the queue must be stopped to edit it).

## Verify

1. Start the queue: `POST /api/queues/<id>/start`. `data/queue-run-state.json` now lists its id.
2. Restart the service (or kill the process and start it again).
3. Within a few seconds `GET /api/queues/<id>` reports `"status": "Running"`, and the service log has event 7300 with outcome `Started`.

## Negative checks

- Stop the queue before restarting: the id disappears from `queue-run-state.json`, and after the restart the queue stays `Stopped`.
- A running queue with `resumeOnServiceStart: false`: after the restart it stays `Stopped` and the log has event 7301.
