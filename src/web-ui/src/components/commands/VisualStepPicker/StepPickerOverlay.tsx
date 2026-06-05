import React, { useRef } from 'react';
import type { ImageMatchResult, PickerStatus } from '../../../types/picker';

type Point = { x: number; y: number };

export type GestureHandler = (start: Point, end: Point, durationMs: number) => void;

type StepPickerOverlayProps = {
  screenshotUrl: string;
  naturalWidth: number;
  naturalHeight: number;
  matches: ImageMatchResult[];
  status: PickerStatus;
  onGesture: GestureHandler;
  onNaturalSizeReady?: (width: number, height: number) => void;
};

type GestureStart = { x: number; y: number; timestamp: number };

export const StepPickerOverlay: React.FC<StepPickerOverlayProps> = ({
  screenshotUrl,
  naturalWidth,
  naturalHeight,
  matches,
  status,
  onGesture,
  onNaturalSizeReady,
}) => {
  const imgRef = useRef<HTMLImageElement | null>(null);
  const gestureStartRef = useRef<GestureStart | null>(null);

  const toNatural = (clientX: number, clientY: number): Point | null => {
    if (!imgRef.current || !naturalWidth || !naturalHeight) return null;
    const rect = imgRef.current.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;
    return {
      x: Math.round(((clientX - rect.left) / rect.width) * naturalWidth),
      y: Math.round(((clientY - rect.top) / rect.height) * naturalHeight),
    };
  };

  const handleMouseDown: React.MouseEventHandler = (e) => {
    if (status !== 'ready') return;
    const pt = toNatural(e.clientX, e.clientY);
    if (!pt) return;
    gestureStartRef.current = { x: pt.x, y: pt.y, timestamp: e.timeStamp };
  };

  const handleMouseUp: React.MouseEventHandler = (e) => {
    if (status !== 'ready') return;
    const start = gestureStartRef.current;
    gestureStartRef.current = null;
    if (!start) return;
    const end = toNatural(e.clientX, e.clientY);
    if (!end) return;
    const durationMs = Math.round(e.timeStamp - start.timestamp);
    onGesture({ x: start.x, y: start.y }, end, durationMs);
  };

  const renderOverlays = () => {
    if (!naturalWidth || !naturalHeight) return null;
    return (
      <svg
        className="step-picker-overlay__svg"
        viewBox={`0 0 ${naturalWidth} ${naturalHeight}`}
        style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', pointerEvents: 'none' }}
        aria-hidden="true"
      >
        {matches.map((m, i) => (
          <g key={`${m.imageId}-${i}`}>
            <rect
              x={m.x}
              y={m.y}
              width={m.width}
              height={m.height}
              fill="rgba(0,180,120,0.15)"
              stroke="rgba(0,220,140,0.9)"
              strokeWidth={2}
            />
            <text
              x={m.x + 3}
              y={m.y + 14}
              fontSize={12}
              fill="rgba(0,255,160,0.95)"
              style={{ fontFamily: 'monospace' }}
            >
              {m.imageName}
            </text>
          </g>
        ))}
      </svg>
    );
  };

  return (
    <div
      className="step-picker-overlay"
      style={{ position: 'relative', display: 'inline-block', cursor: status === 'ready' ? 'crosshair' : 'default', userSelect: 'none' }}
      onMouseDown={handleMouseDown}
      onMouseUp={handleMouseUp}
      onDragStart={(e) => e.preventDefault()}
    >
      <img
        ref={imgRef}
        src={screenshotUrl}
        alt="Emulator screenshot"
        draggable={false}
        style={{ display: 'block', maxWidth: '100%' }}
        onLoad={(e) => {
          const img = e.currentTarget;
          onNaturalSizeReady?.(img.naturalWidth, img.naturalHeight);
        }}
      />
      {renderOverlays()}
      {status === 'loading' && (
        <div
          style={{
            position: 'absolute',
            inset: 0,
            background: 'rgba(0,0,0,0.45)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: '#fff',
            fontSize: 14,
            pointerEvents: 'none',
          }}
          aria-live="polite"
        >
          Re-capturing…
        </div>
      )}
    </div>
  );
};
