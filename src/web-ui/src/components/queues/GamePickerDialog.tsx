import React, { useEffect, useState } from 'react';
import { GameDto, listGames } from '../../services/games';

type GamePickerDialogProps = {
  open: boolean;
  onSelect: (gameId: string) => void;
  onClose: () => void;
};

export const GamePickerDialog: React.FC<GamePickerDialogProps> = ({ open, onSelect, onClose }) => {
  const [games, setGames] = useState<GameDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);

  useEffect(() => {
    if (!open) return;
    setLoading(true);
    listGames()
      .then((data) => { setGames(data); setError(undefined); })
      .catch((err: unknown) => { setGames([]); setError(err instanceof Error ? err.message : 'Failed to load games'); })
      .finally(() => setLoading(false));
  }, [open]);

  if (!open) return null;

  return (
    <section className="queue-template-section" aria-label="Link game">
      <h4>Link game</h4>
      {error && <div className="form-error" role="alert">{error}</div>}
      {loading && <div className="form-hint">Loading…</div>}
      {!loading && games.length === 0 && (
        <div className="form-hint" role="status">No games configured yet.</div>
      )}
      <ul className="template-list">
        {games.map((g) => (
          <li key={g.id} className="template-row" data-testid="game-row">
            <span className="template-name">{g.name}</span>
            <span className="template-actions">
              <button type="button" onClick={() => onSelect(g.id)}>Select</button>
            </span>
          </li>
        ))}
      </ul>
      <div className="queue-template-section-actions">
        <button type="button" className="btn btn-secondary" onClick={onClose}>Close</button>
      </div>
    </section>
  );
};
