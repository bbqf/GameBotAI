import React, { useEffect, useState } from 'react';
import { baseUrl$, getBaseUrl, setBaseUrl } from '../lib/config';

export const TokenGate: React.FC<{
  token: string;
  onTokenChange: (t: string) => void;
  onRememberChange: (remember: boolean) => void;
  onBaseUrlChange: (url: string) => void;
}> = ({ token, onTokenChange, onRememberChange, onBaseUrlChange }) => {
  const [remember, setRemember] = useState<boolean>(false);
  const [baseUrl, setLocalBaseUrl] = useState<string>(getBaseUrl());

  useEffect(() => {
    const unsub = baseUrl$.subscribe((u) => setLocalBaseUrl(u));
    return () => unsub();
  }, []);

  return (
    <section className="token-gate">
      <div className="row">
        <label htmlFor="baseUrl">API Base URL</label>
        <input
          id="baseUrl"
          type="text"
          placeholder="http://localhost:5081"
          value={baseUrl}
          onChange={(e) => {
            setLocalBaseUrl(e.target.value);
            setBaseUrl(e.target.value);
            onBaseUrlChange(e.target.value);
          }}
        />
      </div>
      <div className="row">
        <label htmlFor="token">Bearer Token</label>
        <input
          id="token"
          type="password"
          placeholder="Paste token (optional if service anonymous)"
          value={token}
          onChange={(e) => onTokenChange(e.target.value)}
        />
        <label className="remember">
          <input
            type="checkbox"
            checked={remember}
            onChange={(e) => {
              setRemember(e.target.checked);
              onRememberChange(e.target.checked);
            }}
          />
          Remember token (localStorage)
        </label>
      </div>
    </section>
  );
};