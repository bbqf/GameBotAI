import React from 'react';

type Props = {
  label: string;
  htmlFor: string;
  className?: string;
  children: React.ReactNode;
};

export const FormField: React.FC<Props> = ({ label, htmlFor, className, children }) => {
  return (
    <div className={"row" + (className ? ` ${className}` : '')}>
      <label htmlFor={htmlFor}>{label}</label>
      {children}
    </div>
  );
};