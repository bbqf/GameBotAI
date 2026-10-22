import React, { useCallback, useEffect, useState } from 'react';
import {
  CatalogApiError,
  CatalogGamepack,
  CatalogSettings,
  getCatalogSettings,
  installFromCatalog,
  listCatalog,
  putCatalogSettings
} from '../services/gamepackCatalog';
import './GamepacksPage.css';

export const GAMEPACKS_AREA_PATH = '/gamepacks';

// Plain words for each reason code. The page never shows a raw code alone.
const reasonText: Record<string, string> = {
  not_configured: 'No catalog is set. Enter a catalog location and save it.',
  invalid_location: 'The catalog location is not valid.',
  unreachable: 'The catalog cannot be reached.',
  unauthorized: 'The catalog refused the token.',
  forbidden: 'The catalog does not allow this token.',
  not_found: 'The catalog does not have this file.',
  index_invalid: 'The catalog index is missing or not valid.',
  index_too_large: 'The catalog index is too large.',
  file_missing: 'The catalog does not have the gamepack file.',
  file_too_large: 'The file is too large.',
  hash_mismatch: 'The file does not match its hash. The install stopped.',
  redirect_refused: 'The catalog sent the request to another place. The request stopped.',
  timeout: 'The catalog did not answer in time.',
  entry_not_listed: 'The catalog does not list this version.',
  signature_rejected: 'The signature check rejected the gamepack.'
};

const errorText = (err: unknown, fallback: string): string => {
  if (err instanceof CatalogApiError) {
    const plain = reasonText[err.code];
    if (plain) return err.message && err.message !== plain ? `${plain} ${err.message}` : plain;
    return err.message || fallback;
  }
  return err instanceof Error ? err.message : fallback;
};

type Notice = { kind: 'ok' | 'error'; text: string };

// The last dry run. A take-over install is allowed only after a valid dry run with the same version and take-over choice.
type LastDryRun = { key: string; takeOver: boolean; valid: boolean; takenOver: number };

const TAKE_OVER_FIRST_HINT = 'Run the dry run with take-over first.';

export const GamepacksPage: React.FC = () => {
  const [settings, setSettings] = useState<CatalogSettings | null>(null);
  const [location, setLocation] = useState('');
  const [allowInsecure, setAllowInsecure] = useState(false);
  const [token, setToken] = useState('');
  const [settingsNotice, setSettingsNotice] = useState<Notice | null>(null);
  const [packs, setPacks] = useState<CatalogGamepack[] | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [dryRun, setDryRun] = useState(false);
  const [takeOver, setTakeOver] = useState(false);
  const [lastDryRun, setLastDryRun] = useState<LastDryRun | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [installNotice, setInstallNotice] = useState<Notice | null>(null);

  // The list loads on open and on the Refresh button only. There is no timer (FR-015).
  const reload = useCallback(async () => {
    setLoading(true);
    setListError(null);
    try {
      const list = await listCatalog();
      setPacks(list.gamepacks);
    } catch (err) {
      setPacks(null);
      setListError(errorText(err, 'The list could not be loaded.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    getCatalogSettings()
      .then((s) => {
        if (cancelled) return;
        setSettings(s);
        setLocation(s.location ?? '');
        setAllowInsecure(s.allowInsecure);
      })
      .catch((err) => {
        if (!cancelled) setSettingsNotice({ kind: 'error', text: errorText(err, 'The settings could not be loaded.') });
      });
    void reload();
    return () => {
      cancelled = true;
    };
  }, [reload]);

  const handleSave = async (event: React.FormEvent) => {
    event.preventDefault();
    setSettingsNotice(null);
    try {
      // The token field is never filled from the server. Empty means keep the saved token.
      const saved = await putCatalogSettings({
        location: location.trim() === '' ? null : location.trim(),
        allowInsecure,
        token: token === '' ? null : token
      });
      setSettings(saved);
      setToken('');
      setSettingsNotice({ kind: 'ok', text: 'The settings are saved.' });
      await reload();
    } catch (err) {
      setSettingsNotice({ kind: 'error', text: errorText(err, 'The settings were not saved.') });
    }
  };

  const handleClearToken = async () => {
    setSettingsNotice(null);
    try {
      const saved = await putCatalogSettings({ location: settings?.location ?? null, allowInsecure: settings?.allowInsecure ?? false, token: '' });
      setSettings(saved);
      setSettingsNotice({ kind: 'ok', text: 'The token is cleared.' });
    } catch (err) {
      setSettingsNotice({ kind: 'error', text: errorText(err, 'The token was not cleared.') });
    }
  };

  const handleInstall = async (name: string, version: string) => {
    const key = `${name}@${version}`;
    setBusyKey(key);
    setInstallNotice(null);
    try {
      const result = await installFromCatalog(name, version, dryRun, takeOver);
      const counts = `Created ${result.created}, updated ${result.updated}, removed ${result.removed}.`;
      if (dryRun) {
        const takenOver = result.takenOver ?? 0;
        setLastDryRun({ key, takeOver, valid: result.valid, takenOver });
        const takeOverText = takeOver ? ` Take over ${takenOver}.` : '';
        const problems = (result.problems ?? [])
          .map((p) => p.message)
          .filter((m): m is string => !!m)
          .join(' ');
        setInstallNotice({
          kind: result.valid ? 'ok' : 'error',
          text: result.valid
            ? `Dry run for ${name} ${version}: the install would work. Nothing changed. ${counts}${takeOverText}`
            : `Dry run for ${name} ${version}: the install would fail. Nothing changed.${takeOverText}${problems ? ` ${problems}` : ''}`
        });
      } else {
        setInstallNotice({ kind: 'ok', text: `Installed ${name} ${version}. ${counts}` });
        setLastDryRun(null);
        await reload();
      }
    } catch (err) {
      if (dryRun) setLastDryRun(null);
      setInstallNotice({ kind: 'error', text: `${dryRun ? 'Dry run' : 'Install'} of ${name} ${version} failed. Nothing changed. ${errorText(err, '')}`.trim() });
    } finally {
      setBusyKey(null);
    }
  };

  const handleTakeOverChange = (checked: boolean) => {
    setTakeOver(checked);
    setLastDryRun(null);
    setInstallNotice(null);
  };

  // With take-over on and Dry run off, the button needs a valid dry run for the same version and choice.
  const needsDryRun = (key: string): boolean =>
    takeOver &&
    !dryRun &&
    !(lastDryRun !== null && lastDryRun.valid && lastDryRun.key === key && lastDryRun.takeOver === takeOver);

  return (
    <div className="gamepacks-page">
      <section className="gamepacks-section">
        <h2>Catalog</h2>
        <form className="gamepacks-settings" onSubmit={handleSave}>
          <label>
            Catalog location
            <input
              type="text"
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              placeholder="https://example.com/catalog/ or C:\catalog"
            />
          </label>
          <label className="gamepacks-check">
            <input type="checkbox" checked={allowInsecure} onChange={(e) => setAllowInsecure(e.target.checked)} />
            Allow insecure (http) web addresses
          </label>
          <label>
            Token
            <input
              type="password"
              value={token}
              autoComplete="off"
              onChange={(e) => setToken(e.target.value)}
            />
          </label>
          {settings?.tokenSet && <p className="gamepacks-hint">A token is set. Leave the field empty to keep it.</p>}
          <p className="gamepacks-hint">The token and "allow insecure" apply to web addresses only. A folder catalog ignores both.</p>
          <div className="gamepacks-actions">
            <button type="submit">Save settings</button>
            {settings?.tokenSet && (
              <button type="button" onClick={handleClearToken}>Clear token</button>
            )}
          </div>
          {settingsNotice && (
            <p role={settingsNotice.kind === 'error' ? 'alert' : 'status'} className={settingsNotice.kind === 'error' ? 'error' : undefined}>
              {settingsNotice.text}
            </p>
          )}
        </form>
      </section>

      <section className="gamepacks-section">
        <h2>Available gamepacks</h2>
        <div className="gamepacks-actions">
          <button type="button" onClick={() => void reload()} disabled={loading}>
            {loading ? 'Loading...' : 'Refresh'}
          </button>
          <label className="gamepacks-check">
            <input type="checkbox" checked={dryRun} onChange={(e) => setDryRun(e.target.checked)} />
            Dry run
          </label>
          <label className="gamepacks-check">
            <input type="checkbox" checked={takeOver} onChange={(e) => handleTakeOverChange(e.target.checked)} />
            Take over objects of the user with the same name
          </label>
        </div>
        <p className="gamepacks-hint">The gamepack owns these objects and replaces them. The old content goes to a backup.</p>

        {installNotice && (
          <p role={installNotice.kind === 'error' ? 'alert' : 'status'} className={installNotice.kind === 'error' ? 'error' : undefined}>
            {installNotice.text}
          </p>
        )}
        {listError && <p role="alert" className="error">{listError}</p>}
        {packs && packs.length === 0 && <p>The catalog has no gamepacks.</p>}

        {packs?.map((pack) => (
          <div className="gamepacks-pack" key={pack.name}>
            <h3>{pack.name}</h3>
            <p className="gamepacks-hint">
              {pack.installedVersion ? `Installed version: ${pack.installedVersion}` : 'Not installed.'}
            </p>
            <ul className="gamepacks-versions">
              {pack.versions.map((v) => {
                const key = `${pack.name}@${v.version}`;
                const blocked = needsDryRun(key);
                return (
                  <li key={key} className="gamepacks-version">
                    <div className="gamepacks-version-head">
                      <strong>{v.version}</strong>
                      {v.installed && <span className="gamepacks-badge">Installed</span>}
                    </div>
                    {v.changelog && <pre className="gamepacks-changelog">{v.changelog}</pre>}
                    {v.updateAvailable && (
                      <button
                        type="button"
                        onClick={() => void handleInstall(pack.name, v.version)}
                        disabled={busyKey !== null || blocked}
                        aria-label={`${pack.installedVersion ? 'Update' : 'Install'} ${pack.name} ${v.version}`}
                      >
                        {busyKey === key ? 'Working...' : pack.installedVersion ? 'Update' : 'Install'}
                      </button>
                    )}
                    {v.updateAvailable && blocked && <p className="gamepacks-hint">{TAKE_OVER_FIRST_HINT}</p>}
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </section>
    </div>
  );
};
