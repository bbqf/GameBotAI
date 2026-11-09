import React, { useMemo, useState } from 'react';
import { useImageThumbnail } from '../useImageThumbnail';
import type { ParameterFieldProps } from './fieldProps';
import { SEARCH_THRESHOLD, limitsText, toggleListItem } from './fieldProps';

type TileProps = {
  imageId: string;
  selected: boolean;
  disabled?: boolean;
  onSelect: () => void;
  onKeyDown: (event: React.KeyboardEvent<HTMLButtonElement>) => void;
  tabIndex: number;
  large?: boolean;
  /** Toggle mode (feature 179): the tile is a check box, so several tiles can be selected. */
  toggle?: boolean;
};

/** One image tile: the preview loads when the tile scrolls into view, and the text name is always shown. */
export const ImageTile: React.FC<TileProps> = ({ imageId, selected, disabled, onSelect, onKeyDown, tabIndex, large, toggle }) => {
  const thumbnail = useImageThumbnail(imageId);
  return (
    <button
      type="button"
      role={toggle ? 'checkbox' : 'radio'}
      aria-checked={selected}
      aria-label={imageId}
      className={`image-tile${selected ? ' image-tile-selected' : ''}${large ? ' image-tile-large' : ''}`}
      disabled={disabled}
      tabIndex={tabIndex}
      ref={thumbnail.ref}
      onClick={onSelect}
      onKeyDown={onKeyDown}
    >
      {thumbnail.url ? (
        <img src={thumbnail.url} alt="" className="image-tile-preview" />
      ) : (
        <span className="image-tile-preview image-tile-placeholder" aria-hidden="true">
          {thumbnail.missing ? '?' : ''}
        </span>
      )}
      <span className="image-tile-name">{imageId}</span>
    </button>
  );
};

/**
 * The image picker of an image parameter (feature 168). It shows only the images that the author
 * allowed, with a preview and a text name. It selects with the mouse or with the arrow keys.
 */
export const ImagePickerField: React.FC<ParameterFieldProps> = (props) =>
  props.declaration.multiple === true ? <MultiImagePicker {...props} /> : <SingleImagePicker {...props} />;

/**
 * The image picker of a multi-value image parameter (feature 179). Each tile toggles. The selected tiles are
 * marked and numbered in the order of selection. The limits show as help text.
 */
const MultiImagePicker: React.FC<ParameterFieldProps> = ({ declaration, values, onChangeValues, disabled, ariaLabel }) => {
  const allowed = useMemo(() => declaration.allowedImages ?? [], [declaration.allowedImages]);
  const [search, setSearch] = useState('');
  const selected = values ?? [];
  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return term ? allowed.filter((id) => id.toLowerCase().includes(term)) : allowed;
  }, [allowed, search]);
  const gone = selected.filter((id) => !allowed.includes(id));
  const limits = limitsText(declaration);
  const toggle = (id: string) => onChangeValues?.(toggleListItem(selected, id));

  return (
    <div className="image-picker image-picker-multiple">
      {allowed.length > SEARCH_THRESHOLD && (
        <input
          type="search"
          className="image-picker-search"
          value={search}
          placeholder="Search images"
          aria-label={`Search images for ${declaration.label?.trim() || declaration.name}`}
          onChange={(e) => setSearch(e.target.value)}
        />
      )}
      <div role="group" aria-label={ariaLabel} className="image-picker-grid">
        {shown.map((id, index) => (
          <ImageTile
            key={id}
            imageId={id}
            toggle
            selected={selected.includes(id)}
            disabled={disabled}
            tabIndex={index === 0 ? 0 : -1}
            onSelect={() => toggle(id)}
            onKeyDown={(event) => {
              const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1
                : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
              if (step === 0) return;
              event.preventDefault();
              const next = event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('button')[(index + step + shown.length) % shown.length];
              next?.focus();
            }}
          />
        ))}
      </div>
      <p className="parameter-value-message parameter-list-count" aria-live="polite">
        {selected.length} selected{limits ? `. ${limits}` : ''}
      </p>
      {allowed.length === 0 && <p className="parameter-value-message">No image is allowed for this parameter. Ask the author to choose images.</p>}
      {gone.length > 0 && (
        <p className="parameter-value-message" role="alert">
          Image not found: {gone.join(', ')}. Select only the allowed images.
        </p>
      )}
    </div>
  );
};

const SingleImagePicker: React.FC<ParameterFieldProps> = ({ declaration, value, onChange, disabled, ariaLabel }) => {
  const allowed = useMemo(() => declaration.allowedImages ?? [], [declaration.allowedImages]);
  const [search, setSearch] = useState('');
  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return term ? allowed.filter((id) => id.toLowerCase().includes(term)) : allowed;
  }, [allowed, search]);

  const current = value ?? '';
  const notFound = current !== '' && !allowed.includes(current);

  const move = (index: number, delta: number) => {
    const next = shown[(index + delta + shown.length) % shown.length];
    if (next !== undefined) onChange(next);
  };

  const handleKey = (index: number) => (event: React.KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      move(index, 1);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      move(index, -1);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      onChange(shown[index]);
    }
  };

  return (
    <div className="image-picker">
      {allowed.length > SEARCH_THRESHOLD && (
        <input
          type="search"
          className="image-picker-search"
          value={search}
          placeholder="Search images"
          aria-label={`Search images for ${declaration.label?.trim() || declaration.name}`}
          onChange={(e) => setSearch(e.target.value)}
        />
      )}
      <div role="radiogroup" aria-label={ariaLabel} className="image-picker-grid">
        {shown.map((id, index) => (
          <ImageTile
            key={id}
            imageId={id}
            selected={id === current}
            disabled={disabled}
            tabIndex={id === current || (current === '' && index === 0) ? 0 : -1}
            onSelect={() => onChange(id)}
            onKeyDown={handleKey(index)}
          />
        ))}
      </div>
      {allowed.length === 0 && <p className="parameter-value-message">No image is allowed for this parameter. Ask the author to choose images.</p>}
      {notFound && (
        <p className="parameter-value-message" role="alert">
          Image not found: {current}. Select one of the allowed images.
        </p>
      )}
    </div>
  );
};
