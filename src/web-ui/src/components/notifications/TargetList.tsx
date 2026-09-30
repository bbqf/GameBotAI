import React, { useState } from 'react';
import { ConfirmDeleteModal } from '../ConfirmDeleteModal';
import type { NotificationTargetDto } from '../../services/notifications';

type TargetListProps = {
  targets: NotificationTargetDto[];
  onEdit: (target: NotificationTargetDto) => void;
  onDelete: (id: string) => void;
  /** Sends a test message to a saved target. */
  onTest?: (id: string) => void;
};

/** The saved notification targets, with Edit, Delete (with a confirmation) and test actions (feature 120). */
export const TargetList: React.FC<TargetListProps> = ({ targets, onEdit, onDelete, onTest }) => {
  const [pendingDelete, setPendingDelete] = useState<NotificationTargetDto | null>(null);

  if (targets.length === 0) {
    return <p className="form-hint">No target is saved. Follow the guide and add one.</p>;
  }

  return (
    <>
      <table className="notification-target-list">
        <thead>
          <tr>
            <th>Name</th>
            <th>Type</th>
            <th>Enabled</th>
            <th>Chat ID</th>
            <th>Bot token</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {targets.map((target) => (
            <tr key={target.id}>
              <td>{target.name}</td>
              <td>{target.type}</td>
              <td>{target.enabled ? 'Yes' : 'No'}</td>
              <td>{target.settings?.chatId ?? ''}</td>
              <td>{target.hasSecret ? (target.secretHint ?? 'Saved') : 'Not set'}</td>
              <td>
                <button type="button" className="btn btn-secondary" onClick={() => onEdit(target)}>
                  Edit
                </button>{' '}
                {onTest && (
                  <>
                    <button type="button" className="btn btn-secondary" onClick={() => onTest(target.id)}>
                      Send test message
                    </button>{' '}
                  </>
                )}
                <button type="button" className="btn btn-danger" onClick={() => setPendingDelete(target)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <ConfirmDeleteModal
        open={pendingDelete !== null}
        title="Delete target"
        itemName={pendingDelete?.name}
        message="The target gets no more messages."
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => {
          const id = pendingDelete?.id;
          setPendingDelete(null);
          if (id) onDelete(id);
        }}
      />
    </>
  );
};
