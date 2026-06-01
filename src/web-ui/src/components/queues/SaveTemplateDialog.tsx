import React, { useEffect, useState } from 'react';
import { ApiError } from '../../lib/api';

type SaveTemplateDialogProps = {
  open: boolean;
  /** Name of the template the queue was loaded from, pre-filled as a convenience. */
  originName?: string;
  /** Persists the template; should reject with ApiError(status 409) when the name already exists. */
  onSave: (name: string, overwrite: boolean) => Promise<void>;
  onClose: () => void;
};

const NAME_PATTERN = /^[A-Za-z0-9 _-]+$/;

const validateName = (raw: string): string | undefined => {
  const name = raw.trim();
  if (!name) return 'Name is required';
  if (name.length > 100) return 'Name must be 100 characters or fewer';
  if (!NAME_PATTERN.test(name)) return 'Name may contain only letters, digits, spaces, hyphens, and underscores';
  return undefined;
};

export const SaveTemplateDialog: React.FC<SaveTemplateDialogProps> = ({ open, originName, onSave, onClose }) => {
  const [name, setName] = useState(originName ?? '');
  const [error, setError] = useState<string | undefined>(undefined);
  const [confirmingOverwrite, setConfirmingOverwrite] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (open) {
      setName(originName ?? '');
      setError(undefined);
      setConfirmingOverwrite(false);
      setSaving(false);
    }
  }, [open, originName]);

  if (!open) return null;

  const persist = async (overwrite: boolean) => {
    setSaving(true);
    setError(undefined);
    try {
      await onSave(name.trim(), overwrite);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 409) {
        setConfirmingOverwrite(true);
      } else {
        setError(err instanceof Error ? err.message : 'Failed to save template');
      }
    } finally {
      setSaving(false);
    }
  };

  const handleSave = () => {
    const validation = validateName(name);
    if (validation) {
      setError(validation);
      return;
    }
    void persist(false);
  };

  return (
    <section className="queue-template-section" aria-label="Save template">
      <h4>Save as template</h4>
      {!confirmingOverwrite ? (
        <>
          <label htmlFor="template-name">Template name</label>
          <input
            id="template-name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                handleSave();
              }
            }}
          />
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="queue-template-section-actions">
            <button type="button" className="btn btn-secondary" onClick={onClose} disabled={saving}>Cancel</button>
            <button type="button" className="btn btn-primary" onClick={handleSave} disabled={saving}>Save</button>
          </div>
        </>
      ) : (
        <>
          <p>
            A template named <strong>{name.trim()}</strong> already exists. Overwrite it?
          </p>
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="queue-template-section-actions">
            <button type="button" className="btn btn-secondary" onClick={onClose} disabled={saving}>Cancel</button>
            <button type="button" className="btn btn-danger" onClick={() => void persist(true)} disabled={saving}>Overwrite</button>
          </div>
        </>
      )}
    </section>
  );
};
