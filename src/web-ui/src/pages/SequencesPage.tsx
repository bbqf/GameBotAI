import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { MultiSelect, MultiOption } from '../components/MultiSelect';
import { listSequences, SequenceDto, createSequence, SequenceCreate } from '../services/sequences';
import { listCommands, CommandDto } from '../services/commands';

export const SequencesPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [commandOptions, setCommandOptions] = useState<MultiOption[]>([]);
  const [selectedCommands, setSelectedCommands] = useState<string[]>([]);
  const [error, setError] = useState<string | undefined>(undefined);

  useEffect(() => {
    let mounted = true;
    listSequences()
      .then((data: SequenceDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((s) => ({
          id: s.id,
          name: s.name,
          details: { steps: s.steps?.length ?? 0 }
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]));
    listCommands()
      .then((cmds: CommandDto[]) => {
        if (!mounted) return;
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
      })
      .catch(() => setCommandOptions([]));
    return () => {
      mounted = false;
    };
  }, []);

  return (
    <section>
      <h2>Sequences</h2>
      <div className="actions-header">
        <button onClick={() => setCreating(true)}>Create Sequence</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            const input: SequenceCreate = { name: name.trim(), steps: selectedCommands.length ? selectedCommands : [] };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await createSequence(input);
              setCreating(false);
              setName('');
              setSelectedCommands([]);
              const data = await listSequences();
              const mapped: ListItem[] = data.map((s) => ({
                id: s.id,
                name: s.name,
                details: { steps: s.steps?.length ?? 0 }
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to create sequence');
            }
          }}
        >
          <div>
            <label>Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <MultiSelect label="Commands (steps)" values={selectedCommands} options={commandOptions} onChange={setSelectedCommands} />
          </div>
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="form-actions">
            <button type="submit">Create</button>
            <button type="button" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </form>
      )}
      <List items={items} emptyMessage="No sequences found." />
    </section>
  );
};
