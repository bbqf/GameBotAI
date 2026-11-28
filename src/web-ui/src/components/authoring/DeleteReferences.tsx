import React from 'react';
import type { DeleteConflict } from './useDeleteFlow';

/** The content of a delete dialog after a 409 answer: the message and the items that use the target. */
export const DeleteReferences: React.FC<{ conflict: DeleteConflict }> = ({ conflict }) => {
  const groups = Object.entries(conflict.references ?? {});
  return (
    <>
      <p role="alert">{conflict.message}</p>
      {groups.length > 0 && (
        <div className="ui-references">
          <strong>Referenced by:</strong>
          {groups.map(([key, items]) => (
            <div key={key}>
              <span>{key} ({items.length})</span>
              <ul>
                {items.map((r) => (
                  <li key={r.id}>
                    {r.name} <span className="muted">[{r.id}]</span>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </>
  );
};
