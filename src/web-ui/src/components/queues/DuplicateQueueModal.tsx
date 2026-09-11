import React, { useEffect, useState } from 'react';

type DuplicateQueueModalProps = {
  open: boolean;
  sourceName: string;
  suggestedName: string;
  error?: string;
  onConfirm: (name: string) => void;
  onCancel: () => void;
};

/**
 * Prompts for the duplicate's name (feature 083). Mirrors ConfirmDeleteModal's modal structure.
 * The confirm button stays disabled while the name is empty or unchanged from the source, since
 * the backend rejects both — catching it here avoids an avoidable round-trip.
 */
export const DuplicateQueueModal: React.FC<DuplicateQueueModalProps> = ({
  open,
  sourceName,
  suggestedName,
  error,
  onConfirm,
  onCancel,
}) => {
  const [name, setName] = useState(suggestedName);

  useEffect(() => {
    if (open) setName(suggestedName);
  }, [open, suggestedName]);

  if (!open) return null;

  const trimmed = name.trim();
  const invalid = trimmed.length === 0 || trimmed === sourceName.trim();

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Duplicate Queue">
      <div className="modal">
        <h3>Duplicate Queue</h3>
        <p>
          Create a copy of <strong>{sourceName}</strong> with the same configuration, template,
          game, and entries, under a new name.
        </p>
        <label htmlFor="duplicate-queue-name">New queue name</label>
        <input
          id="duplicate-queue-name"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />
        {invalid && trimmed.length > 0 && (
          <div className="form-hint" role="status">Name must differ from &quot;{sourceName}&quot;.</div>
        )}
        {error && <div className="form-error" role="alert">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="btn btn-secondary" onClick={onCancel}>
            Cancel
          </button>
          <button type="button" disabled={invalid} onClick={() => onConfirm(trimmed)}>
            Duplicate
          </button>
        </div>
      </div>
    </div>
  );
};
