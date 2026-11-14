# Smart Scheduling

Feature 189. Smart scheduling makes an idle queue wake up less often. It merges near wake-ups of different sequences into one wake-up.

## What it does

A sequence in a queue can book its next firing with the `reschedule-self` action and the option `Timer`. Each sequence books its own time, so the times of different sequences do not align. An idle queue thus wakes up many times, and each wake-up does little work.

With smart scheduling on, the queue looks at each new Timer booking. When a firing of a different sequence is already pending a short time after the requested time, the booking moves to the time of that firing. The two sequences then run in the same wake-up.

## Queue settings

Set the two settings in the queue form (Queues, edit a queue) or with the queue API (`smartScheduling`, `smartSchedulingWindowMinutes`).

| Setting | Default | Range |
|---------|---------|-------|
| Smart scheduling | Off | On or off |
| Window (minutes) | 15 | 1 to 120 |

The window field shows only when the switch is on. When you turn the switch off, the queue keeps the window for the next time.

A saved change applies to the next booking of a running queue. You do not have to restart the queue. A booking that exists keeps its time. This is the one exception to the rule "settings apply at the next start". The note near Save on the edit page of a running queue says this.

## The rule

The rule applies only to a `reschedule-self` step with the option `Timer`, in an active queue run, when the stored queue has smart scheduling on and the sequence does not ignore it.

1. The step requests the time T.
2. The queue collects the pending firings of the other sequences of the run.
3. It keeps each firing with a due time later than T and not later than T plus the window.
4. When one or more firings stay, the booking gets the earliest due time. Otherwise the booking keeps T.

Example 1 (window 15 minutes): sequence A has a pending firing at 12:10. Sequence B requests 12:00. The firing of A is 10 minutes later, so B is booked for 12:10. A and B run in the same wake-up.

Example 2 (window 15 minutes): sequence A has a pending firing at 12:20. Sequence B requests 12:00. The firing of A is 20 minutes later, so B keeps 12:00.

The maximum delay of a booking is one window. A booked time is never earlier than the requested time. A booked time never changes after the booking.

These pending firings can be a target: a Timer booking of a different sequence, a template Timer entry (time of day or relative offset), an armed daily retry, and a pending live schedule.

## What does not move

Smart scheduling moves only the new Timer booking. These schedules always keep their time:

- Template Timer entries (time of day and relative offset).
- Live schedules.
- Run-next requests.
- The other `reschedule-self` options (`OncePerRun`, `AtQueueStart`, `EveryStep`, `Cancel`).
- A booking of a sequence that ignores smart scheduling.

## Time-critical sequences

Some actions must happen at an exact time, for example a new shield just before the old shield ends. Open the sequence (Authoring, Sequences) and select **Ignore smart scheduling**. The bookings of this sequence then always keep the requested time. The setting is off by default. The API member is `ignoreSmartScheduling`.

A sequence that ignores smart scheduling can still be a target: other sequences can move to its booking.

## Where to see a postponement

- **Execution log**: the step of a postponed booking has the message `rescheduled this sequence (option Timer, <booked time>); postponed from <requested time> to the firing of '<target>' (smart scheduling, window <N> min); applies to the current run only`. The step details have the attributes `requestedFireAt`, `bookedFireAt`, `smartSchedulingTarget`, `smartSchedulingTargetKind`, and `smartSchedulingWindowMinutes`.
- **Queue monitor**: a postponed firing shows the booked time and the reason `Rescheduled by a sequence, postponed from HH:mm (smart scheduling)`. The monitor header shows `Smart scheduling: N min` when the setting is on.
- **Queue schedule rows** (large screen and phone): the row of a postponed firing shows ` · postponed from HH:mm` after the time.

A booking that is not postponed shows the same text as before this feature.

## Why 15 minutes

The execution logs of 2026-11-06 to 2026-11-13 have 14.3 queue-days on four production queues, with 1 220 wake-ups. A queue woke up 85 times each day. 55% of the idle gaps were 10 minutes or less. A simulation on the same logs gives these numbers:

| Window | Wake-ups for each queue-day | Decrease |
|--------|-----------------------------|----------|
| Off    | 85                          | 0%       |
| 5 min  | 72                          | 15%      |
| 10 min | 58                          | 32%      |
| 15 min | 47                          | 44%      |
| 20 min | 41                          | 52%      |
| 30 min | 32                          | 63%      |
| 60 min | 19                          | 77%      |

A window of 15 minutes removes almost half of the wake-ups, and the maximum delay of a reward stays at 15 minutes. The simulation is an estimate, because a moved firing also moves the bookings that follow it.
