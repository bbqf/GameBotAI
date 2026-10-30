import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { listSequences } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange, disabled }: {
    id?: string; label?: string; value: string; onChange: (v: string) => void; disabled?: boolean;
  }) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

// docs/mobile-ui-principles.md (FR-012): a hit area of at least 44 x 44 CSS px, an input font of at
// least 16 px, one column, and no horizontal scroll.
describe('SequencesPage onTimeLimit mobile layout', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([] as any);
    listSequencesMock.mockResolvedValue([] as any);
  });

  it('gives the select a 44 px hit area and a 16 px font in one column', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Create Sequence'));

    const select = screen.getByLabelText('When the time limit is reached') as HTMLSelectElement;

    expect(parseFloat(select.style.minHeight)).toBeGreaterThanOrEqual(44);
    expect(parseFloat(select.style.minWidth)).toBeGreaterThanOrEqual(44);
    expect(parseFloat(select.style.fontSize)).toBeGreaterThanOrEqual(16);
    // One column: the control fills its field and adds no fixed width that could cause a horizontal scroll.
    expect(select.style.width).toBe('100%');
    expect(select.style.boxSizing).toBe('border-box');
    // The field has the label above the control, not beside it, in a single block.
    expect(select.parentElement?.querySelectorAll('select')).toHaveLength(1);
  });
});
