import React, { useEffect, useMemo, useState } from 'react';
import { listImageItems, type ImageListItem } from '../../services/images';
import { useImageThumbnail } from './useImageThumbnail';

export const MAX_ALLOWED_IMAGES = 50;

export type AllowedImagesEditorProps = {
  allowed: string[];
  onChange: (allowed: string[]) => void;
  disabled?: boolean;
};

const ownerName = (item: ImageListItem): string =>
  item.owner?.kind === 'gamepack' ? `Pack: ${item.owner.name ?? 'unknown'}` : 'Your images';

const AllowedThumb: React.FC<{ imageId: string; onRemove: () => void; disabled?: boolean }> = ({ imageId, onRemove, disabled }) => {
  const thumbnail = useImageThumbnail(imageId);
  return (
    <li className="allowed-image" ref={thumbnail.ref as React.Ref<HTMLLIElement>}>
      {thumbnail.url ? <img src={thumbnail.url} alt="" className="image-tile-preview" /> : <span className="image-tile-preview image-tile-placeholder" aria-hidden="true">{thumbnail.missing ? '?' : ''}</span>}
      <span className="image-tile-name">{imageId}</span>
      <button type="button" disabled={disabled} aria-label={`Remove image ${imageId}`} onClick={onRemove}>
        Remove
      </button>
    </li>
  );
};

/**
 * The author lists the images that a person may select (feature 168). The "Choose images" dialog lists
 * all stored images grouped by owner, with a search box and check boxes. One to 50 images are allowed.
 */
export const AllowedImagesEditor: React.FC<AllowedImagesEditorProps> = ({ allowed, onChange, disabled }) => {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<ImageListItem[]>([]);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<string[]>(allowed);

  useEffect(() => {
    if (!open) return;
    setDraft(allowed);
    setError(null);
    listImageItems().then(setItems).catch(() => setError('The image list did not load. Close the dialog and try again.'));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const groups = useMemo(() => {
    const term = search.trim().toLowerCase();
    const map = new Map<string, ImageListItem[]>();
    items
      .filter((item) => !term || item.id.toLowerCase().includes(term))
      .forEach((item) => {
        const key = ownerName(item);
        map.set(key, [...(map.get(key) ?? []), item]);
      });
    return Array.from(map.entries());
  }, [items, search]);

  const toggle = (id: string) =>
    setDraft((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));

  const apply = () => {
    onChange(draft);
    setOpen(false);
  };

  return (
    <div className="allowed-images-editor">
      {allowed.length === 0 ? (
        <p className="parameter-declaration-hint">No image is allowed yet. Choose one or more images.</p>
      ) : (
        <ul className="allowed-images-list">
          {allowed.map((id) => (
            <AllowedThumb key={id} imageId={id} disabled={disabled} onRemove={() => onChange(allowed.filter((x) => x !== id))} />
          ))}
        </ul>
      )}
      <button type="button" disabled={disabled} onClick={() => setOpen(true)}>
        Choose images
      </button>

      {open && (
        <div role="dialog" aria-modal="true" aria-label="Choose images" className="allowed-images-dialog">
          <input
            type="search"
            value={search}
            placeholder="Search images"
            aria-label="Search stored images"
            onChange={(e) => setSearch(e.target.value)}
          />
          {error && (
            <p className="parameter-declaration-error" role="alert">
              {error}
            </p>
          )}
          {groups.map(([group, groupItems]) => (
            <fieldset key={group}>
              <legend>{group}</legend>
              {groupItems.map((item) => (
                <label key={item.id} className="allowed-images-option">
                  <input
                    type="checkbox"
                    checked={draft.includes(item.id)}
                    disabled={!draft.includes(item.id) && draft.length >= MAX_ALLOWED_IMAGES}
                    onChange={() => toggle(item.id)}
                  />
                  <span>{item.id}</span>
                </label>
              ))}
            </fieldset>
          ))}
          {draft.length >= MAX_ALLOWED_IMAGES && (
            <p className="parameter-declaration-hint">A parameter allows up to {MAX_ALLOWED_IMAGES} images. Clear one to add another.</p>
          )}
          <div className="allowed-images-actions">
            <button type="button" disabled={draft.length === 0} onClick={apply}>
              Use {draft.length} image(s)
            </button>
            <button type="button" onClick={() => setOpen(false)}>
              Cancel
            </button>
          </div>
          {draft.length === 0 && <p className="parameter-declaration-hint">Choose at least one image.</p>}
        </div>
      )}
    </div>
  );
};
