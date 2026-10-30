import React from 'react';
import { readFileSync } from 'fs';
import { join } from 'path';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { TemplateUpgradePanel } from '../TemplateUpgradePanel';
import { TemplateUpgradeNotice } from '../TemplateUpgradeNotice';
import {
  getQueueTemplate,
  previewQueueTemplateUpgrade,
  upgradeQueueTemplate,
  QueueTemplateDetail,
  TemplateUpgradePreview
} from '../../../services/queueTemplates';
import { ApiError } from '../../../lib/api';

jest.mock('../../../services/queueTemplates');

const previewMock = previewQueueTemplateUpgrade as jest.MockedFunction<typeof previewQueueTemplateUpgrade>;
const upgradeMock = upgradeQueueTemplate as jest.MockedFunction<typeof upgradeQueueTemplate>;
const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;

const preview = (over: Partial<TemplateUpgradePreview> = {}): TemplateUpgradePreview => ({
  entriesToChange: 3, runningMatch: [], unmatched: [], ...over
});

const template = (needsUpgrade: boolean): QueueTemplateDetail => ({
  id: 't1', name: 'Daily', entryCount: 3, needsUpgrade, createdAt: null, updatedAt: null, entries: []
});

const flush = async () => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};

describe('TemplateUpgradePanel', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('shows what the upgrade does before it changes anything', async () => {
    previewMock.mockResolvedValue(preview());

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));

    expect(await screen.findByTestId('upgrade-preview')).toHaveTextContent('3 entries get an entry ID');
    expect(previewMock).toHaveBeenCalledWith('t1');
    expect(upgradeMock).not.toHaveBeenCalled();
  });

  it('lists the entries of running queues that get the same IDs', async () => {
    previewMock.mockResolvedValue(preview({
      runningMatch: [
        { queueId: 'q1', position: 0, sequenceName: 'Alpha' },
        { queueId: 'q1', position: 1, sequenceName: 'Bravo' }
      ]
    }));

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));

    const matches = await screen.findByTestId('upgrade-running-match');
    expect(matches).toHaveTextContent('2 entries of running queues');
    expect(matches).toHaveTextContent('Their schedules do not change');
    expect(screen.getAllByTestId('upgrade-match-row')).toHaveLength(2);
  });

  it('refuses to confirm while a running entry has no match', async () => {
    previewMock.mockResolvedValue(preview({
      unmatched: [{ queueId: 'q1', position: 1, runningSequence: 'Bravo', templateSequence: 'Charlie', reason: 'The sequence of the running entry is not the sequence of the template entry.' }]
    }));

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));

    const block = await screen.findByTestId('upgrade-unmatched');
    expect(block).toHaveTextContent('Stop the queue or reload the template');
    expect(block).toHaveTextContent('The sequence of the running entry');
    expect(screen.queryByRole('button', { name: 'Confirm upgrade' })).not.toBeInTheDocument();
  });

  it('upgrades after the operator confirms, and says how many entries changed', async () => {
    previewMock.mockResolvedValue(preview());
    upgradeMock.mockResolvedValue({ entriesChanged: 3 });
    const onDone = jest.fn();

    render(<TemplateUpgradePanel templateId="t1" onDone={onDone} />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm upgrade' }));

    expect(await screen.findByTestId('upgrade-done')).toHaveTextContent('3 entries got an entry ID');
    expect(upgradeMock).toHaveBeenCalledWith('t1', false);
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('sends confirm when a running queue uses the template', async () => {
    previewMock.mockResolvedValue(preview({ runningMatch: [{ queueId: 'q1', position: 0, sequenceName: 'Alpha' }] }));
    upgradeMock.mockResolvedValue({ entriesChanged: 3 });

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm upgrade' }));

    await waitFor(() => expect(upgradeMock).toHaveBeenCalledWith('t1', true));
  });

  it('says that a repeat changed 0 entries', async () => {
    previewMock.mockResolvedValue(preview({ entriesToChange: 0 }));
    upgradeMock.mockResolvedValue({ entriesChanged: 0 });

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm upgrade' }));

    expect(await screen.findByTestId('upgrade-done')).toHaveTextContent('0 entries changed');
  });

  it('shows the message and the hint of a refusal', async () => {
    previewMock.mockRejectedValue(new ApiError(404, 'Queue template not found', undefined, { error: { code: 'not_found', message: 'Queue template not found', hint: 'Reload the list of templates.' } }));

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Queue template not found');
    expect(alert).toHaveTextContent('Reload the list of templates.');
  });

  it('lets the operator cancel the preview', async () => {
    previewMock.mockResolvedValue(preview());

    render(<TemplateUpgradePanel templateId="t1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Upgrade template' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    expect(screen.queryByTestId('upgrade-preview')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Upgrade template' })).toBeInTheDocument();
    expect(upgradeMock).not.toHaveBeenCalled();
  });
});

describe('TemplateUpgradeNotice', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('marks a template that needs an upgrade and offers the upgrade', async () => {
    getTemplateMock.mockResolvedValue(template(true));

    render(<TemplateUpgradeNotice templateId="t1" />);

    expect(await screen.findByTestId('needs-upgrade-mark')).toHaveTextContent('Needs upgrade');
    expect(screen.getByRole('button', { name: 'Upgrade template' })).toBeInTheDocument();
  });

  it('shows nothing for a template that has its IDs', async () => {
    getTemplateMock.mockResolvedValue(template(false));

    const { container } = render(<TemplateUpgradeNotice templateId="t1" />);
    await flush();

    expect(container).toBeEmptyDOMElement();
  });

  it('shows nothing when the template cannot be read', async () => {
    getTemplateMock.mockRejectedValue(new Error('gone'));

    const { container } = render(<TemplateUpgradeNotice templateId="t1" />);
    await flush();

    expect(container).toBeEmptyDOMElement();
  });

  it('goes away after the upgrade', async () => {
    getTemplateMock.mockResolvedValueOnce(template(true)).mockResolvedValue(template(false));
    previewMock.mockResolvedValue(preview());
    upgradeMock.mockResolvedValue({ entriesChanged: 3 });
    const onUpgraded = jest.fn();

    render(<TemplateUpgradeNotice templateId="t1" onUpgraded={onUpgraded} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Upgrade template' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm upgrade' }));

    await waitFor(() => expect(screen.queryByTestId('needs-upgrade-mark')).not.toBeInTheDocument());
    expect(onUpgraded).toHaveBeenCalledTimes(1);
  });
});

describe('TemplateUpgradePanel styles', () => {
  const css = readFileSync(join(__dirname, '..', 'TemplateUpgradePanel.css'), 'utf8');

  it('gives each button a hit area of at least 44 px and wraps text for a phone', () => {
    expect(css).toMatch(/min-height:\s*44px/);
    expect(css).toMatch(/font-size:\s*16px/);
    expect(css).toMatch(/overflow-wrap:\s*anywhere/);
  });
});
