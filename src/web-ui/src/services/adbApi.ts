import { getJson } from '../lib/api';

export type AdbDevice = {
  serial: string;
  state?: string;
  info?: string;
};

export const listAdbDevices = async (): Promise<AdbDevice[]> => {
  const devices = await getJson<AdbDevice[]>('/api/adb/devices');
  return Array.isArray(devices) ? devices : [];
};
