namespace GameBot.Domain.Games;

public interface IGameRepository {
  Task<GameArtifact> AddAsync(GameArtifact artifact, CancellationToken ct = default);
  Task<GameArtifact?> GetAsync(string id, CancellationToken ct = default);
  Task<IReadOnlyList<GameArtifact>> ListAsync(CancellationToken ct = default);
}
