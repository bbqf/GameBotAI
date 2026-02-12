import React, { useEffect, useMemo, useState } from 'react';
import { setRememberToken, setToken, token$ } from './lib/token';
import { setBaseUrl } from './lib/config';
import { ErrorBoundary } from './components/ErrorBoundary';
import { Nav, AuthoringTab } from './components/Nav';
import { ActionsListPage } from './pages/actions/ActionsListPage';
import { CommandsPage } from './pages/CommandsPage';
import { GamesPage } from './pages/GamesPage';
import { SequencesPage } from './pages/SequencesPage';
import { ImagesListPage } from './pages/images/ImagesListPage';
import { normalizeTab } from './lib/navigation';
import { useNavigationCollapse } from './hooks/useNavigationCollapse';
import { Navigation } from './components/Navigation';
import { NavigationAreaId, navigationAreas } from './types/navigation';
import { ConfigurationPage } from './pages/Configuration';
import { ExecutionPage } from './pages/Execution';

const legacyPathToTab = (pathname: string): { tab: AuthoringTab; create?: boolean; id?: string } | undefined => {
  const segments = pathname.split('/').filter(Boolean);
  if (segments.length === 0) return undefined;
  const [head, second, third] = segments;
  const tabMap: Record<string, AuthoringTab> = {
    actions: 'Actions',
    commands: 'Commands',
    games: 'Games',
    sequences: 'Sequences',
    images: 'Images'
  };
  const mappedTab = tabMap[head.toLowerCase()];
  if (!mappedTab) return undefined;
  if (second === 'new') return { tab: mappedTab, create: true };
  if (second && third === 'edit') return { tab: mappedTab, id: second };
  return { tab: mappedTab };
};

const getInitialArea = (): NavigationAreaId => {
  const params = new URLSearchParams(window.location.search);
  const requested = params.get('area');
  if (requested === 'configuration' || requested === 'execution' || requested === 'authoring') return requested;
  const path = window.location.pathname.toLowerCase();
  if (path.startsWith('/configuration')) return 'configuration';
  if (path.startsWith('/execution')) return 'execution';
  return 'authoring';
};

export const App: React.FC = () => {
  const [token, setTokenState] = useState<string>(token$.get() ?? '');
  const { isCollapsed } = useNavigationCollapse();
  const initialLocation = useMemo(() => {
    const searchParams = new URLSearchParams(window.location.search);
    const requestedTab = normalizeTab(searchParams.get('tab'));
    const creationTarget = searchParams.get('create')?.toLowerCase();
    const initialId = searchParams.get('id') ?? undefined;
    const legacy = legacyPathToTab(window.location.pathname);
    const initialTab = requestedTab ?? legacy?.tab ?? 'Actions';
    const initialCreate = creationTarget ?? (legacy?.create ? legacy.tab.toLowerCase() : undefined);
    const derivedId = initialId ?? legacy?.id;
    return {
      tab: initialTab,
      creationTarget: initialCreate,
      initialId: derivedId,
      legacy
    };
  }, []);

  const [activeArea, setActiveArea] = useState<NavigationAreaId>(getInitialArea());
  const [tab, setTab] = useState<AuthoringTab>(initialLocation.tab);
  const creationTarget = initialLocation.creationTarget;
  const requestedTab = initialLocation.tab;
  const initialId = initialLocation.initialId;
  const isLegacyTriggersPath = useMemo(() => window.location.pathname.toLowerCase().startsWith('/triggers'), []);

  useEffect(() => {
    const unsub = token$.subscribe((t) => setTokenState(t ?? ''));
    return () => unsub();
  }, []);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    params.set('tab', tab);
    params.set('area', activeArea);
    if (params.has('create')) params.delete('create');
    if (params.has('id')) params.delete('id');
    const next = `${window.location.pathname}?${params.toString()}`;
    window.history.replaceState(null, '', next);
  }, [tab, activeArea]);

  const Navbar = useMemo(() => (
    <nav className="navbar">
      <div className="brand">GameBot Web UI</div>
    </nav>
  ), []);

  const renderAuthoring = () => (
    <section id="authoring-panel" className="authoring">
      <h1>Authoring</h1>
      <Nav active={tab} onChange={setTab} />
      <ErrorBoundary>
        {tab === 'Actions' && <ActionsListPage initialMode={creationTarget === 'actions' ? 'create' : 'list'} initialEditId={requestedTab === 'Actions' ? initialId : undefined} />}
        {tab === 'Commands' && <CommandsPage initialCreate={creationTarget === 'commands'} initialEditId={requestedTab === 'Commands' ? initialId : undefined} />}
        {tab === 'Games' && <GamesPage initialCreate={creationTarget === 'games'} initialEditId={requestedTab === 'Games' ? initialId : undefined} />}
        {tab === 'Sequences' && <SequencesPage initialCreate={creationTarget === 'sequences'} initialEditId={requestedTab === 'Sequences' ? initialId : undefined} />}
        {tab === 'Images' && <ImagesListPage />}
      </ErrorBoundary>
    </section>
  );

  const renderConfiguration = () => (
    <section id="configuration-panel" className="configuration">
      <h1>Configuration</h1>
      <ConfigurationPage
        token={token}
        onTokenChange={(t) => setToken(t)}
        onRememberChange={(remember) => setRememberToken(remember)}
        onBaseUrlChange={(u) => setBaseUrl(u)}
      />
    </section>
  );

  const renderExecution = () => (
    <section id="execution-panel" className="execution">
      <ErrorBoundary>
        <ExecutionPage />
      </ErrorBoundary>
    </section>
  );

  const renderNotFound = () => (
    <section id="not-found-panel" className="not-found">
      <h1>Not Found</h1>
      <p>The requested page is not available. Use the navigation to continue.</p>
    </section>
  );

  const renderActiveArea = () => {
    if (isLegacyTriggersPath) return renderNotFound();
    if (activeArea === 'authoring') return renderAuthoring();
    if (activeArea === 'configuration') return renderConfiguration();
    return renderExecution();
  };

  return (
    <div className="app">
      {Navbar}
      <Navigation
        areas={navigationAreas}
        activeArea={activeArea}
        onChange={setActiveArea}
        isCollapsed={isCollapsed}
      />
      <main className="content">
        {renderActiveArea()}
      </main>
    </div>
  );
};