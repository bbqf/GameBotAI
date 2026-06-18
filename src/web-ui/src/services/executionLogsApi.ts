import { getJson } from '../lib/api';

export type ExecutionLogListSortBy = 'timestamp' | 'objectName' | 'status';
export type ExecutionLogSortDirection = 'asc' | 'desc';

export type ExecutionLogObjectRefDto = {
  objectType: string;
  objectId: string;
  displayNameSnapshot: string;
  versionSnapshot?: string;
};

export type ExecutionStatus = 'running' | 'success' | 'failure';

export type ExecutionLogEntryDto = {
  id: string;
  timestampUtc: string;
  executionType: string;
  finalStatus: ExecutionStatus;
  childCount: number;
  objectRef: ExecutionLogObjectRefDto;
  summary: string;
};

export type ExecutionTreeNodeKind =
  | 'queue'
  | 'sequence'
  | 'command'
  | 'step'
  | 'condition'
  | 'loop'
  | 'loopIteration'
  | 'wait'
  | 'tap';

export type ExecutionTreeNodeStatus = ExecutionStatus | 'skipped' | 'not_executed';

export type ExecutionTreeNodeDto = {
  nodeKind: ExecutionTreeNodeKind;
  executionId?: string;
  order: number;
  label: string;
  status: ExecutionTreeNodeStatus;
  message?: string;
  timestampUtc?: string;
  appliedDelayMs?: number;
  commandName?: string;
  detailAttributes?: ExecutionLogWaitForImageDetailAttributesDto;
  conditionTrace?: ExecutionLogConditionTraceDto;
  deepLink?: ExecutionLogStepDeepLinkDto;
  children: ExecutionTreeNodeDto[];
};

export type ExecutionSubtreeResponseDto = {
  executionId: string;
  finalStatus: ExecutionStatus;
  root: ExecutionTreeNodeDto;
};

export type ExecutionLogListResponseDto = {
  items: ExecutionLogEntryDto[];
  nextPageToken?: string;
  nextCursor?: string;
};

export type ExecutionLogRelatedObjectDto = {
  label: string;
  targetType: string;
  targetId: string;
  isAvailable: boolean;
  unavailableReason?: string;
};

export type ExecutionLogSnapshotDto = {
  isAvailable: boolean;
  imageUrl?: string;
  caption?: string;
};

export type ExecutionLogStepOutcomeDto = {
  sequenceId?: string;
  sequenceLabel?: string;
  stepId?: string;
  stepLabel?: string;
  stepName: string;
  commandName?: string;
  stepType?: string;
  status: string;
  message: string;
  appliedDelayMs?: number;
  detailAttributes?: ExecutionLogWaitForImageDetailAttributesDto;
  deepLink?: ExecutionLogStepDeepLinkDto;
  conditionTrace?: ExecutionLogConditionTraceDto;
  startedAtUtc?: string;
  endedAtUtc?: string;
};

export type ExecutionLogWaitForImageDetailAttributesDto = {
  timeoutMs?: number;
  effectiveTimeoutMs?: number;
  referenceImageId?: string;
  confidence?: number;
  exitCondition?: 'image_detected' | 'timeout_elapsed' | 'image_unavailable';
  imageLoadStatus?: string;
};

export type ExecutionLogStepDeepLinkDto = {
  sequenceId: string;
  stepId?: string;
  sequenceLabel: string;
  stepLabel: string;
  resolutionStatus: 'resolved' | 'step_missing' | 'sequence_missing';
  directPath: string;
  fallbackRoute?: string;
};

export type ExecutionLogConditionTraceDto = {
  finalResult: boolean;
  selectedBranch: string;
  failureReason?: string;
  operandResults: Record<string, unknown>[];
  operatorSteps: Record<string, unknown>[];
};

export type ExecutionLogDetailDto = {
  executionId: string;
  summary: string;
  relatedObjects: ExecutionLogRelatedObjectDto[];
  snapshot: ExecutionLogSnapshotDto;
  stepOutcomes: ExecutionLogStepOutcomeDto[];
};

export type ListExecutionLogsRequest = {
  sortBy?: ExecutionLogListSortBy;
  sortDirection?: ExecutionLogSortDirection;
  filterTimestamp?: string;
  filterObjectName?: string;
  filterStatus?: string;
  pageSize?: number;
  pageToken?: string;
};

const toQueryString = (query: ListExecutionLogsRequest): string => {
  const params = new URLSearchParams();
  if (query.sortBy) params.set('sortBy', query.sortBy);
  if (query.sortDirection) params.set('sortDirection', query.sortDirection);
  if (query.filterTimestamp) params.set('filterTimestamp', query.filterTimestamp);
  if (query.filterObjectName) params.set('filterObjectName', query.filterObjectName);
  if (query.filterStatus) params.set('filterStatus', query.filterStatus);
  if (query.pageSize && query.pageSize > 0) params.set('pageSize', `${query.pageSize}`);
  if (query.pageToken) params.set('pageToken', query.pageToken);
  const rendered = params.toString();
  return rendered.length > 0 ? `?${rendered}` : '';
};

export const listExecutionLogs = async (query: ListExecutionLogsRequest = {}): Promise<ExecutionLogListResponseDto> => {
  const response = await getJson<ExecutionLogListResponseDto>(`/api/execution-logs${toQueryString(query)}`);
  return {
    items: Array.isArray(response?.items) ? response.items : [],
    nextPageToken: response?.nextPageToken ?? response?.nextCursor,
    nextCursor: response?.nextCursor
  };
};

export const getExecutionLogDetail = (executionId: string) => getJson<ExecutionLogDetailDto>(`/api/execution-logs/${encodeURIComponent(executionId)}`);

export const getExecutionSubtree = (executionId: string) =>
  getJson<ExecutionSubtreeResponseDto>(`/api/execution-logs/${encodeURIComponent(executionId)}/subtree`);
