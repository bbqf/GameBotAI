import React, { useEffect, useState } from 'react';
import { List, ListItem } from '../components/List';
import { Dropdown } from '../components/Dropdown';
import { MultiSelect, MultiOption } from '../components/MultiSelect';
import { listTriggers, TriggerDto, createTrigger, TriggerCreate } from '../services/triggers';
import { listActions, ActionDto } from '../services/actions';
import { listCommands, CommandDto } from '../services/commands';
import { listSequences, SequenceDto } from '../services/sequences';

export const TriggersPage: React.FC = () => {
  const [items, setItems] = useState<ListItem[]>([]);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [criteriaText, setCriteriaText] = useState('');
  const [actionOptions, setActionOptions] = useState<MultiOption[]>([]);
  const [commandOptions, setCommandOptions] = useState<MultiOption[]>([]);
  const [sequenceOptions, setSequenceOptions] = useState<MultiOption[]>([]);
  const [selectedActions, setSelectedActions] = useState<string[]>([]);
  const [selectedCommands, setSelectedCommands] = useState<string[]>([]);
  const [selectedSequence, setSelectedSequence] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | undefined>(undefined);

  useEffect(() => {
    let mounted = true;
    listTriggers()
      .then((data: TriggerDto[]) => {
        if (!mounted) return;
        const mapped: ListItem[] = data.map((t) => ({
          id: t.id,
          name: t.name,
          details: {
            actions: t.actions?.length ?? 0,
            commands: t.commands?.length ?? 0,
            sequence: t.sequence ? 1 : 0
          }
        }));
        setItems(mapped);
      })
      .catch(() => setItems([]));
    Promise.all([listActions(), listCommands(), listSequences()])
      .then(([acts, cmds, seqs]: [ActionDto[], CommandDto[], SequenceDto[]]) => {
        if (!mounted) return;
        setActionOptions(acts.map((a) => ({ value: a.id, label: a.name })));
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
        setSequenceOptions(seqs.map((s) => ({ value: s.id, label: s.name })));
      })
      .catch(() => {
        setActionOptions([]);
        setCommandOptions([]);
        setSequenceOptions([]);
      });
    return () => {
      mounted = false;
    };
  }, []);

  return (
    <section>
      <h2>Triggers</h2>
      <div className="actions-header">
        <button onClick={() => setCreating(true)}>Create Trigger</button>
      </div>
      {creating && (
        <form
          className="create-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setError(undefined);
            let criteria: Record<string, unknown> | undefined = undefined;
            if (criteriaText.trim().length > 0) {
              try {
                criteria = JSON.parse(criteriaText);
              } catch {
                setError('Criteria must be valid JSON');
                return;
              }
            }
            const input: TriggerCreate = {
              name: name.trim(),
              criteria,
              actions: selectedActions.length ? selectedActions : undefined,
              commands: selectedCommands.length ? selectedCommands : undefined,
              sequence: selectedSequence
            };
            if (!input.name) {
              setError('Name is required');
              return;
            }
            try {
              await createTrigger(input);
              setCreating(false);
              setName('');
              setCriteriaText('');
              setSelectedActions([]);
              setSelectedCommands([]);
              setSelectedSequence(undefined);
              const data = await listTriggers();
              const mapped: ListItem[] = data.map((t) => ({
                id: t.id,
                name: t.name,
                details: {
                  actions: t.actions?.length ?? 0,
                  commands: t.commands?.length ?? 0,
                  sequence: t.sequence ? 1 : 0
                }
              }));
              setItems(mapped);
            } catch (err: any) {
              setError(err?.message ?? 'Failed to create trigger');
            }
          }}
        >
          <div>
            <label>Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <label>Criteria (JSON)</label>
            <textarea rows={4} value={criteriaText} onChange={(e) => setCriteriaText(e.target.value)} />
          </div>
          <div>
            <MultiSelect label="Actions" values={selectedActions} options={actionOptions} onChange={setSelectedActions} />
          </div>
          <div>
            <MultiSelect label="Commands" values={selectedCommands} options={commandOptions} onChange={setSelectedCommands} />
          </div>
          <div>
            <Dropdown label="Sequence" value={selectedSequence} options={sequenceOptions} onChange={setSelectedSequence} />
          </div>
          {error && <div className="form-error" role="alert">{error}</div>}
          <div className="form-actions">
            <button type="submit">Create</button>
            <button type="button" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </form>
      )}
      <List items={items} emptyMessage="No triggers found." />
    </section>
  );
};
