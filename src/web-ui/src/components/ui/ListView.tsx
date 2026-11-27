import React from 'react';
import { useLayoutMode, type LayoutMode } from '../../hooks/useLayoutMode';
import { Button } from './Button';
import { IconButton } from './IconButton';
import { RowActionMenu } from './RowActionMenu';
import type { ListColumn, ListState, RowAction, RowActions } from './types';

export type ListViewPhone<T> = {
  /** Line 1 of a phone row. */
  title: (row: T) => string;
  /** The chip on line 2. */
  chip?: (row: T) => React.ReactNode;
  /** The text after the chip on line 2. */
  meta?: (row: T) => React.ReactNode;
};

export type ListViewProps<T> = {
  ariaLabel: string;
  columns: ListColumn<T>[];
  rows: T[];
  getRowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  rowActions?: (row: T) => RowActions;
  /** An alternative to `rowActions` for a caller that needs a hook for each row (desktop). */
  renderActions?: (row: T) => React.ReactNode;
  /** An alternative to `rowActions` for a caller that needs a hook for each row (phone). */
  renderPhoneActions?: (row: T) => React.ReactNode;
  /** The accessible name of the "more" button. The default is "More actions". */
  menuLabel?: (row: T) => string;
  /** The accessible name of the open button. */
  openLabel?: (row: T) => string;
  actionsColumn?: { header: string; width: number };
  state?: ListState;
  phone?: ListViewPhone<T>;
  /** The default comes from the window size. */
  mode?: LayoutMode;
  /**
   * Desktop only. When true, the list does not show the columns with `secondary: true`. Use it while a side panel
   * makes the page narrow, so that all rows and their actions stay visible with no sideways scroll (rule D2).
   */
  compact?: boolean;
  /** Desktop only. The key of the row that is open in a side panel. The row gets a mark (rule D2). */
  selectedRowKey?: string;
  tableClassName?: string;
  rowClassName?: string;
  rowTestId?: (row: T) => string;
};

/** The width that the flexible column keeps as a minimum, in px. */
const FLEX_MIN_WIDTH = 160;
const DEFAULT_ACTIONS_COLUMN = { header: 'Actions', width: 184 };

const cx = (...names: Array<string | false | undefined>) => names.filter(Boolean).join(' ');

const actionButton = (action: RowAction, size: 'default' | 'touch') => (
  <IconButton
    key={action.id}
    icon={action.icon}
    label={action.ariaLabel ?? action.label}
    title={action.label}
    tone={action.tone}
    size={size}
    disabled={action.disabled}
    onClick={action.onSelect}
  />
);

/** One list for all pages: a table on the desktop, a list of two-line rows on the phone. */
export function ListView<T>(props: ListViewProps<T>): React.ReactElement {
  const layout = useLayoutMode();
  const mode = props.mode ?? layout;
  return mode === 'phone' ? <PhoneList {...props} /> : <DesktopList {...props} />;
}

const StateMessage: React.FC<{ state: Exclude<ListState, 'ready'> }> = ({ state }) => {
  if (state.kind === 'loading') return <span role="status">Loading…</span>;
  if (state.kind === 'empty') return <span>{state.text ?? 'No items.'}</span>;
  return (
    <span className="ui-list__error">
      <span role="alert">{state.text}</span>
      {state.onRetry && <Button variant="secondary" onClick={state.onRetry}>Try again</Button>}
    </span>
  );
};

function DesktopList<T>({
  ariaLabel,
  columns: allColumns,
  compact = false,
  selectedRowKey,
  rows,
  getRowKey,
  onRowClick,
  rowActions,
  renderActions,
  menuLabel,
  openLabel,
  actionsColumn,
  state = 'ready',
  tableClassName,
  rowClassName,
  rowTestId,
}: ListViewProps<T>): React.ReactElement {
  const columns = compact ? allColumns.filter((c) => !c.secondary) : allColumns;
  const hasActions = Boolean(rowActions || renderActions);
  const actions = actionsColumn ?? DEFAULT_ACTIONS_COLUMN;
  const fixedWidth = columns.reduce((sum, c) => sum + (c.width === 'flex' ? 0 : c.width), 0) + (hasActions ? actions.width : 0);
  const columnCount = columns.length + (hasActions ? 1 : 0);

  const renderRowActions = (row: T) => {
    if (renderActions) return renderActions(row);
    const set = rowActions ? rowActions(row) : { visible: [] };
    return (
      <>
        {set.visible.map((a) => actionButton(a, 'default'))}
        {set.menu && set.menu.length > 0 && <RowActionMenu label={menuLabel ? menuLabel(row) : 'More actions'} actions={set.menu} />}
      </>
    );
  };

  return (
    <div className="ui-list">
      <table className={cx('ui-list__table', tableClassName)} aria-label={ariaLabel} style={{ minWidth: fixedWidth + FLEX_MIN_WIDTH }}>
        <colgroup>
          {columns.map((c) => (
            <col key={c.id} style={c.width === 'flex' ? undefined : { width: `${c.width}px` }} />
          ))}
          {hasActions && <col style={{ width: `${actions.width}px` }} />}
        </colgroup>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c.id} scope="col" className={c.align === 'end' ? 'ui-list__cell--end' : undefined}>{c.header}</th>
            ))}
            {hasActions && <th scope="col" className="ui-list__cell--end">{actions.header}</th>}
          </tr>
        </thead>
        <tbody>
          {state !== 'ready' ? (
            <tr className="ui-list__state">
              <td colSpan={columnCount}><StateMessage state={state} /></td>
            </tr>
          ) : (
            rows.map((row) => (
              // The keyboard path of a row is the open button in its first cell, so the row needs no key handler.
              // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-noninteractive-element-interactions
              <tr
                key={getRowKey(row)}
                className={cx(
                  'ui-list__row',
                  onRowClick && 'ui-list__row--clickable',
                  selectedRowKey === getRowKey(row) && 'ui-list__row--selected',
                  rowClassName
                )}
                aria-selected={selectedRowKey !== undefined ? selectedRowKey === getRowKey(row) : undefined}
                data-testid={rowTestId?.(row)}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
              >
                {columns.map((c, index) => {
                  const text = c.text ? c.text(row) : undefined;
                  const content = c.text && text === '' ? '—' : c.render(row);
                  return (
                    <td
                      key={c.id}
                      className={c.align === 'end' ? 'ui-list__cell--end' : undefined}
                      title={text || undefined}
                      data-testid={c.cellTestId}
                    >
                      {index === 0 && onRowClick ? (
                        <button
                          type="button"
                          className="ui-list__open"
                          aria-label={openLabel?.(row)}
                          onClick={(event) => {
                            event.stopPropagation();
                            onRowClick(row);
                          }}
                        >
                          {content}
                        </button>
                      ) : (
                        content
                      )}
                    </td>
                  );
                })}
                {hasActions && (
                  // A click in the actions cell must not open the row.
                  // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-noninteractive-element-interactions
                  <td className="ui-list__actions" onClick={(event) => event.stopPropagation()}>
                    <span className="ui-list__actions-inner">{renderRowActions(row)}</span>
                  </td>
                )}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

function PhoneList<T>({
  ariaLabel,
  columns,
  rows,
  getRowKey,
  onRowClick,
  rowActions,
  renderPhoneActions,
  menuLabel,
  openLabel,
  state = 'ready',
  phone,
  rowClassName,
  rowTestId,
}: ListViewProps<T>): React.ReactElement {
  const titleOf = (row: T): string => (phone ? phone.title(row) : (columns[0]?.text?.(row) ?? ''));

  const renderRowActions = (row: T) => {
    if (renderPhoneActions) return renderPhoneActions(row);
    if (!rowActions) return null;
    const set = rowActions(row);
    const main = set.visible[0];
    return (
      <>
        {main && actionButton(main, 'touch')}
        {set.menu && set.menu.length > 0 && <RowActionMenu size="touch" label={menuLabel ? menuLabel(row) : 'More actions'} actions={set.menu} />}
      </>
    );
  };

  return (
    <ul className="ui-list__phone" aria-label={ariaLabel}>
      {state !== 'ready' ? (
        <li className="ui-list__state"><StateMessage state={state} /></li>
      ) : (
        rows.map((row) => {
          const title = titleOf(row);
          return (
            <li key={getRowKey(row)} className={cx('ui-list__phone-row', rowClassName)} data-testid={rowTestId?.(row)}>
              <button
                type="button"
                className="ui-list__phone-open"
                aria-label={openLabel ? openLabel(row) : `Open ${title}`}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
              >
                <span className="ui-list__phone-title" title={title}>{title}</span>
                <span className="ui-list__phone-line">
                  {phone?.chip?.(row)}
                  {phone?.meta && <span className="ui-list__phone-meta">{phone.meta(row)}</span>}
                </span>
              </button>
              <span className="ui-list__phone-actions">{renderRowActions(row)}</span>
            </li>
          );
        })
      )}
    </ul>
  );
}