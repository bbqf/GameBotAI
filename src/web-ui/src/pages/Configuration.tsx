import React from 'react';
import { TokenGate } from '../components/TokenGate';

type ConfigurationPageProps = {
  token: string;
  onTokenChange: (t: string) => void;
  onRememberChange: (remember: boolean) => void;
  onBaseUrlChange: (url: string) => void;
};

export const ConfigurationPage: React.FC<ConfigurationPageProps> = ({ token, onTokenChange, onRememberChange, onBaseUrlChange }) => {
  return (
    <div className="configuration-view">
      <TokenGate
        token={token}
        onTokenChange={onTokenChange}
        onRememberChange={onRememberChange}
        onBaseUrlChange={onBaseUrlChange}
      />
      <p className="configuration-hint">Update host and token settings here; changes apply across the app.</p>
    </div>
  );
};