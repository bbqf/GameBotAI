import React, { useCallback, useEffect, useState } from 'react';
import { TokenGate } from '../components/TokenGate';
import { CollapsibleSection } from '../components/CollapsibleSection';
import { ConfigParameterList } from '../components/ConfigParameterList';
import { ConfigFileSection } from '../components/ConfigFileSection';
import { getConfigSnapshot, updateParameters, reorderParameters } from '../services/config';
import type { ConfigurationParameter } from '../services/config';
import { ApiError } from '../lib/api';

export const CONFIGURATION_AREA_PATH = '/configuration';

type ConfigurationPageProps = {
  token: string;
  onTokenChange: (t: string) => void;
  onRememberChange: (remember: boolean) => void;
  onBaseUrlChange: (url: string) => void;
};

export const ConfigurationPage: React.FC<ConfigurationPageProps> = ({ token, onTokenChange, onRememberChange, onBaseUrlChange }) => {
  const [params, setParams] = useState<ConfigurationParameter[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [applyError, setApplyError] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);

  const fetchConfig = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const snap = await getConfigSnapshot();
      setParams(Object.values(snap.parameters));
    } catch (err) {
      setFetchError(err instanceof ApiError ? err.message : 'Failed to load configuration');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchConfig(); }, [fetchConfig]);

  // Track dirty state for beforeunload warning
  useEffect(() => {
    if (!dirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [dirty]);

  const handleApply = useCallback(async (updates: Record<string, string | null>) => {
    setApplyError(null);
    setDirty(true);
    try {
      const snap = await updateParameters(updates);
      setParams(Object.values(snap.parameters));
      setDirty(false);
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to apply changes';
      setApplyError(msg);
      throw err; // Let ConfigParameterList keep edits on failure
    }
  }, []);

  const handleReorder = useCallback(async (orderedKeys: string[]) => {
    // Optimistic: reorder locally immediately
    const prevParams = params;
    const keyMap = new Map(params.map(p => [p.name, p]));
    const reordered = orderedKeys.map(k => keyMap.get(k)).filter(Boolean) as ConfigurationParameter[];
    // Append any missing
    for (const p of params) {
      if (!orderedKeys.includes(p.name)) reordered.push(p);
    }
    setParams(reordered);

    try {
      const snap = await reorderParameters(orderedKeys);
      setParams(Object.values(snap.parameters));
    } catch {
      setParams(prevParams); // Rollback
    }
  }, [params]);

  return (
    <div className="configuration-view">
      <CollapsibleSection title="Backend Connection">
        <TokenGate
          token={token}
          onTokenChange={onTokenChange}
          onRememberChange={onRememberChange}
          onBaseUrlChange={onBaseUrlChange}
        />
      </CollapsibleSection>

      <CollapsibleSection title="Main Configuration" defaultOpen>
        {loading && <p>Loading configuration…</p>}
        {fetchError && (
          <div className="config-fetch-error">
            <p className="form-error" role="alert">{fetchError}</p>
            <button onClick={fetchConfig}>Retry</button>
          </div>
        )}
        {!loading && !fetchError && (
          <ConfigParameterList
            parameters={params}
            onApply={handleApply}
            onReorder={handleReorder}
            applyError={applyError}
          />
        )}
      </CollapsibleSection>

      <CollapsibleSection title="Execution Log Policy (execution-log-policy.json)">
        <ConfigFileSection fileName="execution-log-policy.json" />
      </CollapsibleSection>

      <CollapsibleSection title="Logging Policy (logging-policy.json)">
        <ConfigFileSection fileName="logging-policy.json" />
      </CollapsibleSection>
    </div>
  );
};