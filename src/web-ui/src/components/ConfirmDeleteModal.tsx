import React from 'react';

type ConfirmDeleteModalProps = {
  open: boolean;
  title?: string;
  itemName?: string;
  message?: string;
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
        <div className="modal-actions">
          <button className="btn btn-secondary" onClick={onCancel} autoFocus>
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
