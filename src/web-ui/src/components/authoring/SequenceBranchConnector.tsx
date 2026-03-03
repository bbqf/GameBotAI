import React from 'react';

type Option = {
  value: string;
  label: string;
};

type SequenceBranchConnectorProps = {
  options: Option[];
  trueTargetId: string;
  falseTargetId: string;
  onTrueTargetChange: (value: string) => void;
  onFalseTargetChange: (value: string) => void;
};

export const SequenceBranchConnector: React.FC<SequenceBranchConnectorProps> = ({
  options,
  trueTargetId,
  falseTargetId,
  onTrueTargetChange,
  onFalseTargetChange
}) => {
  return (
    <div className="field">
      <label htmlFor="condition-true-target">True Target</label>
      <select
        id="condition-true-target"
        aria-label="True Target"
        value={trueTargetId}
        onChange={(event) => onTrueTargetChange(event.target.value)}
      >
        <option value="">Select true target</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>

      <label htmlFor="condition-false-target">False Target</label>
      <select
        id="condition-false-target"
        aria-label="False Target"
        value={falseTargetId}
        onChange={(event) => onFalseTargetChange(event.target.value)}
      >
        <option value="">Select false target</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>
    </div>
  );
};
