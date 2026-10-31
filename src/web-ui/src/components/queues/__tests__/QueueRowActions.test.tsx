import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueueRowActions } from '../QueueRowActions';
import { pauseQueue, resumeQueue } from '../../../services/queues';

jest.mock('../../../services/queues');

const pauseMock = pauseQueue as jest.MockedFunction<typeof pauseQueue>;
const resumeMock = resumeQueue as jest.MockedFunction<typeof resumeQueue>;

type Props = React.ComponentProps<typeof QueueRowActions>;

const queue = (over: Partial<Props['queue']> = {}): Props['queue'] => ({
  id: 'q1', name: 'Daily', status: 'Stopped', ...over,
});

const handlers = () => ({
  onStart: jest.fn(), onStop: jest.fn(), onEdit: jest.fn(), onDuplicate: jest.fn(), onDelete: jest.fn(),
  onChanged: jest.fn(), onError: jest.fn(),
});

const renderActions = (q: Props['queue'], h = handlers()) => {
  render(<QueueRowActions queue={q} {...h} />);
  return h;
};

const names = () => screen.getAllByRole('button').map((b) => b.getAttribute('aria-label'));

beforeEach(() => {
  jest.resetAllMocks();
  pauseMock.mockResolvedValue({ operatorPause: 'pausing' } as any);
  resumeMock.mockResolvedValue({ resumed: true } as any);
});

describe('QueueRowActions icons for each state (feature 173, US3)', () => {
  it('shows Start, Edit, Duplicate, and Delete for a stopped queue', () => {
    renderActions(queue());

    expect(names()).toEqual(['Start Daily', 'Edit Daily', 'Duplicate Daily', 'Delete Daily']);
    expect(screen.getByRole('button', { name: 'Delete Daily' })).toBeEnabled();
  });

  it('shows Pause, Stop, Edit, Duplicate, and a disabled Delete for a running queue', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'none' }));

    expect(names()).toEqual(['Pause Daily', 'Stop Daily', 'Edit Daily', 'Duplicate Daily', 'Delete Daily']);
    expect(screen.getByRole('button', { name: 'Delete Daily' })).toBeDisabled();
  });

  it('treats a running queue with no pause state as not paused', () => {
    renderActions(queue({ status: 'Running' }));

    expect(screen.getByRole('button', { name: 'Pause Daily' })).toBeInTheDocument();
  });

  it('shows Cancel pause in place of Pause while the queue is pausing', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'pausing' }));

    expect(names()).toEqual(['Cancel pause Daily', 'Stop Daily', 'Edit Daily', 'Duplicate Daily', 'Delete Daily']);
    expect(screen.getByRole('button', { name: 'Delete Daily' })).toBeDisabled();
  });

  it('shows Resume in place of Pause while the queue is paused', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'paused' }));

    expect(names()).toEqual(['Resume Daily', 'Stop Daily', 'Edit Daily', 'Duplicate Daily', 'Delete Daily']);
  });

  it('gives each button the tooltip of the action alone', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'paused' }));

    const tooltips = screen.getAllByRole('button').map((b) => b.getAttribute('title'));
    expect(tooltips).toEqual(['Resume', 'Stop', 'Edit', 'Duplicate', 'Delete']);
  });

  it('puts the queue name in every accessible name, so two rows stay apart', () => {
    renderActions(queue({ name: 'Arena farm' }));

    screen.getAllByRole('button').forEach((b) => expect(b.getAttribute('aria-label')).toMatch(/ Arena farm$/));
  });

  it('marks Delete as the destructive action', () => {
    renderActions(queue());

    expect(screen.getByRole('button', { name: 'Delete Daily' })).toHaveClass('icon-button--destructive');
    expect(screen.getByRole('button', { name: 'Edit Daily' })).not.toHaveClass('icon-button--destructive');
  });
});

describe('QueueRowActions behavior', () => {
  it('does what the text buttons did: Start, Stop, Edit, Duplicate, and Delete call the page', () => {
    const stopped = renderActions(queue());
    fireEvent.click(screen.getByRole('button', { name: 'Start Daily' }));
    fireEvent.click(screen.getByRole('button', { name: 'Edit Daily' }));
    fireEvent.click(screen.getByRole('button', { name: 'Duplicate Daily' }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete Daily' }));

    expect(stopped.onStart).toHaveBeenCalledWith('q1');
    expect(stopped.onEdit).toHaveBeenCalledWith('q1');
    expect(stopped.onDuplicate).toHaveBeenCalledWith('q1');
    expect(stopped.onDelete).toHaveBeenCalledWith('q1');
  });

  it('calls Stop for a running queue', () => {
    const running = renderActions(queue({ status: 'Running' }));

    fireEvent.click(screen.getByRole('button', { name: 'Stop Daily' }));

    expect(running.onStop).toHaveBeenCalledWith('q1');
  });

  it('does not call Delete while the queue runs', () => {
    const running = renderActions(queue({ status: 'Running' }));

    fireEvent.click(screen.getByRole('button', { name: 'Delete Daily' }));

    expect(running.onDelete).not.toHaveBeenCalled();
  });

  it('calls pauseQueue for Pause and tells the page', async () => {
    const h = renderActions(queue({ status: 'Running', operatorPause: 'none' }));

    fireEvent.click(screen.getByRole('button', { name: 'Pause Daily' }));

    await waitFor(() => expect(pauseMock).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(h.onChanged).toHaveBeenCalledTimes(1));
  });

  it('calls resumeQueue for Resume and for Cancel pause', async () => {
    const paused = renderActions(queue({ status: 'Running', operatorPause: 'paused' }));
    fireEvent.click(screen.getByRole('button', { name: 'Resume Daily' }));
    await waitFor(() => expect(resumeMock).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(paused.onChanged).toHaveBeenCalledTimes(1));
  });

  it('calls resumeQueue for Cancel pause', async () => {
    const pausing = renderActions(queue({ status: 'Running', operatorPause: 'pausing' }));

    fireEvent.click(screen.getByRole('button', { name: 'Cancel pause Daily' }));

    await waitFor(() => expect(resumeMock).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(pausing.onChanged).toHaveBeenCalledTimes(1));
  });

  it('shows the refusal through onError and does not tell the page that it changed', async () => {
    pauseMock.mockRejectedValue(new Error('The queue is not running.'));
    const h = renderActions(queue({ status: 'Running', operatorPause: 'none' }));

    fireEvent.click(screen.getByRole('button', { name: 'Pause Daily' }));

    await waitFor(() => expect(h.onError).toHaveBeenCalledWith('The queue is not running.'));
    expect(h.onChanged).not.toHaveBeenCalled();
  });

  it('turns the pause button off while its call runs, so a second click does nothing', async () => {
    let finish: (value: any) => void = () => undefined;
    pauseMock.mockReturnValue(new Promise((resolve) => { finish = resolve; }) as any);
    renderActions(queue({ status: 'Running', operatorPause: 'none' }));
    const button = screen.getByRole('button', { name: 'Pause Daily' });

    fireEvent.click(button);
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(pauseMock).toHaveBeenCalledTimes(1);

    finish({ operatorPause: 'pausing' });
    await waitFor(() => expect(button).toBeEnabled());
  });
});
