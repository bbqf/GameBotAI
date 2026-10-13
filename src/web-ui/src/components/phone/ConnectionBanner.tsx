import React from 'react';
import { ConnectionState } from '../../hooks/useConnectionState';

export type ConnectionBannerProps = {
  state: ConnectionState;
  /** True after an API call returned 401. */
  unauthorized: boolean;
  onOpenConfiguration: () => void;
};

/** Banner at the top of the content: connection lost (C-09) or token needed (C-10). */
export const ConnectionBanner: React.FC<ConnectionBannerProps> = ({ state, unauthorized, onOpenConfiguration }) => {
  if (state !== 'lost' && !unauthorized) return null;
  return (
    <>
      {state === 'lost' && (
        <div className="phone-banner" role="alert">Connection lost</div>
      )}
      {unauthorized && (
        <div className="phone-banner phone-banner--error" role="alert">
          <p>Access token needed. Open Configuration.</p>
          <button type="button" onClick={onOpenConfiguration}>Open Configuration</button>
        </div>
      )}
    </>
  );
};
