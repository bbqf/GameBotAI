import React, { useEffect, useState } from 'react';
import { getImageBlob } from '../../services/images';

/**
 * Props for the ImageThumbnail component.
 *
 * @prop imageId - Identifier used to fetch the image blob via /api/images/{imageId}.
 * @prop className - Optional CSS class applied to the rendered img or placeholder span.
 * @prop alt - Optional alt text for the img element; defaults to imageId.
 *
 * Error mode: if the blob fetch fails, the component silently renders a placeholder span — non-blocking.
 * Cache behaviour: the first successful fetch stores the object URL in a module-level cache;
 * subsequent renders of the same imageId skip the network call and use the cached URL.
 */
export type ImageThumbnailProps = {
  imageId: string;
  className?: string;
  alt?: string;
};

// Session-lifetime cache: imageId → object URL. Not cleared — safe for authoring tool scale.
const thumbnailCache = new Map<string, string>();

export const ImageThumbnail: React.FC<ImageThumbnailProps> = ({ imageId, className, alt }) => {
  const [src, setSrc] = useState<string | null>(thumbnailCache.get(imageId) ?? null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (thumbnailCache.has(imageId)) {
      setSrc(thumbnailCache.get(imageId)!);
      setFailed(false);
      return;
    }
    let cancelled = false;
    getImageBlob(imageId)
      .then((blob) => {
        if (cancelled) return;
        const url = URL.createObjectURL(blob);
        thumbnailCache.set(imageId, url);
        setSrc(url);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => { cancelled = true; };
  }, [imageId]);

  if (src && !failed) {
    return <img src={src} alt={alt ?? imageId} className={className} />;
  }
  return <span className={`image-thumbnail--placeholder${className ? ` ${className}` : ''}`} aria-label={alt ?? imageId} />;
};
