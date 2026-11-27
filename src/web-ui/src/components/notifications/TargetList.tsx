import React from 'react';
import type { NotificationTargetDto } from '../../services/notifications';
import { useLayoutMode, type LayoutMode } from '../../hooks/useLayoutMode';
import { ListView, StatusChip, type ListColumn, type ListState, type RowActions } from '../ui';

type TargetListProps = {
  targets: NotificationTargetDto[];
  state?: ListState;
  onEdit: (target: NotificationTargetDto) => void;
  /** Asks to delete a target. The page shows the confirmation dialog. */
  onDelete: (target: NotificationTargetDto) => void;
  /** Sends a test message to a saved target. */
  onTest?: (id: string) => void;
  /** The id of the target whose test call runs. Its test icon is off. */
  testingId?: string | null;
  mode?: LayoutMode;
  /** Desktop: show only Name, Enabled, and Actions (while the side panel is open). */
  compact?: boolean;
  /** The id of the target that is open in the side panel. Its row gets a mark. */
  selectedId?: string | null;
};

const typeText = (target: NotificationTargetDto): string => (target.type === 'telegram' ? 'Telegram' : target.type);
const chatIdText = (target: NotificationTargetDto): string => target.settings?.chatId ?? '';
const tokenText = (target: NotificationTargetDto): string => {
  if (!target.hasSecret) return 'Not set';
  return target.secretHint ? `Saved (${target.secretHint})` : 'Saved';
};
const chip = (target: NotificationTargetDto) => (
  <StatusChip state={target.enabled ? 'done' : 'stopped'} text={target.enabled ? 'On' : 'Off'} />
);

const columns: ListColumn<NotificationTargetDto>[] = [
  { id: 'name', header: 'Name', width: 'flex', render: (t) => t.name, text: (t) => t.name },
  { id: 'type', header: 'Type', width: 120, render: typeText, text: typeText, secondary: true },
  { id: 'enabled', header: 'Enabled', width: 132, render: chip },
  {
    id: 'chatId',
    header: 'Chat ID',
    width: 200,
    secondary: true,
    render: (t) => <span className="ui-mono">{chatIdText(t)}</span>,
    text: chatIdText
  },
  { id: 'token', header: 'Bot token', width: 160, render: tokenText, text: tokenText, secondary: true }
];

/** The saved notification targets in the shared list: a test icon, an edit icon, and a menu with Delete (feature 203). */
export const TargetList: React.FC<TargetListProps> = ({ targets, state = 'ready', onEdit, onDelete, onTest, testingId, mode, compact, selectedId }) => {
  const layout = useLayoutMode();
  const phone = (mode ?? layout) === 'phone';

  const rowActions = (target: NotificationTargetDto): RowActions => {
    const test = {
      id: 'test',
      label: 'Send test message',
      ariaLabel: `Send test message ${target.name}`,
      icon: 'send' as const,
      disabled: testingId === target.id || !onTest,
      onSelect: () => onTest?.(target.id)
    };
    const edit = { id: 'edit', label: 'Edit', ariaLabel: `Edit ${target.name}`, icon: 'edit' as const, onSelect: () => onEdit(target) };
    const remove = {
      id: 'delete',
      label: 'Delete',
      ariaLabel: `Delete ${target.name}`,
      icon: 'delete' as const,
      tone: 'danger' as const,
      onSelect: () => onDelete(target)
    };
    return phone ? { visible: [test], menu: [edit, remove] } : { visible: [test, edit], menu: [remove] };
  };

  return (
    <ListView
      ariaLabel="Notification targets"
      tableClassName="notification-target-list"
      columns={columns}
      rows={targets}
      getRowKey={(t) => t.id}
      onRowClick={onEdit}
      rowActions={rowActions}
      menuLabel={(t) => `More actions for ${t.name}`}
      actionsColumn={{ header: 'Actions', width: 148 }}
      state={state}
      mode={mode}
      compact={compact}
      selectedRowKey={selectedId ?? undefined}
      rowTestId={(t) => `target-row-${t.id}`}
      phone={{
        title: (t) => t.name,
        chip,
        meta: (t) => (chatIdText(t) ? `${typeText(t)} · ${chatIdText(t)}` : typeText(t))
      }}
    />
  );
};
