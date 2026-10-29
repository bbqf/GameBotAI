import React, { useMemo, useState } from 'react';
import { useImageThumbnail } from '../useImageThumbnail';
import type { ParameterFieldProps } from './fieldProps';
import { SEARCH_THRESHOLD } from './fieldProps';

type TileProps = {
  imageId: string;
  selected: boolean;
  disabled?: boolean;
  onSelect: () => void;
  onKeyDown: (event: React.KeyboardEvent<HTMLButtonElement>) => void;
  tabIndex: number;
  large?: boolean;
};

/** One image tile: the preview loads when the tile scrolls into view, and the text name is always shown. */
export const ImageTile: React.FC<TileProps> = ({ imageId, selected, disabled, onSelect, onKeyDown, tabIndex, large }) => {
  const thumbnail = useImageThumbnail(imageId);
  return (
    <button
      type="button"
      role="radio"
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
export const ImagePickerField: React.FC<ParameterFieldProps> = ({ declaration, value, onChange, disabled, ariaLabel }) => {
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
