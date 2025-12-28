import React, { useEffect, useMemo, useState } from 'react';
import { TokenGate } from './components/TokenGate';
import { setRememberToken, setToken, token$ } from './lib/token';
import { setBaseUrl } from './lib/config';
import { ErrorBoundary } from './components/ErrorBoundary';
import { Nav, AuthoringTab } from './components/Nav';
import { ActionsListPage } from './pages/actions/ActionsListPage';
import { CommandsPage } from './pages/CommandsPage';
import { GamesPage } from './pages/GamesPage';
import { SequencesPage } from './pages/SequencesPage';
import { TriggersPage } from './pages/TriggersPage';
import { normalizeTab } from './lib/navigation';

const legacyPathToTab = (pathname: string): { tab: AuthoringTab; create?: boolean; id?: string } | undefined => {
  const segments = pathname.split('/').filter(Boolean);
  if (segments.length === 0) return undefined;
  const [head, second, third] = segments;
  const tabMap: Record<string, AuthoringTab> = {
    actions: 'Actions',
    commands: 'Commands',
    games: 'Games',
    sequences: 'Sequences',
    triggers: 'Triggers'
  };
  const mappedTab = tabMap[head.toLowerCase()];
  if (!mappedTab) return undefined;
  if (second === 'new') return { tab: mappedTab, create: true };
  if (second && third === 'edit') return { tab: mappedTab, id: second };
  return { tab: mappedTab };
};

export const App: React.FC = () => {
  const [token, setTokenState] = useState<string>(token$.get() ?? '');
  const initialLocation = useMemo(() => {
    const searchParams = new URLSearchParams(window.location.search);
    const requestedTab = normalizeTab(searchParams.get('tab'));
    const creationTarget = searchParams.get('create')?.toLowerCase();
    const initialId = searchParams.get('id') ?? undefined;
    const legacy = legacyPathToTab(window.location.pathname);
    const initialTab = requestedTab ?? legacy?.tab ?? 'Actions';
    const initialCreate = creationTarget ?? (legacy?.create ? legacy.tab.toLowerCase() : undefined);
    const derivedId = initialId ?? legacy?.id;
    const shouldRewrite = Boolean(legacy && !requestedTab && !creationTarget && !initialId);
    return {
      tab: initialTab,
      creationTarget: initialCreate,
      initialId: derivedId,
      legacy,
      shouldRewrite
    };
  }, []);
  const [tab, setTab] = useState<AuthoringTab>(initialLocation.tab);
  const creationTarget = initialLocation.creationTarget;
  const requestedTab = initialLocation.tab;
  const initialId = initialLocation.initialId;


  useEffect(() => {
    const unsub = token$.subscribe((t) => setTokenState(t ?? ''));
    return () => unsub();
  }, []);

  useEffect(() => {
    if (!initialLocation.legacy || !initialLocation.shouldRewrite) return;
    const params = new URLSearchParams();
    params.set('tab', initialLocation.legacy.tab);
    if (initialLocation.legacy.create) params.set('create', initialLocation.legacy.tab.toLowerCase());
    if (initialLocation.legacy.id) params.set('id', initialLocation.legacy.id);
    const next = `/?${params.toString()}`;
    window.history.replaceState(null, '', next);
  }, [initialLocation]);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    params.set('tab', tab);
    if (params.has('create')) params.delete('create');
    if (params.has('id')) params.delete('id');
    const next = `${window.location.pathname}?${params.toString()}`;
    window.history.replaceState(null, '', next);
  }, [tab]);

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
            {tab === 'Actions' && <ActionsListPage initialMode={creationTarget === 'actions' ? 'create' : 'list'} initialEditId={requestedTab === 'Actions' ? initialId : undefined} />}
            {tab === 'Commands' && <CommandsPage initialCreate={creationTarget === 'commands'} initialEditId={requestedTab === 'Commands' ? initialId : undefined} />}
            {tab === 'Games' && <GamesPage initialCreate={creationTarget === 'games'} initialEditId={requestedTab === 'Games' ? initialId : undefined} />}
            {tab === 'Sequences' && <SequencesPage initialCreate={creationTarget === 'sequences'} initialEditId={requestedTab === 'Sequences' ? initialId : undefined} />}
            {tab === 'Triggers' && <TriggersPage initialCreate={creationTarget === 'triggers'} initialEditId={requestedTab === 'Triggers' ? initialId : undefined} />}
          </ErrorBoundary>
        </section>
      </main>
    </div>
  );
};