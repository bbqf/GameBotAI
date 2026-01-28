import axios from "axios";

export interface RunningSessionDto {
  sessionId: string;
  gameId: string;
  emulatorId: string;
  startedAtUtc: string;
  lastHeartbeatUtc: string;
  status: "running" | "stopping";
}

export interface RunningSessionsResponse {
  sessions: RunningSessionDto[];
}

export interface StartSessionRequest {
  gameId: string;
  emulatorId: string;
  options?: Record<string, unknown>;
}

export interface StartSessionResponse {
  sessionId: string;
  runningSessions: RunningSessionDto[];
}

export interface StopSessionRequest {
  sessionId: string;
}

export async function getRunningSessions() {
  const resp = await axios.get<RunningSessionsResponse>("/api/sessions/running");
  return resp.data;
}

export async function startSession(payload: StartSessionRequest) {
  const resp = await axios.post<StartSessionResponse>("/api/sessions/start", payload);
  return resp.data;
}

export async function stopSession(payload: StopSessionRequest) {
  const resp = await axios.post<{ stopped: boolean }>("/api/sessions/stop", payload);
  return resp.data;
}
