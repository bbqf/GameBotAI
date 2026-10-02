import React from 'react';
import type { StepThroughQueueDto } from '../../services/stepThrough';

type QueueBannerProps = {
  queue: StepThroughQueueDto;
  busy: boolean;
  onPause: () => void;
};

/** Shows the queue that owns the device and lets the author pause it (FR-012, FR-012a to FR-012c). */
export const QueueBanner: React.FC<QueueBannerProps> = ({ queue, busy, onPause }) => {
  if (queue.pausedByStepThrough) {
    return (
      <div className="step-through-banner" role="status">
        The queue "{queue.queueName}" is paused by this step-through. It resumes when you close this view.
      </div>
    );
  }

  if (queue.alreadyPaused) {
    return (
      <div className="step-through-banner" role="status">
        The queue "{queue.queueName}" was paused before. This step-through does not resume it.
      </div>
    );
  }

  if (queue.firingActive) {
    return (
      <div className="step-through-banner step-through-banner--warning" role="alert">
        The queue "{queue.queueName}" runs a firing on this device now. Wait for the firing to end, then pause the queue.
      </div>
    );
  }

  return (
    <div className="step-through-banner step-through-banner--warning" role="alert">
      <span>
        The queue "{queue.queueName}" runs on this device. A step could collide with a firing of the queue.
        Pause the queue before you run a step.
      </span>
      <button type="button" onClick={onPause} disabled={busy}>Pause queue</button>
    </div>
  );
};
