import React from 'react';
import { render, screen } from '@testing-library/react';
import { CommandForm, CommandFormValue } from '../../../components/commands/CommandForm';

const baseValue: CommandFormValue = {
  name: 'Zoom command',
  steps: [],
  detection: {
    referenceImageId: 'home_button',
    confidence: '0.8',
    offsetX: '0',
    offsetY: '0'
  }
};

describe('CommandForm zoom layout', () => {
  it('renders key fields at 125% zoom without missing controls', () => {
    render(
      <div style={{ width: '1280px', zoom: 1.25 }}>
        <CommandForm
          value={baseValue}
          actionOptions={[]}
          commandOptions={[]}
          onChange={() => undefined}
        />
      </div>
    );

    expect(screen.getByLabelText(/Name \*/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Reference image ID/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Confidence \(0-1\)/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset X/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset Y/i)).toBeInTheDocument();
  });

  it('renders key fields at 150% zoom without missing controls', () => {
    render(
      <div style={{ width: '1280px', zoom: 1.5 }}>
        <CommandForm
          value={baseValue}
          actionOptions={[]}
          commandOptions={[]}
          onChange={() => undefined}
        />
      </div>
    );

    expect(screen.getByLabelText(/Name \*/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Reference image ID/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Confidence \(0-1\)/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset X/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Offset Y/i)).toBeInTheDocument();
  });
});
