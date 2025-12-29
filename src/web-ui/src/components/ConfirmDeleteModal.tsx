import React from 'react';

type ConfirmDeleteModalProps = {
  open: boolean;
  title?: string;
  itemName?: string;
  message?: string;
  references?: Record<string, Array<{ id: string; name: string }>>;
  confirmText?: string;
  cancelText?: string;
  onConfirm: () => void;
  onCancel: () => void;
};

export const ConfirmDeleteModal: React.FC<ConfirmDeleteModalProps> = ({
  open,
  title = 'Confirm Delete',
  itemName,
  message = 'This action cannot be undone.',
  references,
  confirmText = 'Delete',
  cancelText = 'Cancel',
  onConfirm,
  onCancel,
}) => {
  if (!open) return null;
  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label={title}>
      <div className="modal">
        <h3>{title}</h3>
        <p>
          {itemName ? (
            <>
              Are you sure you want to delete <strong>{itemName}</strong>? {message}
            </>
          ) : (
            message
          )}
        </p>
        {references && Object.keys(references).length > 0 && (
          <div className="references">
            <strong>Referenced by:</strong>
            {Object.entries(references).map(([key, items]) => (
              <div key={key} className="reference-group">
                <span className="reference-key">{key} ({items.length})</span>
                <ul>
                  {items.map((r) => (
                    <li key={r.id}>
                      {r.name} <span className="muted">[{r.id}]</span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
        <div className="modal-actions">
          <button className="btn btn-secondary" onClick={onCancel}>
            {cancelText}
          </button>
          <button className="btn btn-danger" onClick={onConfirm}>
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  );
};
