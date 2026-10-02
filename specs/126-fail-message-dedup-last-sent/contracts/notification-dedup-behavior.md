# Contract: Failure message dedup behavior

This feature adds no endpoint, no field, and no setting. The only contract is the message behavior of the worker.

## Rule

For each enabled target, the worker sends a failure message unless the last message that it started to send
through that target has the same queue ID, sequence ID, status, and text.

## Cases

| Input sequence on one target | Messages sent |
| --- | --- |
| failure (A), failure (A) | failure |
| failure (A), success (B), failure (A) | failure, success, failure |
| failure (A), failure (A), success (A) | failure, recovered |
| failure (A, queue 1), failure (B, queue 2), failure (A, queue 1) | three failures |
| failure (A, reason 1), failure (A, reason 2) | two failures |
| failure (A), device alert, failure (A) | failure, alert, failure (the alert is the last message) |
| failure (A) on target 1, target 2 is added, failure (A) again | target 2 gets the message, target 1 does not |
| failure (A) with level None or a dropped message between | the dropped message does not count |

## Not changed

- Message text and format.
- The "recovered", "success", and "cancelled" rules.
- The notification endpoints and their schemas.
