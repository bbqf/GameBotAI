import {
  composeInfo,
  nodeKey,
  projectEntryRow,
  projectNodeRow,
  typeLabel
} from '../executionLogGrid';
import { ExecutionLogEntryDto, ExecutionTreeNodeDto } from '../../services/executionLogsApi';

const makeNode = (overrides: Partial<ExecutionTreeNodeDto> = {}): ExecutionTreeNodeDto => ({
  nodeKind: 'tap',
  order: 1,
  label: 'primitiveTap',
  status: 'success',
  children: [],
  ...overrides
});

describe('executionLogGrid helpers', () => {
  describe('typeLabel', () => {
    it('maps known node kinds to display labels', () => {
      expect(typeLabel('sequence')).toBe('Sequence');
      expect(typeLabel('command')).toBe('Command');
      expect(typeLabel('loopIteration')).toBe('Iteration');
      expect(typeLabel('tap')).toBe('Tap');
    });

    it('falls back to the raw value for unknown kinds', () => {
      expect(typeLabel('mystery')).toBe('mystery');
    });
  });

  describe('composeInfo', () => {
    it('uses the message alone when no extra detail is present', () => {
      expect(composeInfo(makeNode({ message: 'ran ok' }))).toBe('ran ok');
    });

    it('appends applied delay', () => {
      expect(composeInfo(makeNode({ message: 'Step ran', appliedDelayMs: 197 }))).toBe(
        'Step ran (delay 197 ms)'
      );
    });

    it('appends condition trace text', () => {
      const info = composeInfo(
        makeNode({
          nodeKind: 'condition',
          message: 'evaluated',
          conditionTrace: { finalResult: true, selectedBranch: 'then', operandResults: [], operatorSteps: [] }
        })
      );
      expect(info).toContain('Condition: final result true (then branch)');
    });

    it('appends full wait detail attributes matching the former detail panel', () => {
      const info = composeInfo(
        makeNode({
          nodeKind: 'wait',
          message: 'waited',
          detailAttributes: {
            timeoutMs: 1500,
            effectiveTimeoutMs: 1500,
            referenceImageId: 'mail_icon',
            confidence: 0.92,
            exitCondition: 'timeout_elapsed',
            imageLoadStatus: 'loaded'
          }
        })
      );
      expect(info).toContain('Wait settings: timeout 1500 ms, effective timeout 1500 ms.');
      expect(info).toContain('Image: mail_icon; confidence 0.92; load status loaded.');
      expect(info).toContain('Exit condition: Timeout elapsed.');
    });

    it('uses sensible fallbacks when wait attributes are partially set', () => {
      const info = composeInfo(makeNode({ nodeKind: 'wait', detailAttributes: { exitCondition: 'image_detected' } }));
      expect(info).toContain('Wait settings: timeout n/a, effective timeout n/a.');
      expect(info).toContain('Image: not configured; confidence default; load status n/a.');
      expect(info).toContain('Exit condition: Image detected.');
    });
  });

  describe('nodeKey', () => {
    it('prefers executionId when present', () => {
      expect(nodeKey(makeNode({ executionId: 'child-a' }), 'root')).toBe('root/child-a');
    });

    it('falls back to kind+order and is unique across siblings', () => {
      const parent = 'root';
      const a = nodeKey(makeNode({ order: 1 }), parent);
      const b = nodeKey(makeNode({ order: 2 }), parent);
      expect(a).not.toBe(b);
      expect(a).toBe('root/tap-1');
    });
  });

  describe('projectEntryRow', () => {
    const entry: ExecutionLogEntryDto = {
      id: 'exec-1',
      timestampUtc: '2026-06-02T07:09:01.000Z',
      executionType: 'sequence',
      finalStatus: 'success',
      childCount: 7,
      objectRef: { objectType: 'sequence', objectId: 'seq-1', displayNameSnapshot: 'Donate' },
      summary: "Sequence 'Donate' success with 7 steps executed."
    };

    it('maps top-level fields and uses the supplied formatted timestamp', () => {
      const row = projectEntryRow(entry, '6/2/2026, 7:09:01 AM');
      expect(row).toMatchObject({
        key: 'exec-1',
        depth: 0,
        expandable: true,
        timestamp: '6/2/2026, 7:09:01 AM',
        name: 'Donate',
        type: 'Sequence',
        status: 'success',
        info: "Sequence 'Donate' success with 7 steps executed."
      });
    });

    it('marks a childless command as not expandable', () => {
      const command = { ...entry, executionType: 'command', childCount: 0 } as ExecutionLogEntryDto;
      expect(projectEntryRow(command, 'x').expandable).toBe(false);
    });
  });

  describe('projectNodeRow', () => {
    it('blanks the timestamp and projects label/type/status/info', () => {
      const row = projectNodeRow(makeNode({ label: 'primitiveTap' }), 'root', 2);
      expect(row).toMatchObject({
        depth: 2,
        expandable: false,
        timestamp: '',
        name: 'primitiveTap',
        type: 'Tap',
        status: 'success'
      });
    });

    it('is expandable when the node has children', () => {
      const node = makeNode({ nodeKind: 'command', children: [makeNode()] });
      expect(projectNodeRow(node, 'root', 1).expandable).toBe(true);
    });
  });
});
