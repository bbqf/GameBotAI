import React, { useEffect, useMemo, useRef, useState } from 'react';
import { listImages } from '../../services/images';
import { ImageThumbnail } from './ImageThumbnail';
import './ImageSelectorDropdown.css';

/**
 * Props for the ImageSelectorDropdown component.
 *
 * @prop id - Optional id for the trigger button.
 * @prop label - Optional label rendered above the trigger.
 * @prop value - Current image ID; empty string means unset.
 * @prop onChange - Called with the selected image ID, or '' when cleared.
 * @prop required - Suppresses the clear button. Parent is responsible for wiring a validation error
 *   when onStaleChange(true) fires (e.g. LOC-01 primitive tap image must block form save).
 * @prop disabled - Disables the trigger and all interactions.
 * @prop error - External validation message rendered as role="alert". Distinct from the internal
 *   stale warning that appears when value is absent from the fetched library.
 * @prop onStaleChange - Called with true when value is non-empty and absent from the fetched list.
 *   Called with false when the value is resolved (present in list) or is empty.
 */
export type ImageSelectorDropdownProps = {
  id?: string;
  label?: string;
  value: string;
  onChange: (id: string) => void;
  required?: boolean;
  disabled?: boolean;
  error?: string;
  onStaleChange?: (isStale: boolean) => void;
  'data-testid'?: string;
};

type UseImageListResult = {
  images: string[];
  loading: boolean;
  error: string | null;
  retry: () => void;
};

function useImageList(active: boolean): UseImageListResult {
  const [images, setImages] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    if (!active) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    listImages()
      .then((ids) => {
        if (cancelled) return;
        setImages(ids);
        setLoading(false);
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setError(e instanceof Error ? e.message : 'Failed to load images');
        setLoading(false);
      });
    return () => { cancelled = true; };
  }, [active, tick]);

  return { images, loading, error, retry: () => setTick((t) => t + 1) };
}

export const ImageSelectorDropdown: React.FC<ImageSelectorDropdownProps> = ({
  id, label, value, onChange, required, disabled, error, onStaleChange,
  'data-testid': dataTestId,
}) => {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const { images, loading, error: fetchError, retry } = useImageList(open);

  useEffect(() => {
    if (!open || loading) return;
    if (!value) { onStaleChange?.(false); return; }
    onStaleChange?.(!images.includes(value));
  }, [open, loading, images, value, onStaleChange]);

  useEffect(() => {
    if (!open) return;
    const onMouseDown = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onMouseDown);
    return () => document.removeEventListener('mousedown', onMouseDown);
  }, [open]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? images.filter((img) => img.toLowerCase().includes(q)) : images;
  }, [images, query]);

  const isStale = open && !loading && !!value && !images.includes(value);
  const errorDescId = error && id ? `${id}-error` : undefined;

  const handleSelect = (imgId: string) => { onChange(imgId); setOpen(false); setQuery(''); };
  const handleClear = () => { onChange(''); setOpen(false); onStaleChange?.(false); };
  const toggleOpen = () => { if (!disabled) { setOpen((o) => !o); setQuery(''); } };

  return (
    <div className="image-selector" ref={containerRef}>
      {label && <label htmlFor={id}>{label}</label>}
      <button
        id={id}
        type="button"
        className="image-selector__trigger"
        data-testid={dataTestId ?? 'image-selector-trigger'}
        disabled={disabled}
        aria-expanded={open}
        aria-describedby={errorDescId}
        onClick={toggleOpen}
      >
        {value
          ? <><ImageThumbnail imageId={value} className="image-selector__option-thumb" /><span>{value}</span></>
          : <span className="image-selector__placeholder">Select image…</span>}
      </button>
      {open && (
        <div className="image-selector__panel" data-testid="image-selector-panel">
          <input
            className="image-selector__search"
            type="text"
            placeholder="Search…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          {loading && <div className="image-selector__loading">Loading…</div>}
          {!loading && fetchError && (
            <div className="image-selector__error">
              {fetchError}
              <button type="button" onClick={retry}>Retry</button>
            </div>
          )}
          {!loading && !fetchError && filtered.length === 0 && (
            <div className="image-selector__empty">No images available</div>
          )}
          {!loading && !fetchError && filtered.map((imgId) => (
            <button
              key={imgId}
              type="button"
              className="image-selector__option"
              data-testid="image-selector-option"
              onClick={() => handleSelect(imgId)}
            >
              <span className="image-selector__option-thumb"><ImageThumbnail imageId={imgId} /></span>
              <span className="image-selector__option-label">{imgId}</span>
            </button>
          ))}
          {!required && value && (
            <button
              type="button"
              className="image-selector__clear"
              data-testid="image-selector-clear"
              onClick={handleClear}
            >
              Clear
            </button>
          )}
        </div>
      )}
      {isStale && <div className="image-selector__stale-warning">Image &quot;{value}&quot; not found in library</div>}
      {error && <div id={errorDescId} className="field-error" role="alert">{error}</div>}
    </div>
  );
};
