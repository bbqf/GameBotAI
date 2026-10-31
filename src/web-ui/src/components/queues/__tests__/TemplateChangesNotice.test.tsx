import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { TemplateChangesNotice } from '../TemplateChangesNotice';
import { getTemplateDiff } from '../../../services/queues';

jest.mock('../../../services/queues');
jest.mock('../ReviewTemplateChangesDialog', () => ({
  ReviewTemplateChangesDialog: ({ paused, onClose, onApplied }: { paused: boolean; onClose: () => void; onApplied?: () => void }) => (
    <div data-testid="review-dialog-stub">
      paused:{String(paused)}
      <button type="button" onClick={() => onApplied?.()}>Stub apply</button>
      <button type="button" onClick={onClose}>Stub close</button>
    </div>
  ),
}));

const diffMock = getTemplateDiff as jest.MockedFunction<typeof getTemplateDiff>;

const difference = (n: number) => ({
  key: `added:e${n}`, type: 'added', entryId: `e${n}`, sequenceName: `Seq ${n}`, summary: `Add Seq ${n}`,
  selectedByDefault: true, affectsSchedule: false,
});

const diff = (count: number, over: Record<string, unknown> = {}) => ({
  queueId: 'q1', templateId: 't1', needsUpgrade: false, baseToken: 'tok',
  differences: Array.from({ length: count }, (_, i) => difference(i + 1)), ...over,
});

const renderNotice = async (props: Partial<React.ComponentProps<typeof TemplateChangesNotice>> = {}) => {
  const result = render(<TemplateChangesNotice queueId="q1" hasTemplate paused={false} {...props} />);
  await act(async () => { await Promise.resolve(); });
  return result;
};

beforeEach(() => {
  jest.resetAllMocks();
  diffMock.mockResolvedValue(diff(0) as any);
});

describe('TemplateChangesNotice (feature 173, US5)', () => {
  it('shows nothing when no change waits', async () => {
    const { container } = await renderNotice();

    expect(container).toBeEmptyDOMElement();
  });

  it('shows the count and the Review button when changes wait', async () => {
    diffMock.mockResolvedValue(diff(2) as any);
    await renderNotice();

    expect(await screen.findByText(/2 changes wait/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review changes' })).toBeInTheDocument();
  });

  it('uses the singular for one change', async () => {
    diffMock.mockResolvedValue(diff(1) as any);
    await renderNotice();

    expect(await screen.findByText(/1 change waits/)).toBeInTheDocument();
  });

  it('shows nothing and reads nothing for a queue with no linked template', async () => {
    const { container } = await renderNotice({ hasTemplate: false });

    expect(container).toBeEmptyDOMElement();
    expect(diffMock).not.toHaveBeenCalled();
  });

  it('shows "needs an upgrade" with the Review button for a template that needs an upgrade', async () => {
    diffMock.mockResolvedValue(diff(0, { needsUpgrade: true }) as any);
    await renderNotice();

    expect(await screen.findByText(/needs an upgrade/i)).toBeInTheDocument();
    expect(screen.queryByText(/changes? waits?/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review changes' })).toBeInTheDocument();
  });

  it('shows nothing when the read fails', async () => {
    diffMock.mockRejectedValue(new Error('down'));
    const { container } = await renderNotice();

    expect(container).toBeEmptyDOMElement();
  });

  it('opens the review with the pause state, and closes it', async () => {
    diffMock.mockResolvedValue(diff(1) as any);
    await renderNotice({ paused: true });

    fireEvent.click(await screen.findByRole('button', { name: 'Review changes' }));
    expect(screen.getByTestId('review-dialog-stub')).toHaveTextContent('paused:true');

    fireEvent.click(screen.getByRole('button', { name: 'Stub close' }));
    expect(screen.queryByTestId('review-dialog-stub')).not.toBeInTheDocument();
  });

  it('reads again after an apply, tells the parent, and keeps the review open until it closes', async () => {
    const onChanged = jest.fn();
    diffMock.mockResolvedValue(diff(1) as any);
    await renderNotice({ paused: true, onChanged });
    fireEvent.click(await screen.findByRole('button', { name: 'Review changes' }));

    diffMock.mockResolvedValue(diff(0) as any);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Stub apply' })); });

    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/change waits?|changes wait/)).not.toBeInTheDocument();
    expect(screen.getByTestId('review-dialog-stub')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Stub close' }));
    expect(screen.queryByTestId('review-dialog-stub')).not.toBeInTheDocument();
  });

  it('reads the differences again when the refresh key changes', async () => {
    const { rerender } = await renderNotice({ refreshKey: 0 });
    const calls = diffMock.mock.calls.length;
    diffMock.mockResolvedValue(diff(3) as any);

    rerender(<TemplateChangesNotice queueId="q1" hasTemplate paused={false} refreshKey={1} />);
    await act(async () => { await Promise.resolve(); });

    expect(diffMock.mock.calls.length).toBe(calls + 1);
    expect(await screen.findByText(/3 changes wait/)).toBeInTheDocument();
  });

  describe('polling', () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it('reads the differences again every 5 s', async () => {
      await renderNotice();
      const calls = diffMock.mock.calls.length;

      diffMock.mockResolvedValue(diff(2) as any);
      await act(async () => { jest.advanceTimersByTime(5000); });

      expect(diffMock.mock.calls.length).toBe(calls + 1);
      expect(await screen.findByText(/2 changes wait/)).toBeInTheDocument();
    });

    it('does not read while it is turned off', async () => {
      await renderNotice({ enabled: false });
      const calls = diffMock.mock.calls.length;

      await act(async () => { jest.advanceTimersByTime(15000); });

      expect(diffMock.mock.calls.length).toBe(calls);
    });
  });
});
