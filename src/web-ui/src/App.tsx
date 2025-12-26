import React, { useEffect, useMemo, useState } from 'react';
import { TokenGate } from './components/TokenGate';
import { setRememberToken, setToken, token$ } from './lib/token';
import { setBaseUrl } from './lib/config';
import { ErrorBoundary } from './components/ErrorBoundary';
import { Nav, AuthoringTab } from './components/Nav';
import { ActionsPage } from './pages/ActionsPage';
import { CommandsPage } from './pages/CommandsPage';
import { GamesPage } from './pages/GamesPage';
import { SequencesPage } from './pages/SequencesPage';
import { TriggersPage } from './pages/TriggersPage';

export const App: React.FC = () => {
  const [token, setTokenState] = useState<string>(token$.get() ?? '');
  const [tab, setTab] = useState<AuthoringTab>('Actions');


  useEffect(() => {
    const unsub = token$.subscribe((t) => setTokenState(t ?? ''));
    return () => unsub();
  }, []);

  const Navbar = useMemo(() => (
    <nav className="navbar">
      <div className="brand">GameBot Web UI</div>
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
        <section className="authoring">
          <h1>Authoring</h1>
          <Nav active={tab} onChange={setTab} />
          <ErrorBoundary>
            {tab === 'Actions' && <ActionsPage />}
            {tab === 'Commands' && <CommandsPage />}
            {tab === 'Games' && <GamesPage />}
            {tab === 'Sequences' && <SequencesPage />}
            {tab === 'Triggers' && <TriggersPage />}
          </ErrorBoundary>
        </section>
      </main>
    </div>
  );
};