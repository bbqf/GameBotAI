import React, { useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '../../lib/api';
import { CropResponse, cropImageFromCapture, fetchEmulatorScreenshot } from '../../services/images';

const MIN_SIZE = 16;

type Selection = { x: number; y: number; width: number; height: number };
type CaptureState = { captureId: string; url: string; naturalWidth: number; naturalHeight: number } | null;

type Point = { x: number; y: number };

export const EmulatorCaptureCropper: React.FC = () => {
  const [capture, setCapture] = useState<CaptureState>(null);
  const [selection, setSelection] = useState<Selection | null>(null);
  const [dragStart, setDragStart] = useState<Point | null>(null);
  const [lastSavedPath, setLastSavedPath] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [overwrite, setOverwrite] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [capturing, setCapturing] = useState(false);
  const [saving, setSaving] = useState(false);
  const imageRef = useRef<HTMLImageElement | null>(null);

  const resolveErrorCode = (payload: unknown): string | undefined => {
    const anyPayload = payload as any;
    if (!anyPayload) return undefined;
    if (typeof anyPayload.error === 'string') return anyPayload.error;
    if (typeof anyPayload.error?.code === 'string') return anyPayload.error.code;
    return undefined;
  };

  useEffect(() => {
    return () => {
      if (capture?.url) URL.revokeObjectURL(capture.url);
    };
  }, [capture?.url]);

  const sizeTooSmall = useMemo(() => {
    if (!selection) return false;
    return selection.width < MIN_SIZE || selection.height < MIN_SIZE;
  }, [selection]);

  const canSave = useMemo(() => {
    if (!capture || !selection) return false;
    if (sizeTooSmall) return false;
    return name.trim().length > 0;
  }, [capture, selection, name, sizeTooSmall]);

  const setNewCapture = (next: CaptureState) => {
    setLastSavedPath(null);
    if (capture?.url && capture.url !== next?.url) {
      URL.revokeObjectURL(capture.url);
    }
    setCapture(next);
    setSelection(null);
    setStatus(null);
    setError(null);
  };

  const clearCapture = () => {
    setCapture(null);
    setSelection(null);
    setStatus(null);
  };

  const handleCapture = async () => {
    setCapturing(true);
    setStatus(null);
    setError(null);
    try {
      const res = await fetchEmulatorScreenshot();
      const url = URL.createObjectURL(res.blob);
      setNewCapture({ captureId: res.captureId, url, naturalWidth: 0, naturalHeight: 0 });
    } catch (e: any) {
      if (e instanceof ApiError) {
        const code = resolveErrorCode(e.payload);
        if (code === 'emulator_unavailable' || e.status === 503) {
          setError('Emulator unavailable. Ensure it is running and retry capture.');
        } else {
          setError(e.message);
        }
      } else {
        setError(e?.message ?? 'Failed to capture emulator screenshot');
      }
    } finally {
      setCapturing(false);
    }
  };

  const updateSelectionFromClient = (clientX: number, clientY: number, origin: Point | null) => {
    if (!capture || !imageRef.current || !origin) return;
    const rect = imageRef.current.getBoundingClientRect();
    if (!rect.width || !rect.height || capture.naturalWidth === 0 || capture.naturalHeight === 0) return;
    const scaleX = capture.naturalWidth / rect.width;
    const scaleY = capture.naturalHeight / rect.height;
    const clampedX = Math.min(Math.max(clientX - rect.left, 0), rect.width);
    const clampedY = Math.min(Math.max(clientY - rect.top, 0), rect.height);
    const nx = clampedX * scaleX;
    const ny = clampedY * scaleY;
    const startX = Math.min(origin.x, nx);
    const startY = Math.min(origin.y, ny);
    const endX = Math.max(origin.x, nx);
    const endY = Math.max(origin.y, ny);
    setSelection({
      x: Math.round(startX),
      y: Math.round(startY),
      width: Math.round(endX - startX),
      height: Math.round(endY - startY)
    });
  };

  const handleMouseDown: React.MouseEventHandler<HTMLDivElement> = (e) => {
    if (!capture || !imageRef.current) return;
    const rect = imageRef.current.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    const scaleX = capture.naturalWidth / rect.width;
    const scaleY = capture.naturalHeight / rect.height;
    const start: Point = {
      x: Math.max(0, Math.min(capture.naturalWidth, (e.clientX - rect.left) * scaleX)),
      y: Math.max(0, Math.min(capture.naturalHeight, (e.clientY - rect.top) * scaleY))
    };
    setDragStart(start);
    setSelection({ x: Math.round(start.x), y: Math.round(start.y), width: 0, height: 0 });
  };

  const handleMouseMove: React.MouseEventHandler<HTMLDivElement> = (e) => {
    if (!dragStart) return;
    updateSelectionFromClient(e.clientX, e.clientY, dragStart);
  };

  const handleMouseUp: React.MouseEventHandler<HTMLDivElement> = (e) => {
    if (!dragStart) return;
    updateSelectionFromClient(e.clientX, e.clientY, dragStart);
    setDragStart(null);
  };

  const selectionStyle = useMemo(() => {
    if (!capture || !selection || !imageRef.current) return undefined;
    const rect = imageRef.current.getBoundingClientRect();
    if (!rect.width || !rect.height || capture.naturalWidth === 0 || capture.naturalHeight === 0) return undefined;
    const scaleX = rect.width / capture.naturalWidth;
    const scaleY = rect.height / capture.naturalHeight;
    return {
      left: `${selection.x * scaleX}px`,
      top: `${selection.y * scaleY}px`,
      width: `${selection.width * scaleX}px`,
      height: `${selection.height * scaleY}px`
    } as React.CSSProperties;
  }, [capture, selection]);

  const handleSave = async () => {
    if (!capture || !selection) {
      setError('Capture and selection are required');
      return;
    }
    if (selection.width < MIN_SIZE || selection.height < MIN_SIZE) {
      setError('Selection must be at least 16x16');
      return;
    }
    if (!name.trim()) {
      setError('Name is required');
      return;
    }

    setSaving(true);
    setStatus(null);
    setError(null);

    try {
      const payload: CropResponse = await cropImageFromCapture({
        name: name.trim(),
        overwrite,
        sourceCaptureId: capture.captureId,
        bounds: selection
      });
      setStatus(`Saved ${payload.fileName} to ${payload.storagePath}`);
      setLastSavedPath(payload.storagePath);
    } catch (e: any) {
      if (e instanceof ApiError) {
        const code = resolveErrorCode(e.payload);
        if (code === 'capture_missing' || e.status === 404) {
          setError('Capture expired. Capture again and retry.');
          clearCapture();
        } else if (code === 'bounds_out_of_range') {
          setError('Selection is outside the captured image. Adjust and retry.');
        } else if (e.status === 409) {
          setError(e.message || 'Name already exists. Enable overwrite to replace or choose another name.');
        } else if (e.status === 400) {
          setError(e.message || 'Invalid selection or request.');
        } else if (e.status === 503 || code === 'emulator_unavailable') {
          setError('Emulator unavailable. Retry after ensuring the emulator is running.');
        } else {
          setError(e.message || 'Failed to save crop');
        }
      } else {
        setError(e?.message ?? 'Failed to save crop');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="capture-panel" aria-label="Capture and crop emulator screenshot">
      <h3>Capture from emulator</h3>
      <div className="row">
        <button type="button" onClick={() => void handleCapture()} disabled={capturing}>
          {capturing ? 'Capturing…' : 'Capture emulator screenshot'}
        </button>
        {capture && capture.naturalWidth > 0 && (
          <div className="capture-meta">Captured {capture.naturalWidth}×{capture.naturalHeight}px</div>
        )}
      </div>
      {capture && (
        <div className="capture-viewer">
          <div
            className="capture-overlay"
            onMouseDown={handleMouseDown}
            onMouseMove={handleMouseMove}
            onMouseUp={handleMouseUp}
            data-testid="capture-overlay"
          >
            <img
              ref={imageRef}
              src={capture.url}
              alt="Emulator screenshot"
              onLoad={(e) => {
                const img = e.currentTarget;
                setCapture((prev) => prev ? { ...prev, naturalWidth: img.naturalWidth, naturalHeight: img.naturalHeight } : prev);
              }}
            />
            {selection && selectionStyle && (
              <div className="selection-rect" style={selectionStyle} aria-label="Selection rectangle" />
            )}
          </div>
        </div>
      )}
      <div className="row">
        <label htmlFor="crop-name">Image name *</label>
        <input
          id="crop-name"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="enter image name"
        />
      </div>
      <div className="row">
        <label htmlFor="overwrite">
          <input
            id="overwrite"
            type="checkbox"
            checked={overwrite}
            onChange={(e) => setOverwrite(e.target.checked)}
          />
          Allow overwrite
        </label>
      </div>
      {selection && (
        <div className="form-hint">Selection: {selection.width}×{selection.height} @ ({selection.x}, {selection.y})</div>
      )}
      <div className="form-hint">Min crop size is 16×16. If a capture expires, take a new screenshot and retry.</div>
      {lastSavedPath && <div className="form-hint">Last saved to: {lastSavedPath}</div>}
      {sizeTooSmall && <div className="form-error" role="alert">Selection must be at least 16x16</div>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {status && <div className="form-hint" role="status">{status}</div>}
      <div className="row">
        <button type="button" onClick={() => void handleSave()} disabled={!canSave || saving}>
          {saving ? 'Saving…' : 'Save crop'}
        </button>
        <button type="button" onClick={() => setSelection(null)} disabled={!capture}>
          Clear selection
        </button>
      </div>
    </section>
  );
};
