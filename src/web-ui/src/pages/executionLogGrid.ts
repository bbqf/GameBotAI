import {
  ExecutionLogEntryDto,
  ExecutionTreeNodeDto,
  ExecutionTreeNodeKind
} from '../services/executionLogsApi';

// A single line in the unified execution-logs grid. Top-level executions and
// nested sub-elements both project onto this shape so every level shares the
// same six columns.
export type GridRow = {
  key: string;
  depth: number;
  expandable: boolean;
  timestamp: string;
  name: string;
  type: string;
  status: string;
  info: string;
};

const NODE_TYPE_LABELS: Record<ExecutionTreeNodeKind, string> = {
  queue: 'Queue',
  sequence: 'Sequence',
  command: 'Command',
  step: 'Step',
  condition: 'Condition',
  loop: 'Loop',
  loopIteration: 'Iteration',
  if: 'If',
  wait: 'Wait',
  tap: 'Tap'
};

export const typeLabel = (nodeKind: string): string =>
  NODE_TYPE_LABELS[nodeKind as ExecutionTreeNodeKind] ?? nodeKind;

// Human-readable label for a status token. Most statuses read fine as-is; the
// neutral break outcome 'no_break' (feature 066) gets a friendlier "No break".
export const statusLabel = (status: string): string =>
  status === 'no_break' ? 'No break' : status;

export const formatExitCondition = (exitCondition?: string): string => {
  switch (exitCondition) {
    case 'image_detected':
      return 'Image detected';
    case 'timeout_elapsed':
      return 'Timeout elapsed';
    case 'image_unavailable':
      return 'Image unavailable';
    default:
      return exitCondition ?? 'Unknown';
  }
};

// Builds the "Additional information" text for a sub-element row, reusing the
// descriptive content that previously lived in the Execution Detail panel.
export const composeInfo = (node: ExecutionTreeNodeDto): string => {
  const parts: string[] = [];
  if (node.message) {
    parts.push(node.message);
  }
  if (typeof node.appliedDelayMs === 'number') {
    parts.push(`(delay ${node.appliedDelayMs} ms)`);
  }
  if (node.conditionTrace) {
    parts.push(
      `Condition: final result ${node.conditionTrace.finalResult ? 'true' : 'false'} (${node.conditionTrace.selectedBranch} branch)`
    );
  }
  if (node.detailAttributes) {
    const d = node.detailAttributes;
    const timeout = typeof d.timeoutMs === 'number' ? `${d.timeoutMs} ms` : 'n/a';
    const effective = typeof d.effectiveTimeoutMs === 'number' ? `${d.effectiveTimeoutMs} ms` : 'n/a';
    const confidence = typeof d.confidence === 'number' ? d.confidence.toFixed(2) : 'default';
    parts.push(`Wait settings: timeout ${timeout}, effective timeout ${effective}.`);
    parts.push(`Image: ${d.referenceImageId ?? 'not configured'}; confidence ${confidence}; load status ${d.imageLoadStatus ?? 'n/a'}.`);
    parts.push(`Exit condition: ${formatExitCondition(d.exitCondition)}.`);
  }
  return parts.join(' ');
};

// Stable, sibling-unique key for a sub-element node, derived from its parent's
// key so the same node always toggles independently across re-renders.
export const nodeKey = (node: ExecutionTreeNodeDto, parentKey: string): string =>
  `${parentKey}/${node.executionId ?? `${node.nodeKind}-${node.order}`}`;

// Projects a top-level execution onto a grid row. The timestamp is formatted by
// the caller (exact/relative mode lives in component state).
export const projectEntryRow = (entry: ExecutionLogEntryDto, timestamp: string): GridRow => ({
  key: entry.id,
  depth: 0,
  expandable: entry.executionType === 'sequence' || entry.executionType === 'queue' || entry.childCount > 0,
  timestamp,
  name: entry.objectRef.displayNameSnapshot,
  type: typeLabel(entry.executionType),
  status: entry.finalStatus,
  info: entry.summary
});

// Projects a sub-element node onto a grid row. Nodes backed by a recorded execution
// (queue/sequence/command) carry the moment they ran; the caller formats it (exact/relative
// mode lives in component state) and passes it in. Primitive step nodes have no recorded
// execution time, so their timestamp stays blank.
export const projectNodeRow = (
  node: ExecutionTreeNodeDto,
  parentKey: string,
  depth: number,
  timestamp = ''
): GridRow => ({
  key: nodeKey(node, parentKey),
  depth,
  expandable: node.children.length > 0,
  timestamp,
  name: node.label,
  type: typeLabel(node.nodeKind),
  status: node.status,
  info: composeInfo(node)
});
