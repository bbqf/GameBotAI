import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueueTemplateControls } from '../QueueTemplateControls';
import { listQueueTemplates } from '../../../services/queueTemplates';

jest.mock('../../../services/queueTemplates');

const listMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;

beforeEach(() => {
  jest.resetAllMocks();
  listMock.mockResolvedValue([] as any);
});

const renderControls = (overrides: Partial<React.ComponentProps<typeof QueueTemplateControls>> = {}) => {
  const onSaveTemplate = overrides.onSaveTemplate ?? jest.fn().mockResolvedValue(undefined);
  const onLoadTemplate = overrides.onLoadTemplate ?? jest.fn();
  const onReload = overrides.onReload ?? jest.fn();
  render(
    <QueueTemplateControls
      associatedTemplateName={overrides.associatedTemplateName}
      status={overrides.status ?? 'Stopped'}
      onSaveTemplate={onSaveTemplate}
      onLoadTemplate={onLoadTemplate}
      onReload={onReload}
    />
  );
  return { onSaveTemplate, onLoadTemplate, onReload };
};

describe('QueueTemplateControls', () => {
  it('renders both sections closed by default', () => {
    renderControls();
    expect(screen.queryByRole('region', { name: 'Save template' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Load template' })).not.toBeInTheDocument();
  });

  it('shows the (no template) placeholder and opens the Load section when clicked', async () => {
    renderControls();
    fireEvent.click(screen.getByText('(no template)'));
    expect(await screen.findByRole('region', { name: 'Load template' })).toBeInTheDocument();
  });

  it('shows the associated template name on the name button', () => {
    renderControls({ associatedTemplateName: 'Daily Farm' });
    expect(screen.getByText('Daily Farm')).toBeInTheDocument();
  });

  it('opens the Save section when Save Template is clicked', () => {
    renderControls();
    fireEvent.click(screen.getByText('Save Template'));
    expect(screen.getByRole('region', { name: 'Save template' })).toBeInTheDocument();
  });

  it('keeps at most one section open (opening one closes the other)', async () => {
    renderControls();
    fireEvent.click(screen.getByText('Save Template'));
    expect(screen.getByRole('region', { name: 'Save template' })).toBeInTheDocument();

    fireEvent.click(screen.getByText('(no template)'));
    expect(await screen.findByRole('region', { name: 'Load template' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Save template' })).not.toBeInTheDocument();
  });

  it('collapses the open section when dismissed', () => {
    renderControls();
    fireEvent.click(screen.getByText('Save Template'));
    const section = screen.getByRole('region', { name: 'Save template' });
    fireEvent.click(within(section).getByText('Cancel'));
    expect(screen.queryByRole('region', { name: 'Save template' })).not.toBeInTheDocument();
  });

  it('disables Reload Template when no template is associated', () => {
    renderControls();
    expect(screen.getByText('Reload Template')).toBeDisabled();
  });

  it('disables Reload Template while the queue is running', () => {
    renderControls({ associatedTemplateName: 'Daily Farm', status: 'Running' });
    expect(screen.getByText('Reload Template')).toBeDisabled();
  });

  it('fires onReload when enabled and clicked', () => {
    const { onReload } = renderControls({ associatedTemplateName: 'Daily Farm' });
    fireEvent.click(screen.getByText('Reload Template'));
    expect(onReload).toHaveBeenCalled();
  });
});
