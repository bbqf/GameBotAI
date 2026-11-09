import React, { useState } from 'react';
import { QueueStatus } from '../../services/queues';
import { GamePickerDialog } from './GamePickerDialog';
import { IconButton } from './IconButton';

type QueueGameControlsProps = {
  linkedGameId: string | null;
  linkedGameName: string | null;
  status: QueueStatus;
  onLink: (gameId: string) => void;
  onUnlink: () => void;
};

export const QueueGameControls: React.FC<QueueGameControlsProps> = ({
  linkedGameId,
  linkedGameName,
  status,
  onLink,
  onUnlink,
}) => {
  const [pickerOpen, setPickerOpen] = useState(false);
  const running = status === 'Running';

  return (
    <section className="queue-template-controls" aria-label="Queue game">
      <div className="queue-template-row">
        <span className="queue-section-label">Game</span>
        <button
          type="button"
          className="link-button queue-template-name"
          onClick={() => setPickerOpen((o) => !o)}
          aria-expanded={pickerOpen}
        >
          {linkedGameName ?? '(no game)'}
        </button>
        <IconButton
          icon="unlink"
          label="Unlink"
          onClick={onUnlink}
          disabled={!linkedGameId || running}
          title={
            !linkedGameId
              ? 'No game linked.'
              : running
                ? 'Stop the queue before unlinking a game.'
                : 'Unlink'
          }
        />
      </div>

      <GamePickerDialog
        open={pickerOpen}
        onSelect={(gameId) => {
          onLink(gameId);
          setPickerOpen(false);
        }}
        onClose={() => setPickerOpen(false)}
      />
    </section>
  );
};
