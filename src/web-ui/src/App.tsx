import React, { useEffect, useMemo, useState } from 'react';
import { TokenGate } from './components/TokenGate';
import { SequencesCreate } from './pages/SequencesCreate';
import { SequenceView } from './pages/SequenceView';
import { setRememberToken, setToken, token$ } from './lib/token';
import { setBaseUrl } from './lib/config';

export const App: React.FC = () => {
  const [route, setRoute] = useState<'create' | 'view'>('create');
  const [token, setTokenState] = useState<string>(token$.get() ?? '');

  useEffect(() => {
    const unsub = token$.subscribe((t) => setTokenState(t ?? ''));
    return () => unsub();
  }, []);

  const Navbar = useMemo(() => (
    <nav className="navbar">
      <div className="brand">GameBot Web UI</div>
      <div className="links">
        <button onClick={() => setRoute('create')}>Create Sequence</button>
        <button onClick={() => setRoute('view')}>View Sequence</button>
      </div>
    </nav>
  ), []);

  return (
    <div className="app">
      {Navbar}
      <TokenGate
        token={token}
        onTokenChange={(t) => setToken(t)}
        onRememberChange={(r) => setRememberToken(r)}
        onBaseUrlChange={(u) => setBaseUrl(u)}
      />
      <main className="content">
        {route === 'create' && <SequencesCreate />}
        {route === 'view' && <SequenceView />}
      </main>
    </div>
  );
};