import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';
import * as updateService from '../services/update';

jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div>Commands page</div> }));

describe('Update area', () => {
  beforeEach(() => {
    jest.spyOn(updateService, 'getUpdateStatus').mockResolvedValue({ installedVersion: '1.8.0.1', canInstallHere: true });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('is a top-level tab and not an Authoring tab', async () => {
    render(<App />);
    expect(screen.getAllByRole('tab', { name: 'Update' })).toHaveLength(1);

    fireEvent.click(screen.getByRole('tab', { name: 'Update' }));

    expect(await screen.findByRole('heading', { name: 'Update', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check for Update' })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Backup & Restore' })).not.toBeInTheDocument();
  });
});