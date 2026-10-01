# Quickstart: Reschedule-Self Cancel

## Author a sequence

1. Add step 0: `reschedule-self` with `{ "option": "Timer", "timerRelativeOffset": "00:30:00" }`.
2. Add the work steps.
3. Add a final step: `reschedule-self` with `{ "option": "Cancel" }`. In the web UI editor, pick "Cancel pending booking". The timer fields are hidden.
4. Save the sequence. A 201 or 200 response confirms that the payload is valid.

## Verify

1. Put the sequence in a queue and start the queue.
2. After the run succeeds, read the step outcome. It is `cancelled` with `removed` true.
3. Read the queue monitor. The sequence has no pending booking.
4. Wait 30 minutes. The queue does not wake for the sequence.
5. Make a work step fail by name. The booking of step 0 stays, and the queue wakes 30 minutes later.
6. Run the sequence with no queue. The Cancel step succeeds with outcome `noop` and `removed` false.

7. Put two OncePerRun bookings of one sequence in one cycle and run a Cancel step in the first firing. The second
   booking does not fire.

## Run the tests

1. Run `dotnet build` for the solution.
2. Run `dotnet test` with a filter on `RescheduleSelfCancel` for the new tests.
3. Run `dotnet test` for the full unit, contract and integration projects.
4. In `src\web-ui`, run `npx vite build` and `npx jest` for the option list.
