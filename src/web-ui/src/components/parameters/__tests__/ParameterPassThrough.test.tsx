import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ParameterPassThrough } from '../ParameterPassThrough';
import { ParameterBindingForm } from '../ParameterBindingForm';
import type { ParameterBinding, ParameterDeclaration } from '../types';

const target: ParameterDeclaration = {
  name: 'mode', type: 'text', kind: 'choice', required: false,
  options: [{ label: 'Fast', value: 'f' }],
};

const sequenceDeclarations: ParameterDeclaration[] = [
  { name: 'seqMode', type: 'text', label: 'Sequence mode', required: false },
  { name: 'seqCount', type: 'number', required: false },
];

describe('ParameterPassThrough', () => {
  it('offers only sequence parameters of the same type', () => {
    render(<ParameterPassThrough declaration={target} sequenceDeclarations={sequenceDeclarations} value="" onChange={jest.fn()} />);

    const select = screen.getByRole('combobox');
    const options = Array.from((select as HTMLSelectElement).options).map((o) => o.text);
    expect(options).toEqual(['Enter a value here', 'Sequence mode']);
  });

  it('stores {{name}} when a sequence parameter is selected', () => {
    const onChange = jest.fn();
    render(<ParameterPassThrough declaration={target} sequenceDeclarations={sequenceDeclarations} value="" onChange={onChange} />);

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'seqMode' } });

    expect(onChange).toHaveBeenCalledWith('{{seqMode}}');
  });

  it('has an accessible name and renders nothing when no parameter fits', () => {
    const { container, rerender } = render(
      <ParameterPassThrough declaration={target} sequenceDeclarations={sequenceDeclarations} value="" onChange={jest.fn()} />,
    );
    expect(screen.getByRole('combobox', { name: /Use a parameter of this sequence/ })).toBeInTheDocument();

    rerender(<ParameterPassThrough declaration={target} sequenceDeclarations={[]} value="" onChange={jest.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('ParameterBindingForm with pass-through', () => {
  const Harness: React.FC = () => {
    const [bindings, setBindings] = React.useState<ParameterBinding[]>([{ name: 'mode', value: '' }]);
    return (
      <>
        <ParameterBindingForm
          declarations={[target]}
          bindings={bindings}
          onChange={setBindings}
          sequenceDeclarations={sequenceDeclarations}
        />
        <output data-testid="stored">{bindings[0]?.value}</output>
      </>
    );
  };

  it('hides the option buttons and checks no range or list for a stored placeholder', () => {
    render(<Harness />);
    expect(screen.getByRole('radio', { name: 'Fast' })).toBeInTheDocument();

    fireEvent.change(screen.getByRole('combobox', { name: /Use a parameter of this sequence/ }), { target: { value: 'seqMode' } });

    expect(screen.getByTestId('stored')).toHaveTextContent('{{seqMode}}');
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('is not shown on a form without sequence declarations', () => {
    render(<ParameterBindingForm declarations={[target]} bindings={[]} onChange={jest.fn()} />);

    expect(screen.queryByRole('combobox', { name: /Use a parameter of this sequence/ })).not.toBeInTheDocument();
  });
});
