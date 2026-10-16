import React, { useEffect, useMemo, useState } from 'react';
import { setRememberToken, setToken, token$ } from './lib/token';
import { setBaseUrl } from './lib/config';
import { ErrorBoundary } from './components/ErrorBoundary';
import { Nav, AuthoringTab } from './components/Nav';
import { CommandsPage } from './pages/CommandsPage';
import { GamesPage } from './pages/GamesPage';
import { SequencesPage } from './pages/SequencesPage';
import { ImagesListPage } from './pages/images/ImagesListPage';
import { BackupRestorePage } from './pages/BackupRestorePage';
import { QueuesPage } from './pages/QueuesPage';
import { normalizeTab } from './lib/navigation';
import { useLayoutMode } from './hooks/useLayoutMode';
import { useConnectionState } from './hooks/useConnectionState';
import { clearUnauthorized, useUnauthorized } from './lib/unauthorized';
import { Navigation } from './components/Navigation';
import { PhoneHeader } from './components/phone/PhoneShell';
import { PhoneTabBar } from './components/phone/PhoneTabBar';
import { ConnectionBanner } from './components/phone/ConnectionBanner';
import { DesktopOnlyNotice } from './components/phone/DesktopOnlyNotice';
import { PhoneQueuesView } from './components/queues/PhoneQueuesView';
import { NavigationAreaId, navigationAreas, isPhoneVisible } from './types/navigation';
import { initFirstEntry, parseAddress, pushScreen, replaceScreen } from './lib/phoneHistory';
import { CONFIGURATION_AREA_PATH, ConfigurationPage } from './pages/Configuration';
import { EXECUTION_AREA_PATH, EXECUTION_LOGS_AREA_PATH, ExecutionPage } from './pages/Execution';
import { ExecutionLogsPage } from './pages/ExecutionLogs';
import { NOTIFICATIONS_AREA_PATH, NotificationsPage } from './pages/NotificationsPage';
import { UPDATE_AREA_PATH, UpdatePage } from './pages/UpdatePage';
import { GAMEPACKS_AREA_PATH, GamepacksPage } from './pages/GamepacksPage';
import { MONITORING_AREA_PATH, MonitoringPage } from './pages/MonitoringPage';

const legacyPathToTab = (pathname: string): { tab: AuthoringTab; create?: boolean; id?: string } | undefined => {
  const segments = pathname.split('/').filter(Boolean);
  if (segments.length === 0) return undefined;
  const [head, second, third] = segments;
  const tabMap: Record<string, AuthoringTab> = {
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

const getInitialArea = (phone: boolean): NavigationAreaId => {
  const params = new URLSearchParams(window.location.search);
  const requested = params.get('area');
  if (requested === 'configuration' || requested === 'execution' || requested === 'execution-logs' || requested === 'authoring' || requested === 'queues' || requested === 'notifications' || requested === 'update' || requested === 'gamepacks' || requested === 'monitoring') return requested;
  const path = window.location.pathname.toLowerCase();
  if (path.startsWith(CONFIGURATION_AREA_PATH)) return 'configuration';
  if (path.startsWith(EXECUTION_LOGS_AREA_PATH)) return 'execution-logs';
  if (path.startsWith(NOTIFICATIONS_AREA_PATH)) return 'notifications';
  if (path.startsWith(UPDATE_AREA_PATH)) return 'update';
  if (path.startsWith(GAMEPACKS_AREA_PATH)) return 'gamepacks';
  if (path.startsWith(MONITORING_AREA_PATH)) return 'monitoring';
  if (path.startsWith(EXECUTION_AREA_PATH)) return 'execution';
  if (path.startsWith('/queues')) return 'queues';
  // Phone mode opens Queues when the URL names no area (contract C-03).
  return phone ? 'queues' : 'authoring';
};

const isAreaVisible = (_areaId: NavigationAreaId): boolean => true;

/** A phone request for a hidden area shows Queues (contract A-03). */
const phoneAreaOrQueues = (area: NavigationAreaId): NavigationAreaId => {
  const def = navigationAreas.find((a) => a.id === area);
  return def && isPhoneVisible(def) ? area : 'queues';
};

const getInitialQueueId = (): string | null => {
  const params = new URLSearchParams(window.location.search);
  const requested = params.get('area');
  const queue = params.get('queue');
  return (requested === null || requested === 'queues') && queue ? queue : null;
};

export const App: React.FC = () => {
  const [token, setTokenState] = useState<string>(token$.get() ?? '');
  const layoutMode = useLayoutMode();
  const phone = layoutMode === 'phone';
  const [reloadSignal, setReloadSignal] = useState(0);
  const connectionState = useConnectionState(() => setReloadSignal((s) => s + 1), phone);
  const unauthorized = useUnauthorized();
  const initialLocation = useMemo(() => {
    const searchParams = new URLSearchParams(window.location.search);
    const requestedTab = normalizeTab(searchParams.get('tab'));
    const creationTarget = searchParams.get('create')?.toLowerCase();
    const initialId = searchParams.get('id') ?? undefined;
    const legacy = legacyPathToTab(window.location.pathname);
    const initialTab = requestedTab ?? legacy?.tab ?? 'Commands';
    const initialCreate = creationTarget ?? (legacy?.create ? legacy.tab.toLowerCase() : undefined);
    const derivedId = initialId ?? legacy?.id;
    return {
      tab: initialTab,
      creationTarget: initialCreate,
      initialId: derivedId,
      legacy
    };
  }, []);

  const [activeArea, setActiveArea] = useState<NavigationAreaId>(() => {
    const area = getInitialArea(phone);
    return phone ? phoneAreaOrQueues(area) : area;
  });
  // The open queue of the phone Queues area. It lives here so the address and the history own it (plan D-7).
  const [selectedQueueId, setSelectedQueueId] = useState<string | null>(() => (
    getInitialArea(phone) === 'queues' ? getInitialQueueId() : null
  ));
  const [tab, setTab] = useState<AuthoringTab>(initialLocation.tab);
  // Bumped when the user re-clicks the already-active tab/area, signalling the
  // mounted page to return to its list view (honoring unsaved changes).
  const [authoringResetSignal, setAuthoringResetSignal] = useState(0);
  const [queuesResetSignal, setQueuesResetSignal] = useState(0);

  const handleTabChange = (next: AuthoringTab) => {
    if (next === tab) { setAuthoringResetSignal((s) => s + 1); return; }
    setTab(next);
  };

  const handleAreaChange = (next: NavigationAreaId) => {
    if (phone) pushScreen({ area: next, queueId: null });
    if (next === activeArea) {
      if (next === 'authoring') setAuthoringResetSignal((s) => s + 1);
      else if (next === 'queues') setQueuesResetSignal((s) => s + 1);
      setSelectedQueueId(null);
      return;
    }
    setActiveArea(next);
    setSelectedQueueId(null);
  };

  const handleOpenQueue = (id: string | null) => {
    if (id !== null) pushScreen({ area: 'queues', queueId: id });
    setSelectedQueueId(id);
  };
  const creationTarget = initialLocation.creationTarget;
  const requestedTab = initialLocation.tab;
  const initialId = initialLocation.initialId;
  const isLegacyTriggersPath = useMemo(() => window.location.pathname.toLowerCase().startsWith('/triggers'), []);

  useEffect(() => {
    const unsub = token$.subscribe((t) => {
      setTokenState(t ?? '');
      if (t) clearUnauthorized();
    });
    return () => unsub();
  }, []);

  // Phone mode: mark the first entry and restore the screen on back and forward (contract H-01 to H-06).
  useEffect(() => {
    if (!phone) return;
    initFirstEntry();
    const onPopState = () => {
      const position = parseAddress(window.location, 'queues');
      const area = phoneAreaOrQueues(position.area);
      setActiveArea(area);
      setSelectedQueueId(area === position.area ? position.queueId : null);
    };
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, [phone]);

  // A hidden area never shows on a phone. The redirect replaces the entry (contract H-05).
  useEffect(() => {
    if (!phone) return;
    const target = phoneAreaOrQueues(activeArea);
    if (target !== activeArea) {
      setActiveArea(target);
      setSelectedQueueId(null);
      replaceScreen({ area: target, queueId: null });
    }
  }, [phone, activeArea]);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    params.set('tab', tab);
    params.set('area', activeArea);
    if (activeArea === 'queues' && selectedQueueId) params.set('queue', selectedQueueId);
    else params.delete('queue');
    if (params.has('create')) params.delete('create');
    if (params.has('id')) params.delete('id');
    const next = `${window.location.pathname}?${params.toString()}`;
    // Keep the current state, so the entry index stays after a sync (contract H-06). The desktop state is null.
    window.history.replaceState(window.history.state, '', next);
  }, [tab, activeArea, selectedQueueId]);

  const Navbar = useMemo(() => (
    <nav className="navbar">
      <div className="brand">GameBot Web UI</div>
    </nav>
  ), []);

  const renderAuthoring = () => (
    <section id="authoring-panel" className="authoring">
      <h1>Authoring</h1>
      <Nav active={tab} onChange={handleTabChange} />
      <ErrorBoundary>
        {tab === 'Commands' && <CommandsPage initialCreate={creationTarget === 'commands'} initialEditId={requestedTab === 'Commands' ? initialId : undefined} navResetSignal={authoringResetSignal} />}
        {tab === 'Games' && <GamesPage initialCreate={creationTarget === 'games'} initialEditId={requestedTab === 'Games' ? initialId : undefined} navResetSignal={authoringResetSignal} />}
        {tab === 'Sequences' && <SequencesPage initialCreate={creationTarget === 'sequences'} initialEditId={requestedTab === 'Sequences' ? initialId : undefined} navResetSignal={authoringResetSignal} />}
        {tab === 'Images' && <ImagesListPage navResetSignal={authoringResetSignal} />}
        {tab === 'Backup & Restore' && <BackupRestorePage />}
      </ErrorBoundary>
    </section>
  );

  const renderQueues = () => (
    <section id="queues-panel" className="queues">
      {!phone && <h1>Queues</h1>}
      <ErrorBoundary>
        {phone
          ? <PhoneQueuesView connectionState={connectionState} reloadSignal={reloadSignal} queueId={selectedQueueId} onOpenQueue={handleOpenQueue} />
          : <QueuesPage navResetSignal={queuesResetSignal} />}
      </ErrorBoundary>
    </section>
  );

  const renderConfiguration = () => (
    <section id="configuration-panel" className="configuration">
      {!phone && <h1>Configuration</h1>}
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

  const renderExecutionLogs = () => (
    <section id="execution-logs-panel" className="execution-logs">
      <ErrorBoundary>
        <ExecutionLogsPage />
      </ErrorBoundary>
    </section>
  );

  const renderNotifications = () => (
    <section id="notifications-panel" className="notifications">
      {!phone && <h1>Notifications</h1>}
      <ErrorBoundary>
        <NotificationsPage />
      </ErrorBoundary>
    </section>
  );

  const renderUpdate = () => (
    <section id="update-panel" className="update">
      {!phone && <h1>Update</h1>}
      <ErrorBoundary>
        <UpdatePage />
      </ErrorBoundary>
    </section>
  );

  const renderGamepacks = () => (
    <section id="gamepacks-panel" className="gamepacks">
      {!phone && <h1>Gamepacks</h1>}
      <ErrorBoundary>
        <GamepacksPage />
      </ErrorBoundary>
    </section>
  );

  const renderMonitoring = () => (
    <section id="monitoring-panel" className="monitoring">
      {!phone && <h1>Monitoring</h1>}
      <ErrorBoundary>
        <MonitoringPage layout={phone ? 'phone' : 'desktop'} connectionState={connectionState} reloadSignal={reloadSignal} />
      </ErrorBoundary>
    </section>
  );

  const renderNotFound = () => (
    <section id="not-found-panel" className="not-found">
      <h1>Not Found</h1>
      <p>The requested page is not available. Use the navigation to continue.</p>
    </section>
  );

  const activeAreaDef = navigationAreas.find((a) => a.id === activeArea);

  const renderActiveArea = () => {
    if (isLegacyTriggersPath) return renderNotFound();
    // A desktop-only area never renders its content on a phone (contract C-04).
    if (phone && activeAreaDef?.phone === 'desktop-only') {
      return <DesktopOnlyNotice areaLabel={activeAreaDef.label} onBack={() => handleAreaChange('queues')} />;
    }
    if (activeArea === 'authoring') return renderAuthoring();
    if (activeArea === 'queues') return renderQueues();
    if (activeArea === 'monitoring') return renderMonitoring();
    if (activeArea === 'configuration') return renderConfiguration();
    if (activeArea === 'notifications') return renderNotifications();
    if (activeArea === 'update') return renderUpdate();
    if (activeArea === 'gamepacks') return renderGamepacks();
    if (activeArea === 'execution-logs') return renderExecutionLogs();
    return renderExecution();
  };

  return (
    <div className={phone ? 'app app--phone' : 'app'}>
      {/* The <main> element keeps its position in both modes, so area content does not remount on a mode change. */}
      {phone ? (
        <PhoneHeader title={activeAreaDef?.label ?? ''} />
      ) : (
        <>
          {Navbar}
          <Navigation
            areas={navigationAreas}
            activeArea={activeArea}
            onChange={handleAreaChange}
            isAreaVisible={isAreaVisible}
          />
        </>
      )}
      <main className="content">
        {phone && (
          <ConnectionBanner
            state={connectionState}
            unauthorized={unauthorized}
            onOpenConfiguration={() => handleAreaChange('configuration')}
          />
        )}
        {renderActiveArea()}
      </main>
      {phone && <PhoneTabBar areas={navigationAreas} activeArea={activeArea} onChange={handleAreaChange} />}
    </div>
  );
};