import { useEffect, useState } from 'react';
import { AdbDevice, listAdbDevices } from './adbApi';

export type UseAdbDevicesState = {
  loading: boolean;
  devices: AdbDevice[];
  error?: string;
  refresh: () => void;
};

export const useAdbDevices = (enabled = true): UseAdbDevicesState => {
  const [loading, setLoading] = useState(false);
  const [devices, setDevices] = useState<AdbDevice[]>([]);
  const [error, setError] = useState<string | undefined>(undefined);

  const load = async () => {
    if (!enabled) return;
    setLoading(true);
    setError(undefined);
    try {
      const list = await listAdbDevices();
      setDevices(list);
    } catch (err: any) {
      setError(err?.message ?? 'Failed to load devices');
      setDevices([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!enabled) {
      setDevices([]);
      setError(undefined);
      setLoading(false);
      return;
    }
    void load();
  }, [enabled]);

  return { loading, devices, error, refresh: () => { void load(); } };
};
