import React from 'react';
import type { NotificationTargetDto } from '../../services/notifications';
import type { LayoutMode } from '../../hooks/useLayoutMode';
import { SidePanel } from '../ui';
import { TargetForm } from './TargetForm';

type TargetEditorProps = {
  /** null = closed, 'new' = add form, otherwise the target to edit. */
  editing: NotificationTargetDto | 'new' | null;
  onSaved: (saved: NotificationTargetDto) => void;
  onClose: () => void;
  onDelete: (target: NotificationTargetDto) => void;
  mode?: LayoutMode;
};

/** The target form in a side panel (desktop) or on a full page (phone). */
export const TargetEditor: React.FC<TargetEditorProps> = ({ editing, onSaved, onClose, onDelete, mode }) => (
  <SidePanel
    open={editing !== null}
    title={editing === 'new' || editing === null ? 'Add target' : 'Edit target'}
    testId="target-editor"
    mode={mode}
    onClose={onClose}
  >
    <TargetForm
      target={editing === 'new' || editing === null ? null : editing}
      onSaved={onSaved}
      onCancel={onClose}
      onDelete={onDelete}
    />
  </SidePanel>
);
