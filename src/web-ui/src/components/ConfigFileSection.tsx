import React, { useCallback, useEffect, useState } from 'react';
import { ConfigParameterList } from './ConfigParameterList';
import { getConfigFileParams, updateConfigFileParams } from '../services/config';
import type { ConfigurationParameter } from '../services/config';
import { ApiError } from '../lib/api';

export type ConfigFileSectionProps = {
  fileName: string;
};

export const ConfigFileSection: React.FC<ConfigFileSectionProps> = ({ fileName }) => {
  const [params, setParams] = useState<ConfigurationParameter[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [applyError, setApplyError] = useState<string | null>(null);

  const fetchFile = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const result = await getConfigFileParams(fileName);
      setParams(Object.values(result.parameters));
    } catch (err) {
      setFetchError(err instanceof ApiError ? err.message : 'Failed to load config file');
    } finally {
      setLoading(false);
    }
  }, [fileName]);

  useEffect(() => { fetchFile(); }, [fetchFile]);

  const handleApply = useCallback(async (updates: Record<string, string | null>) => {
    setApplyError(null);
    try {
      const result = await updateConfigFileParams(fileName, updates);
      setParams(Object.values(result.parameters));
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to apply changes';
      setApplyError(msg);
      throw err;
    }
  }, [fileName]);

  if (loading) return <p>Loading…</p>;
  if (fetchError) return (
    <div className="config-fetch-error">
      <p className="form-error" role="alert">{fetchError}</p>
      <button onClick={fetchFile}>Retry</button>
    </div>
  );

  if (params.length === 0) return <p className="config-empty-state">File not yet created. Configure via the API to generate it.</p>;

  return (
    <ConfigParameterList
      parameters={params}
      onApply={handleApply}
      applyError={applyError}
    />
  );
};
