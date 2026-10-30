import { deleteJson, getJson, postJson } from '../lib/api';
import type { ParameterBinding, ParameterScopeEntry } from '../components/parameters/types';

export type QueueTemplateSummary = {
  id: string;
  name: string;
  entryCount: number;
  /**
   * True when at least one entry has no entry ID (feature 171). The template works as before. A live apply to a
   * running queue needs the manual upgrade first.
   */
  needsUpgrade?: boolean;
  createdAt: string | null;
  updatedAt: string | null;
};

export type ScheduleType = 'OncePerRun' | 'EveryStep' | 'Timer' | 'AtQueueStart' | 'BeforeEachRun';

export type QueueTemplateEntryDto = {
  /** Stable ID of the entry (feature 171). Absent for an entry of a template that needs an upgrade. */
  entryId?: string | null;
  sequenceId: string;
  sequenceName: string | null;
  stale: boolean;
  scheduleType: ScheduleType;
  timerTimeOfDay: string | null;
  /** Relative-mode offset ("HH:mm:ss") for a Timer entry; null in time-of-day mode. */
  timerRelativeOffset: string | null;
  /** Whether the entry runs during a queue run. Disabled entries stay in the template but are skipped. */
  enabled: boolean;
  /** Values this entry supplies to its sequence's run scope (feature 078). */
  parameterValues?: ParameterBinding[] | null;
  /** True when the entry carries any value, so the list can badge it without being opened (FR-030). */
  hasParameterOverrides?: boolean;
  /** Effective value and originating scope per parameter, for the entry's preview (FR-028). */
  effectiveParameters?: ParameterScopeEntry[] | null;
};

export type QueueTemplateDetail = QueueTemplateSummary & { entries: QueueTemplateEntryDto[] };

/** Per-entry payload for saving a template. */
export type TemplateEntrySaveDto = {
  /** Send back the entry ID that the detail gave, so that a save keeps the identity of the entry (feature 171). */
  entryId?: string;
  sequenceId: string;
  scheduleType?: ScheduleType;
  timerTimeOfDay?: string;
  /** Relative-mode offset ("HH:mm:ss"); mutually exclusive with timerTimeOfDay. */
  timerRelativeOffset?: string;
  /** Whether the entry runs during a queue run. Omit or true = enabled; false = disabled. */
  enabled?: boolean;
  /**
   * Values this entry supplies (feature 078). Holds both declared bindings and ad-hoc names; an
   * ad-hoc name reaches any command beneath the entry at any depth.
   */
  parameterValues?: ParameterBinding[];
};

export type SaveQueueTemplate = {
  name: string;
  entries: TemplateEntrySaveDto[];
  overwrite: boolean;
};

const base = '/api/queue-templates';

export const listQueueTemplates = () => getJson<QueueTemplateSummary[]>(base);
export const getQueueTemplate = (id: string) => getJson<QueueTemplateDetail>(`${base}/${id}`);
export const saveQueueTemplate = (input: SaveQueueTemplate) => postJson<QueueTemplateDetail>(base, input);
export const deleteQueueTemplate = (id: string) => deleteJson<void>(`${base}/${id}`);

// ── Manual upgrade of a template (feature 171) ───────────────────────────────────────────────

export type TemplateUpgradeMatch = { queueId: string; position: number; sequenceName: string };

export type TemplateUpgradeUnmatched = {
  queueId: string;
  position: number;
  runningSequence?: string | null;
  templateSequence?: string | null;
  reason: string;
};

/** What the upgrade would do. It changes nothing. */
export type TemplateUpgradePreview = {
  /** The number of entries that have no entry ID now. */
  entriesToChange: number;
  /** The entries of running queues that use the template, with their match. */
  runningMatch: TemplateUpgradeMatch[];
  /** The running entries that the upgrade cannot match. The upgrade is refused while this list is not empty. */
  unmatched: TemplateUpgradeUnmatched[];
};

export type TemplateUpgradeResult = { entriesChanged: number };

/** Reads what the upgrade of a template would do. */
export const previewQueueTemplateUpgrade = (id: string) =>
  postJson<TemplateUpgradePreview>(`${base}/${id}/upgrade?dryRun=true`, {});

/**
 * Gives an entry ID to each entry that has none. It changes no other field. Send `confirm` when a running queue uses
 * the template. A repeat changes nothing and gives 0.
 */
export const upgradeQueueTemplate = (id: string, confirm = false) =>
  postJson<TemplateUpgradeResult>(`${base}/${id}/upgrade${confirm ? '?confirm=true' : ''}`, {});
