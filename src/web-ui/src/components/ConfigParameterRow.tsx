import React from 'react';
import type { ConfigurationParameter } from '../services/config';

export type ConfigParameterRowProps = {
  param: ConfigurationParameter;
  editValue?: string;
  isDirty?: boolean;
  onChange?: (name: string, value: string) => void;
  draggable?: boolean;
  onDragStart?: (e: React.DragEvent<HTMLDivElement>) => void;
  onDragOver?: (e: React.DragEvent<HTMLDivElement>) => void;
  onDragEnd?: (e: React.DragEvent<HTMLDivElement>) => void;
  onDrop?: (e: React.DragEvent<HTMLDivElement>) => void;
};

const sourceBadgeClass = (source: string) => {
  switch (source) {
    case 'Environment': return 'badge badge-env';
    case 'File': return 'badge badge-file';
    default: return 'badge badge-default';
  }
};

export const ConfigParameterRow: React.FC<ConfigParameterRowProps> = ({
  param, editValue, isDirty, onChange, draggable, onDragStart, onDragOver, onDragEnd, onDrop
}) => {
  const isReadOnly = param.source === 'Environment';
  const displayValue = param.isSecret ? '***' : String(param.value ?? '');

  return (
    <div
      className={`config-param-row${isDirty ? ' config-param-dirty' : ''}${isReadOnly ? ' config-param-readonly' : ''}`}
      draggable={draggable}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragEnd={onDragEnd}
      onDrop={onDrop}
    >
      {draggable && <span className="drag-handle" aria-label="Drag to reorder">⠿</span>}
      <span className="config-param-name">{param.name}</span>
      <span className={sourceBadgeClass(param.source)}>{param.source}</span>
      {onChange && !isReadOnly ? (
        <input
          className="config-param-input"
          type={param.isSecret ? 'password' : 'text'}
          value={editValue ?? displayValue}
          placeholder={param.isSecret ? '***' : ''}
          onChange={(e) => onChange(param.name, e.target.value)}
          aria-label={`Value for ${param.name}`}
        />
      ) : (
        <span className="config-param-value">{displayValue}</span>
      )}
    </div>
  );
};
