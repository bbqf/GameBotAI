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
const openMenu = (name = 'Daily') => fireEvent.click(screen.getByRole('button', { name: `More actions for ${name}` }));

beforeEach(() => {
  jest.resetAllMocks();
  pauseMock.mockResolvedValue({ operatorPause: 'pausing' } as any);
  resumeMock.mockResolvedValue({ resumed: true } as any);
});

describe('QueueRowActions slots for each state', () => {
  it('shows Start, a Stop that is off, Edit, and the menu button for a stopped queue', () => {
    renderActions(queue());

    expect(names()).toEqual(['Start Daily', 'Stop Daily', 'Edit Daily', 'More actions for Daily']);
    expect(screen.getByRole('button', { name: 'Stop Daily' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Start Daily' })).toBeEnabled();
  });

  it('shows Pause, Stop, Edit, and the menu button for a running queue', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'none' }));

    expect(names()).toEqual(['Pause Daily', 'Stop Daily', 'Edit Daily', 'More actions for Daily']);
    expect(screen.getByRole('button', { name: 'Stop Daily' })).toBeEnabled();
  });

  it('treats a running queue with no pause state as not paused', () => {
    renderActions(queue({ status: 'Running' }));

    expect(screen.getByRole('button', { name: 'Pause Daily' })).toBeInTheDocument();
  });

  it('shows Cancel pause in place of Pause while the queue is pausing', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'pausing' }));

    expect(names()).toEqual(['Cancel pause Daily', 'Stop Daily', 'Edit Daily', 'More actions for Daily']);
  });

  it('shows Resume in place of Pause while the queue is paused', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'paused' }));

    expect(names()).toEqual(['Resume Daily', 'Stop Daily', 'Edit Daily', 'More actions for Daily']);
  });

  it('gives each visible button the tooltip of the action alone', () => {
    renderActions(queue({ status: 'Running', operatorPause: 'paused' }));

    const tooltips = screen.getAllByRole('button').slice(0, 3).map((b) => b.getAttribute('title'));
    expect(tooltips).toEqual(['Resume', 'Stop', 'Edit']);
  });

  it('puts the queue name in every accessible name, so two rows stay apart', () => {
    renderActions(queue({ name: 'Arena farm' }));

    screen.getAllByRole('button').forEach((b) => expect(b.getAttribute('aria-label')).toMatch(/ Arena farm$/));
  });
});

describe('QueueRowActions menu', () => {
  it('has Duplicate and a Delete that is on for a stopped queue', () => {
    renderActions(queue());
    openMenu();

    const items = screen.getAllByRole('menuitem');
    expect(items.map((i) => i.textContent)).toEqual(['Duplicate', 'Delete']);
    expect(items[1]).not.toHaveAttribute('aria-disabled');
    expect(items[1]).toHaveClass('ui-menu__item--danger');
  });

  it('has a Delete that is off while the queue runs', () => {
    const h = renderActions(queue({ status: 'Running' }));
    openMenu();

    const del = screen.getByRole('menuitem', { name: 'Delete' });
    expect(del).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(del);
    expect(h.onDelete).not.toHaveBeenCalled();
  });

  it('calls Duplicate and Delete', () => {
    const h = renderActions(queue());
    openMenu();
    fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    openMenu();
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));

    expect(h.onDuplicate).toHaveBeenCalledWith('q1');
    expect(h.onDelete).toHaveBeenCalledWith('q1');
  });
});

describe('QueueRowActions behavior', () => {
  it('calls Start, Edit for a stopped queue', () => {
    const stopped = renderActions(queue());
    fireEvent.click(screen.getByRole('button', { name: 'Start Daily' }));
    fireEvent.click(screen.getByRole('button', { name: 'Edit Daily' }));

    expect(stopped.onStart).toHaveBeenCalledWith('q1');
    expect(stopped.onEdit).toHaveBeenCalledWith('q1');
  });

  it('calls Stop for a running queue and does not call it for a stopped queue', () => {
    const running = renderActions(queue({ status: 'Running' }));
    fireEvent.click(screen.getByRole('button', { name: 'Stop Daily' }));

    expect(running.onStop).toHaveBeenCalledWith('q1');
  });

  it('calls pauseQueue for Pause and tells the page', async () => {
    const h = renderActions(queue({ status: 'Running', operatorPause: 'none' }));

    fireEvent.click(screen.getByRole('button', { name: 'Pause Daily' }));

    await waitFor(() => expect(pauseMock).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(h.onChanged).toHaveBeenCalledTimes(1));
  });

  it('calls resumeQueue for Resume', async () => {
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

  it('uses a default text when the refusal has no message', async () => {
    pauseMock.mockRejectedValue('x');
    const h = renderActions(queue({ status: 'Running', operatorPause: 'none' }));

    fireEvent.click(screen.getByRole('button', { name: 'Pause Daily' }));

    await waitFor(() => expect(h.onError).toHaveBeenCalledWith('The request failed.'));
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