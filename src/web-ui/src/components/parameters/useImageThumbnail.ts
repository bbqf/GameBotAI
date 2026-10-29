import { useEffect, useRef, useState } from 'react';
import { getImageBlob } from '../../services/images';

export type ImageThumbnail = {
  /** Object URL of the image, or null while loading or when the image is missing. */
  url: string | null;
  /** True when the image was not found. */
  missing: boolean;
  /** Attach to the element that holds the thumbnail. The image loads when the element scrolls into view. */
  ref: (element: Element | null) => void;
};

/**
 * Loads the thumbnail of a stored image when its tile scrolls into view (feature 168). The blob URL is
 * released when the tile goes away. Without `IntersectionObserver` (for example in a test), the image
 * loads at once.
 */
export const useImageThumbnail = (imageId: string): ImageThumbnail => {
  const [visible, setVisible] = useState(false);
  const [url, setUrl] = useState<string | null>(null);
  const [missing, setMissing] = useState(false);
  const observer = useRef<IntersectionObserver | null>(null);

  const ref = (element: Element | null) => {
    observer.current?.disconnect();
    observer.current = null;
    if (!element) return;
    if (typeof IntersectionObserver === 'undefined') {
      setVisible(true);
      return;
    }
    observer.current = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        setVisible(true);
        observer.current?.disconnect();
      }
    });
    observer.current.observe(element);
  };

  useEffect(() => () => observer.current?.disconnect(), []);

  useEffect(() => {
    if (!visible) return undefined;
    let cancelled = false;
    let objectUrl: string | null = null;
    setMissing(false);
    getImageBlob(imageId)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setMissing(true);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [visible, imageId]);

  return { url, missing, ref };
};
